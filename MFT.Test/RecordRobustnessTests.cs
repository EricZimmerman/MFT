using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MFT.Attributes;
using MFT.Other;
using NUnit.Framework;
using static MFT.Test.RecordBuilder;

namespace MFT.Test;

/// <summary>
///     Built and damaged FILE records: parsing keeps what it can, reports the rest in ParseErrors, and never hangs.
///     Add regression tests here
/// </summary>
[TestFixture]
public class RecordRobustnessTests
{
    private static readonly DateTime Created = new(2021, 6, 1, 12, 30, 45, DateTimeKind.Utc);

    private static FileRecord Parse(byte[] record, long offset = 0)
    {
        var task = Task.Run(() => new FileRecord(record, offset, false));
        task.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue("parsing must finish");
        return task.Result;
    }

    private static byte[] Simple(params byte[][] extra) =>
        Record(70, 1, new[] { StandardInfo(Created), FileName(5, 5, "test.txt") }.Concat(extra));

    [Test]
    public void BuiltRecord()
    {
        var fr = Parse(Simple(DataResident(Bytes(1, 2, 3))));

        fr.ParseErrors.Should().BeEmpty();
        fr.FixupOk.Should().BeTrue();
        fr.EntryNumber.Should().Be(70);
        fr.IsDeleted().Should().BeFalse();
        fr.Attributes.Select(t => t.AttributeType).Should().Equal(AttributeType.StandardInformation,
            AttributeType.FileName, AttributeType.Data);
        fr.Attributes.OfType<StandardInfo>().Single().CreatedOn.Should().Be(new DateTimeOffset(Created));
        fr.Attributes.OfType<StandardInfo>().Single().SecurityId.Should().Be(0x100);
        var fn = fr.Attributes.OfType<FileName>().Single().FileInfo;
        fn.FileName.Should().Be("test.txt");
        fn.ParentMftRecord.MftEntryNumber.Should().Be(5);
        fr.Attributes.OfType<Data>().Single().ResidentData.Data.Should().Equal(1, 2, 3);
    }

    [Test]
    public void FixupMismatchIsFlagged()
    {
        var r = Simple();
        r[1022] = 0x99; //the end of sector 2 no longer has the update sequence number

        var fr = Parse(r);

        fr.FixupOk.Should().BeFalse();
        fr.Attributes.Should().HaveCount(2);
    }

    [Test]
    public void FixupArrayOutsideTheRecord()
    {
        var r = Simple();
        BitConverter.GetBytes((short)2000).CopyTo(r, 6); //fixup entries

        var fr = Parse(r);

        fr.ParseErrors.Should().ContainSingle().Which.Should().Contain("does not fit");
        fr.Attributes.Should().HaveCount(2);
    }

    [Test]
    public void UnknownAttributeIsSkipped()
    {
        //this used to throw "Add me", which ended the whole $MFT
        var fr = Parse(Simple(Resident(0x5000, Bytes(1, 2, 3, 4)), DataResident(Bytes(9))));

        fr.ParseErrors.Should().ContainSingle().Which.Should().Contain("Unknown attribute type 0x5000");
        fr.Attributes.OfType<Data>().Should().ContainSingle("the attribute after the unknown one is parsed");
    }

    [Test]
    public void AttributeTooLarge()
    {
        var bad = DataResident(Bytes(1, 2, 3));
        BitConverter.GetBytes(0x7000).CopyTo(bad, 4);

        var fr = Parse(Simple(bad));

        fr.ParseErrors.Should().ContainSingle().Which.Should().Contain("has size 0x7000");
        fr.Attributes.Should().HaveCount(2);
    }

    [Test]
    public void AttributeThatFailsIsReportedAndSkipped()
    {
        var bad = FileName(5, 5, "x");
        bad[0x18 + 0x40] = 200; //name length far past the attribute

        var fr = Parse(Simple(bad, DataResident(Bytes(1))));

        fr.ParseErrors.Should().ContainSingle().Which.Should().Contain("FileName").And.Contain("could not be parsed");
        fr.Attributes.OfType<Data>().Should().ContainSingle();
    }

    [Test]
    public void TooSmallForAHeader()
    {
        var fr = Parse(new byte[0x20]);

        fr.IsBad.Should().BeTrue();
        fr.ParseErrors.Should().ContainSingle().Which.Should().Contain("header");
    }

    [Test]
    public void IndexRootEntryOfSizeZeroDoesNotHang()
    {
        //root header (0x10), node header (0x10), then an entry whose size is 0
        var content = new byte[0x60];
        BitConverter.GetBytes(0x30).CopyTo(content, 0); //indexed attribute type
        BitConverter.GetBytes(1).CopyTo(content, 4); //collation
        BitConverter.GetBytes(4096).CopyTo(content, 8);
        BitConverter.GetBytes(0x10).CopyTo(content, 0x10); //first entry
        BitConverter.GetBytes(0x50).CopyTo(content, 0x14);
        BitConverter.GetBytes(0x50).CopyTo(content, 0x18);

        var fr = Parse(Simple(Resident(0x90, content, "$I30")));

        fr.Attributes.OfType<IndexRoot>().Should().ContainSingle();
    }

    [Test]
    public void ExtendedAttributeChunkOfSizeZeroDoesNotHang()
    {
        var fr = Parse(Simple(Resident(0xE0, new byte[0x40])));

        fr.Attributes.Should().HaveCount(3);
    }

