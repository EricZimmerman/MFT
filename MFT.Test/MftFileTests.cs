using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MFT.Other;
using NUnit.Framework;
using static MFT.Test.RecordBuilder;

namespace MFT.Test;

/// <summary>Whole $MFT files built from records: header checks, partial records, directory cycles</summary>
[TestFixture]
public class MftFileTests
{
    private string _file;

    [SetUp]
    public void SetUp()
    {
        _file = Path.Combine(Path.GetTempPath(), $"mft-test-{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        File.Delete(_file);
    }

    private Mft Load(byte[] bytes)
    {
        File.WriteAllBytes(_file, bytes);
        var task = Task.Run(() => MftFile.Load(_file, false));
        task.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue("loading must finish");
        return task.Result;
    }

    //the root (5) has sequence number 5, the others 1
    private static ushort Seq(uint entry) => (ushort)(entry == 5 ? 5 : 1);

    private static byte[] Directory(uint entry, uint parent, string name) =>
        Record(entry, 3, new[] { StandardInfo(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)), FileName(parent, Seq(parent), name) });

    private static byte[] File_(uint entry, uint parent, string name) =>
        Record(entry, 1, new[] { StandardInfo(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)), FileName(parent, Seq(parent), name) });

    private static byte[] Volume(params byte[][] more)
    {
        var records = new byte[8][];
        for (uint i = 0; i < 8; i++)
        {
            records[i] = i == 5
                ? Record(5, 3, new[] { StandardInfo(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)), FileName(5, 5, ".") }, sequence: 5)
                : SystemRecord(i, $"$sys{i}");
        }

        return Mft(records.Concat(more).ToArray());
    }

    [Test]
    public void Paths()
    {
        var mft = Load(Volume(Directory(8, 5, "dir"), File_(9, 8, "file.txt")));

        var file = mft.FileRecords["00000009-00000001"];
        mft.GetFullParentPath(file.GetFileNameAttributeFromFileRecord().FileInfo.ParentMftRecord.GetKey()).Should()
            .Be(@".\dir");
        mft.GetDirectoryContents("00000008-00000001").Should().ContainSingle().Which.FileName.Should().Be("file.txt");
    }

    [Test]
    public void DirectoryEntriesAndSubdirectories()
    {
        //10 has two names in 8 and one in 9; 12 is a deleted directory in 8
        var mft = Load(Volume(Directory(8, 5, "dir"), Directory(9, 8, "sub"),
            Record(10, 1, new[]
            {
                StandardInfo(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)), FileName(8, 1, "a.txt"), FileName(8, 1, "b.txt"),
                FileName(9, 1, "c.txt")
            }),
            File_(11, 9, "d.txt"),
            Record(12, 2, new[] { StandardInfo(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)), FileName(8, 1, "gone") }, sequence: 2)));

        var entries = mft.GetDirectoryEntries("00000008-00000001");
        entries.Select(t => t.FileName).Should().BeEquivalentTo("sub", "a.txt", "b.txt", "gone");
        entries.Should().BeSubsetOf(mft.GetDirectoryContents("00000008-00000001")).And.HaveCount(4);

        entries.Single(t => t.FileName == "sub").Record.Should().BeSameAs(mft.FileRecords["00000009-00000001"]);
        entries.Single(t => t.FileName == "a.txt").Record.Should().BeSameAs(mft.FileRecords["0000000A-00000001"]);
        entries.Single(t => t.FileName == "b.txt").Record.Should().BeSameAs(mft.FileRecords["0000000A-00000001"]);
        var gone = entries.Single(t => t.FileName == "gone");
        gone.Record.Should().BeSameAs(mft.FreeFileRecords["0000000C-00000001"]);
        gone.Key.Should().Be("12-1");
        gone.IsDirectory.Should().BeTrue();

        mft.GetDirectoryEntries("00000009-00000001").Select(t => t.FileName).Should().BeEquivalentTo("c.txt", "d.txt");
        //the set itself is not handed out
        entries.Should().NotBeAssignableTo<System.Collections.Generic.ICollection<ParentMapEntry>>();
        mft.GetDirectoryEntries("00000063-00000001").Should().BeEmpty();
        mft.GetDirectoryEntries(null).Should().BeEmpty();

        mft.HasSubdirectories("00000005-00000005").Should().BeTrue();
        mft.HasSubdirectories("00000008-00000001").Should().BeTrue();
        mft.HasSubdirectories("00000009-00000001").Should().BeFalse();
        mft.HasSubdirectories("0000000A-00000001").Should().BeFalse();
        mft.HasSubdirectories("00000063-00000001").Should().BeFalse();
        mft.HasSubdirectories(null).Should().BeFalse();

        new ParentMapEntry("x", "1-1", false).Record.Should().BeNull();
    }

    [Test]
    public void DirectoryCycleDoesNotHang()
    {
        //8's parent is 9 and 9's parent is 8: this looped forever
        var mft = Load(Volume(Directory(8, 9, "a"), Directory(9, 8, "b"), File_(10, 8, "file.txt")));

        var path = mft.GetFullParentPath("00000008-00000001");

        path.Should().Contain("PathUnknown");
    }

    [Test]
    public void PartialLastRecord()
    {
        var bytes = Volume(File_(8, 5, "whole.txt"));
        bytes = bytes.Concat(File_(9, 5, "partial.txt").Take(600)).ToArray();

        var mft = Load(bytes);

        mft.FileRecords.Should().ContainKey("00000008-00000001");
        //the rest of the partial record is zeros, not the previous record's bytes
        var partial = mft.FileRecords.Values.Concat(mft.FreeFileRecords.Values).Single(t => t.Offset == 9 * 1024);
        partial.FixupOk.Should().BeFalse();
    }

    [TestCase(0)]
    [TestCase(-1024)]
    [TestCase(1000)]
    [TestCase(0x100000)]
    public void BadRecordSizeInTheHeader(int size)
    {
        //a size of 0 used to loop forever
        var bytes = Volume();
        BitConverter.GetBytes(size).CopyTo(bytes, 0x1C);

        File.WriteAllBytes(_file, bytes);
        FluentActions.Invoking(() => MftFile.Load(_file, false)).Should().Throw<Exception>()
            .WithMessage("*record size*");
    }

    [Test]
    public void NotAnMft()
    {
        File.WriteAllBytes(_file, new byte[4096]);
        FluentActions.Invoking(() => MftFile.Load(_file, false)).Should().Throw<Exception>()
            .WithMessage("Invalid header! Expected 'FILE' Signature.");
    }

    [Test]
    public void ExtensionRecordAttributesJoinTheBase()
    {
        var extension = Record(9, 1, new[] { DataResident(Bytes(1, 2, 3), "ads") }, baseRecord: 8 | 1UL << 48);

        var mft = Load(Volume(File_(8, 5, "base.txt"), extension));

        var baseRecord = mft.FileRecords["00000008-00000001"];
        baseRecord.GetAlternateDataStreams().Should().ContainSingle().Which.Name.Should().Be("ads");
    }

    [Test]
    public void CorruptedMftLoads()
    {
        var bytes = File.ReadAllBytes(MftTests.TestFile("xw", "$MFT"));
        var random = new Random(7);
        for (var i = 0; i < 3000; i++)
        {
            var at = random.Next(1024, bytes.Length);
            bytes[at] = (byte)random.Next(256);
        }

        var mft = Load(bytes);

        mft.FileRecords.Count.Should().BeGreaterThan(300);
        foreach (var fr in mft.FileRecords.Values)
        {
            var fn = fr.GetFileNameAttributeFromFileRecord();
            if (fn != null)
            {
                _ = mft.GetFullParentPath(fn.FileInfo.ParentMftRecord.GetKey());
            }
        }
    }
}
