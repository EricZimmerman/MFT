using System.IO;
using System.Linq;
using FluentAssertions;
using MFT.Attributes;
using MFT.Other;
using NUnit.Framework;

namespace MFT.Test;

/// <summary>
///     The $MFT samples in TestFiles. Expected values were checked against the Rust mft crate (pymft) and raw decoding
/// </summary>
[TestFixture]
public class MftTests
{
    public static string TestFile(params string[] parts) =>
        Path.Combine(new[] { TestContext.CurrentContext.TestDirectory, "TestFiles" }.Concat(parts).ToArray());

    private static string Time(System.DateTimeOffset? d) => d?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.ffffff");

    private static string FullPath(Mft mft, FileRecord fr)
    {
        var fn = fr.GetFileNameAttributeFromFileRecord();
        return mft.GetFullParentPath(fn.FileInfo.ParentMftRecord.GetKey()) + "\\" + fn.FileInfo.FileName;
    }

    private static FileRecord Entry(Mft mft, uint entry) =>
        mft.FileRecords.Values.Concat(mft.FreeFileRecords.Values).Single(t => t.EntryNumber == entry &&
            t.MftRecordToBaseRecord.MftEntryNumber == 0);

    [TestCase("NIST/DFR-16", 117, 0, 0, 37)]
    [TestCase("xw", 467, 159, 2, 0)]
    [TestCase("tdungan", 42861, 9459, 0, 0)]
    [TestCase("Generated", 51, 45, 0, 0)]
    public void RecordCounts(string folder, int inUse, int free, int bad, int uninitialized)
    {
        var mft = MftFile.Load(TestFile(folder, "$MFT"), false);

        mft.FileRecords.Should().HaveCount(inUse);
        mft.FreeFileRecords.Should().HaveCount(free);
        mft.BadRecords.Should().HaveCount(bad);
        mft.UninitializedRecords.Should().HaveCount(uninitialized);
        mft.ParseErrors.Should().BeEmpty();
        mft.FileRecords.Values.Should().OnlyContain(t => t.ParseErrors.Count == 0);
    }

    [Test]
    public void TdunganValues()
    {
        var mft = MftFile.Load(TestFile("tdungan", "$MFT"), false);

        var root = Entry(mft, 5);
        root.SequenceNumber.Should().Be(5);
        root.IsDirectory().Should().BeTrue();
        var rootSi = root.Attributes.OfType<StandardInfo>().Single();
        Time(rootSi.CreatedOn).Should().Be("2010-11-10 17:38:01.062500");
        Time(rootSi.LastAccessedOn).Should().Be("2012-04-07 02:33:13.583147");

        var log = Entry(mft, 329);
        FullPath(mft, log).Should().Be(@".\Documents and Settings\RSydow\Local Settings\Temp\vmmsi.log");
        var si = log.Attributes.OfType<StandardInfo>().Single();
        Time(si.CreatedOn).Should().Be("2011-07-06 01:04:45.406250");
        Time(si.ContentModifiedOn).Should().Be("2011-07-05 18:06:07.722625");
        si.SecurityId.Should().Be(1040);

        FullPath(mft, Entry(mft, 40000)).Should().Be(@".\WINDOWS\$NtServicePackUninstall$\reg00099");
    }

    [Test]
    public void ResidentAttributeListHasItsEntries()
    {
        //the size check was inverted, so resident attribute lists had no entries
        var mft = MftFile.Load(TestFile("tdungan", "$MFT"), false);

        var list = Entry(mft, 329).Attributes.OfType<AttributeList>().Single();

        list.IsResident.Should().BeTrue();
        list.AttributeInformations.Select(t =>
                $"{t.EntryInfo.MftEntryNumber}-{t.EntryInfo.MftSequenceNumber}:{t.FirstVirtualClusterNumber}")
            .Should().Equal("329-2:0", "329-2:0", "1473-2:0", "2120-2:559");
    }

    [Test]
    public void AttributesPastTheUsedSizeOfDeletedRecords()
    {
        //record 179 is free and its used size (0x158) ends before its $DATA attribute; the attribute chain is intact
        var xw = File.ReadAllBytes(TestFile("xw", "$MFT"));
        var record = xw.Skip(179 * 1024).Take(1024).ToArray();

        var fr = new FileRecord(record, 179 * 1024, false);

        fr.IsDeleted().Should().BeTrue();
        fr.Attributes.Select(t => t.AttributeType).Should().Equal(AttributeType.StandardInformation,
            AttributeType.FileName, AttributeType.FileName, AttributeType.Data);
        var data = fr.Attributes.OfType<Data>().Single();
        data.NonResidentData.ActualSize.Should().Be(34304);
        data.NonResidentData.DataRuns.Should().ContainSingle().Which.ClusterOffset.Should().Be(3614);
        fr.ParseErrors.Should().BeEmpty("leftovers past the used size are not errors");
    }