    [Test]
    public void BadDataRunsKeepTheGoodOnes()
    {
        //one run (1 byte length 0x10, 2 byte offset 0x1234), then a header claiming 15 byte lengths
        var runs = Bytes(0x21, 0x10, 0x34, 0x12, 0xFF, 0x01, 0x02);

        var fr = Parse(Simple(NonResident(0x80, 0x10000, runs)));

        var data = fr.Attributes.OfType<Data>().Single().NonResidentData;
        data.DataRuns.Should().ContainSingle().Which.ClusterOffset.Should().Be(0x1234);
        data.DataRuns[0].ClustersInRun.Should().Be(0x10);
    }

    [Test]
    public void NegativeRunOffsetIsSignExtended()
    {
        var runs = Bytes(0x21, 0x10, 0x00, 0x10, 0x21, 0x08, 0x00, 0xF0); //+0x1000 then -0x1000

        var fr = Parse(Simple(NonResident(0x80, 0x18000, runs)));

        fr.Attributes.OfType<Data>().Single().NonResidentData.DataRuns.Select(t => t.ClusterOffset).Should()
            .Equal(0x1000, -0x1000);
    }

    [Test]
    public void ResidentAttributeList()
    {
        var entries = new[] { (0x10, 70u, ""), (0x30, 70u, ""), (0x80, 71u, "ads") }
            .Select(t => AttributeListEntry(t.Item1, t.Item2, t.Item3)).SelectMany(t => t).ToArray();

        var fr = Parse(Simple(Resident(0x20, entries)));

        fr.Attributes.OfType<AttributeList>().Single().AttributeInformations
            .Select(t => (t.EntryInfo.MftEntryNumber, t.Name)).Should()
            .Equal((70u, null), (70u, null), (71u, "ads"));
    }

    private static byte[] AttributeListEntry(int type, uint entry, string name)
    {
        var n = System.Text.Encoding.Unicode.GetBytes(name);
        var e = new byte[Align(0x1A + n.Length)];
        BitConverter.GetBytes(type).CopyTo(e, 0);
        BitConverter.GetBytes((ushort)e.Length).CopyTo(e, 4);
        e[6] = (byte)name.Length;
        e[7] = 0x1A;
        BitConverter.GetBytes((ulong)entry | 1UL << 48).CopyTo(e, 0x10);
        n.CopyTo(e, 0x1A);
        return e;
    }

    [Test]
    public void UsedSizeShorterThanTheAttributes()
    {
        //deleted records can have a used size that ends before attributes that are still intact
        var r = Record(70, 0, new[] { StandardInfo(Created), FileName(5, 5, "gone.txt"), DataResident(Bytes(1, 2)) },
            usedSize: 0x38 + 0x60);

        var fr = Parse(r);

        fr.Attributes.Select(t => t.AttributeType).Should().Equal(AttributeType.StandardInformation,
            AttributeType.FileName, AttributeType.Data);
        fr.ParseErrors.Should().BeEmpty();
    }

    [Test]
    public void GarbagePastTheUsedSizeIsIgnored()
    {
        var r = Record(70, 0, new[] { StandardInfo(Created), FileName(5, 5, "gone.txt") }, usedSize: 0x38 + 0x60);
        var end = 0x38 + StandardInfo(Created).Length + FileName(5, 5, "gone.txt").Length;
        BitConverter.GetBytes(0x80).CopyTo(r, end); //a $DATA type with a nonsense size, where the end marker was
        BitConverter.GetBytes(0x7FFF).CopyTo(r, end + 4);

        var fr = Parse(r);

        fr.Attributes.Should().HaveCount(2);
        fr.ParseErrors.Should().BeEmpty();
    }

    [Test]
    public void OffsetsPastTwoGigabytes()
    {
        var fr = Parse(Simple(), 3L * 1024 * 1024 * 1024);

        fr.Offset.Should().Be(3L * 1024 * 1024 * 1024);
    }

    [Test]
    public void SlackIndexEntriesAreFound()
    {
        var slack = new byte[0x10].Concat(FileNameContent(5, 5, "deleted file.txt", 1, Created, 1234))
            .Concat(new byte[0x40]).ToArray();

        var found = FileRecord.GetSlackFileEntries(slack, 0, 0x200, 70);

        var entry = found.Should().ContainSingle().Subject;
        entry.FileInfo.FileName.Should().Be("deleted file.txt");
        entry.FileInfo.LogicalSize.Should().Be(1234);
        entry.FileInfo.CreatedOn.Should().Be(new DateTimeOffset(Created));
    }

    [Test]
    public void RandomCorruptionNeverThrows()
    {
        var xw = File.ReadAllBytes(MftTests.TestFile("xw", "$MFT"));
        var random = new Random(42);

        for (var i = 0; i < 4000; i++)
        {
            var start = random.Next(xw.Length / 1024) * 1024;
            var r = xw.Skip(start).Take(1024).ToArray();
            for (var c = random.Next(1, 12); c > 0; c--)
            {
                r[random.Next(r.Length)] = (byte)random.Next(256);
            }

            var fr = Parse(r, start);
            if (fr.Attributes != null)
            {
                foreach (var a in fr.Attributes)
                {
                    _ = a.ToString();
                }
            }
        }
    }
}