    [Test]
    public void Ntfs30RecordUsesItsPosition()
    {
        //this record's fixup array is at 0x2A (NTFS 3.0), so there is no entry number at 0x2C
        var mft = MftFile.Load(TestFile("tdungan", "$MFT"), false);

        mft.FileRecords.Values.Concat(mft.FreeFileRecords.Values).Should()
            .Contain(t => t.Offset == 17863L * 1024 && t.EntryNumber == 17863);
    }

    [Test]
    public void XwPathsAndBaseReferences()
    {
        var mft = MftFile.Load(TestFile("xw", "$MFT"), false);

        FullPath(mft, Entry(mft, 100)).Should()
            .Be(@".\Documents and Settings\EdgarAllanPoe\Local Settings\Temp\Temporary Internet Files\Content.IE5\0P2NGHQ3\home_navi[1].htm");

        var extension = mft.FileRecords.Values.Concat(mft.FreeFileRecords.Values).Single(t => t.Offset == 432 * 1024);
        extension.MftRecordToBaseRecord.MftEntryNumber.Should().Be(485);
        extension.MftRecordToBaseRecord.MftSequenceNumber.Should().Be(2);
    }

    [Test]
    public void Nist()
    {
        var mft = MftFile.Load(TestFile("NIST", "DFR-16", "$MFT"), false);

        var dir = Entry(mft, 77);
        FullPath(mft, dir).Should().Be(@".\Y05\Y05L01\Y05L02\Y05L03");
        Time(dir.Attributes.OfType<StandardInfo>().Single().LastAccessedOn).Should().Be("2012-02-06 16:18:34.158664");
        FullPath(mft, Entry(mft, 30)).Should().Be(@".\$Extend\$RmMetadata\$Txf");
    }

    [Test]
    public void GeneratedVolume()
    {
        var mft = MftFile.Load(TestFile("Generated", "$MFT"), false);

        var hello = Entry(mft, 64);
        FullPath(mft, hello).Should().Be(@".\hello.txt");
        hello.GetFileSize().Should().Be(12);
        hello.Attributes.OfType<Data>().Single().ResidentData.Data.Should()
            .Equal(System.Text.Encoding.ASCII.GetBytes("hello world\n"));

        Entry(mft, 65).GetFileSize().Should().Be(1000);
        FullPath(mft, Entry(mft, 72)).Should().Be(@".\Long file name number 7.txt");
        mft.GetDirectoryContents("00000005-00000005").Should().HaveCount(43);
    }

    [TestCase("NIST/DFR-16")]
    [TestCase("xw")]
    [TestCase("tdungan")]
    [TestCase("Generated")]
    public void DirectoryEntriesMatchContents(string folder)
    {
        var mft = MftFile.Load(TestFile(folder, "$MFT"), false);

        CheckDirectoryEntries(mft).Should().BeGreaterThan(0);
    }

    /// <summary>
    ///     For every record key: <see cref="Mft.GetDirectoryEntries" /> has the same entries as
    ///     <see cref="Mft.GetDirectoryContents" />, each with its record, and <see cref="Mft.HasSubdirectories" /> is
    ///     whether any of them is a directory. Returns the number of directories with entries
    /// </summary>
    public static int CheckDirectoryEntries(Mft mft)
    {
        var withEntries = 0;
        foreach (var key in mft.FileRecords.Keys.Concat(mft.FreeFileRecords.Keys))
        {
            var contents = mft.GetDirectoryContents(key);
            var entries = mft.GetDirectoryEntries(key);

            entries.Count.Should().Be(contents.Count, key);
            new System.Collections.Generic.HashSet<ParentMapEntry>(entries).SetEquals(contents).Should().BeTrue(key);
            mft.HasSubdirectories(key).Should().Be(contents.Any(t => t.IsDirectory), key);

            foreach (var entry in entries)
            {
                entry.Record.Should().NotBeNull(key);
                entry.Record.GetKey(true).Should().Be(entry.Key, key);
                entry.Record.IsDirectory().Should().Be(entry.IsDirectory, key);
            }

            if (entries.Count > 0)
            {
                withEntries += 1;
            }
        }

        return withEntries;
    }

    [Test]
    public void IndexRootEntriesHaveTheirOwnReferences()
    {
        var mft = MftFile.Load(TestFile("Generated", "$MFT"), false);

        //$Extend's $I30 root: each entry is for its own file, and its key's parent is $Extend
        var root = Entry(mft, 11).Attributes.OfType<IndexRoot>().Single(t => t.Name == "$I30");
        root.IndexEntries.Select(t => (t.FileName, t.MftReference.MftEntryNumber, t.ParentMftRecord.MftEntryNumber)).Should()
            .Equal(("$ObjId", 25UL, 11UL), ("$Quota", 24UL, 11UL), ("$Reparse", 26UL, 11UL));
        root.MftRecord.MftEntryNumber.Should().Be(25);
    }
}
