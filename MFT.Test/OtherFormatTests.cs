using System;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using MFT.Attributes;
using NUnit.Framework;
using static MFT.Test.MftTests;

namespace MFT.Test;

/// <summary>$Boot, $I30, $SDS, $J and $O. Expected values come from ntfsinfo/ntfsls and independent decoders</summary>
[TestFixture]
public class OtherFormatTests
{
    private string _file;

    [SetUp]
    public void SetUp()
    {
        _file = Path.Combine(Path.GetTempPath(), $"ntfs-test-{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        File.Delete(_file);
    }

    private string Write(byte[] bytes)
    {
        File.WriteAllBytes(_file, bytes);
        return _file;
    }

    [TestCase("Generated", 4096, 131071L, 4L, 8191L, 1024, 4096, "330892F2154739CE")]
    [TestCase("DFIRArtifactMuseum/Boot/Win2012R2/StolenSzechuan", 4096, 22872063L, 786432L, 2L, 1024, 4096, "98E6491BE648FAD0")]
    [TestCase("DFIRArtifactMuseum/Boot/Win11/RathbunVM", 4096, 124370943L, 786432L, 2L, 1024, 4096, "EA180B53180B1E67")]
    [TestCase("DFIRArtifactMuseum/Boot/Win10/APTSimulatorVM", 4096, 125153279L, 786432L, 2L, 1024, 4096, "F81A488F1A484CB6")]
    public void Boot(string folder, int clusterSize, long totalSectors, long mft, long mirror, int recordSize,
        int indexSize, string serial)
    {
        var boot = global::Boot.BootFile.Load(TestFile(folder.Split('/').Append("$Boot").ToArray()));

        boot.FileSystemSignature.Should().Be("NTFS    ");
        (boot.BytesPerSector * boot.SectorsPerCluster).Should().Be(clusterSize);
        boot.TotalSectors.Should().Be(totalSectors);
        boot.MftClusterBlockNumber.Should().Be(mft);
        boot.MirrorMftClusterBlockNumber.Should().Be(mirror);
        boot.MftEntrySize.Should().Be(recordSize);
        boot.IndexEntrySize.Should().Be(indexSize);
        boot.VolumeSerialNumberRaw.ToString("X16").Should().Be(serial);
    }

    [TestCase(0x80, 128)]
    [TestCase(0xF8, 256)] //1 MB clusters on 4 KB sectors: values over 0x80 are 2^(256 - value)
    [TestCase(0xF4, 4096)]
    public void LargeClusters(int raw, int sectorsPerCluster)
    {
        var bytes = File.ReadAllBytes(TestFile("Generated", "$Boot"));
        bytes[13] = (byte)raw;

        var boot = global::Boot.BootFile.Load(Write(bytes));

        boot.SectorsPerCluster.Should().Be(sectorsPerCluster);
    }

    [Test]
    public void GeneratedRootIndex()
    {
        var i30 = I30.I30File.Load(TestFile("Generated", "$I30"));

        i30.ParseErrors.Should().BeEmpty();
        var names = i30.Entries.Where(t => t.FromSlack == false && t.FileInfo.NameType != NameTypes.Dos)
            .Select(t => t.FileInfo.FileName).ToList();
        names.Should().Contain(new[] { "hello.txt", "a.bin", "Long file name number 1.txt", "Long file name number 30.txt" });
        //$MFT and "Long file name number 11.txt" are in the index root (in the MFT record), not in this allocation
        names.Should().NotContain("$MFT");
        i30.Entries.Single(t => t.FileInfo.FileName == "hello.txt").MftReferenceSelf.MftEntryNumber.Should().Be(64);
    }

    [TestCase("Start", 88)]
    [TestCase("FirstDelete", 62)]
    [TestCase("SecondDelete", 57)]
    public void I30Samples(string folder, int entries)
    {
        var i30 = I30.I30File.Load(TestFile("$I30", folder, "$I30"));

        i30.Entries.Should().HaveCount(entries);
        i30.ParseErrors.Should().BeEmpty();
    }

    [Test]
    public void I30EntryOfSizeZeroDoesNotHang()
    {
        var page = File.ReadAllBytes(TestFile("$I30", "Start", "$I30")).Take(4096).ToArray();
        var first = 0x18 + BitConverter.ToInt32(page, 0x18);
        BitConverter.GetBytes((short)0).CopyTo(page, first + 8); //first entry size

        var i30 = I30.I30File.Load(Write(page));

        i30.ParseErrors.Should().Contain(t => t.Contains("has size 0x0"));
    }

    [Test]
    public void I30PageWithoutSignatureIsSkipped()
    {
        var bytes = File.ReadAllBytes(TestFile("$I30", "Start", "$I30"));
        var expected = I30.I30File.Load(TestFile("$I30", "Start", "$I30")).Entries.Count(t => t.PageNumber != 1);
        Encoding.ASCII.GetBytes("JUNK").CopyTo(bytes, 4096);

        var i30 = I30.I30File.Load(Write(bytes));

        i30.ParseErrors.Should().ContainSingle().Which.Should().Contain("no 'INDX' signature");
        i30.Entries.Should().HaveCount(expected);
    }

    [Test]
    public void SdsSample()
    {
        var sds = SDS.SdsFile.Load(TestFile("DFIRArtifactMuseum", "Secure_$SDS", "Win2012R2", "StolenSzechuan", "$Secure_$SDS"));

        //each entry once: the mirror copies are the same, so they are not repeated
        sds.SdsEntries.Should().HaveCount(508);
        sds.SdsEntries.Select(t => t.Id).Should().OnlyHaveUniqueItems();
        sds.ParseErrors.Should().BeEmpty();

        var first = sds.SdsEntries[0];
        first.Id.Should().Be(256);
        first.Hash.Should().Be("CBC6FE32");
        first.SecurityDescriptor.OwnerSid.Should().Be("S-1-5-18");
        first.SecurityDescriptor.GroupSid.Should().Be("S-1-5-32-544");
        first.SecurityDescriptor.Dacl.AceRecords.Select(t => t.Sid).Should().Equal("S-1-5-18", "S-1-5-32-544");
    }

    [Test]
    public void SdsDifferingMirrorIsKept()
    {
        var bytes = File.ReadAllBytes(TestFile("DFIRArtifactMuseum", "Secure_$SDS", "Win2012R2", "StolenSzechuan", "$Secure_$SDS"));
        bytes[0x40000 + 0x14 + 0x40] ^= 0xFF; //change the mirror copy of the first entry's descriptor

        var sds = SDS.SdsFile.Load(Write(bytes));

        sds.SdsEntries.Should().HaveCount(509);
        sds.SdsEntries.Single(t => t.FromMirror).Id.Should().Be(256);
    }

    [Test]
    public void GeneratedSds()
    {
        var sds = SDS.SdsFile.Load(TestFile("Generated", "$SDS"));

        sds.SdsEntries.Select(t => (t.Id, t.SecurityDescriptor.OwnerSid)).Should()
            .Equal((256u, "S-1-5-32-544"), (257u, "S-1-5-32-544"));
    }

    [Test]
    public void AceSidsUseTheirSubauthorityCount()
    {
        //an ACCESS_ALLOWED_CALLBACK ACE: the SID (BUILTIN\Users) is followed by application data, which used to
        //be added to the SID as subauthorities
        var sid = new byte[] { 1, 2, 0, 0, 0, 0, 0, 5, 0x20, 0, 0, 0, 0x21, 2, 0, 0 };
        var appData = Encoding.ASCII.GetBytes("artx0123456789AB");
        var ace = new byte[8 + sid.Length + appData.Length];
        ace[0] = 9;
        BitConverter.GetBytes((ushort)ace.Length).CopyTo(ace, 2);
        BitConverter.GetBytes(0x1200A9).CopyTo(ace, 4);
        sid.CopyTo(ace, 8);
        appData.CopyTo(ace, 8 + sid.Length);

        new AceRecord(ace).Sid.Should().Be("S-1-5-32-545");
    }

    [Test]
    public void ObjectAceSid()
    {
        //ACCESS_ALLOWED_OBJECT_ACE with both GUIDs: the SID is after the flags and 32 bytes of GUIDs
        var sid = new byte[] { 1, 1, 0, 0, 0, 0, 0, 5, 0x12, 0, 0, 0 };
        var ace = new byte[0xC + 32 + sid.Length];
        ace[0] = 5;
        BitConverter.GetBytes((ushort)ace.Length).CopyTo(ace, 2);
        BitConverter.GetBytes(3).CopyTo(ace, 8);
        sid.CopyTo(ace, 0xC + 32);

        new AceRecord(ace).Sid.Should().Be("S-1-5-18");
    }

    [Test]
    public void LargeAceDoesNotThrowOffTheRest()
    {
        //an ACL with a 0x110 byte ACE and then a normal one; the size used to be read as one byte (0x10)
        var big = new byte[0x110];
        BitConverter.GetBytes((ushort)0x110).CopyTo(big, 2);
        new byte[] { 1, 1, 0, 0, 0, 0, 0, 5, 0x12, 0, 0, 0 }.CopyTo(big, 8);
        var small = new byte[0x14];
        BitConverter.GetBytes((ushort)0x14).CopyTo(small, 2);
        new byte[] { 1, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0 }.CopyTo(small, 8);
        var acl = new byte[8].Concat(big).Concat(small).ToArray();
        acl[0] = 2;
        BitConverter.GetBytes((ushort)acl.Length).CopyTo(acl, 2);
        BitConverter.GetBytes((ushort)2).CopyTo(acl, 4);

        new XAclRecord(acl, XAclRecord.AclTypeEnum.Discretionary).AceRecords.Select(t => t.Sid).Should()
            .Equal("S-1-5-18", "S-1-1-0");
    }

    [Test]
    public void SidConversion()
    {
        Helpers.ConvertHexStringToSidString(new byte[] { 1, 1, 0, 0, 0, 0, 0, 5, 0x12, 0, 0, 0 }).Should().Be("S-1-5-18");
        Helpers.ConvertHexStringToSidString(new byte[] { 1, 2, 0, 0, 0, 0, 0, 5, 0x20, 0, 0, 0, 0x20, 2, 0, 0, 0xFF, 0xFF })
            .Should().Be("S-1-5-32-544", "bytes after the SID are not subauthorities");
        Helpers.ConvertHexStringToSidString(new byte[] { 1, 5, 0, 0, 0, 0, 0, 5, 0x15, 0, 0, 0 })
            .Should().Be("S-1-5-21", "a count past the end is cut to what is there");
        Helpers.ConvertHexStringToSidString(new byte[] { 1, 1, 0 }).Should().BeEmpty();
    }

    [Test]
    public void UsnSample()
    {
        var usn = Usn.UsnFile.Load(TestFile("DFIRArtifactMuseum", "J", "Win2012R2", "StolenSzechuan", "$J"));

        usn.UsnEntries.Should().HaveCount(82151);
        usn.ParseErrors.Should().BeEmpty();

        var first = usn.UsnEntries[0];
        first.Name.Should().Be("WindowsUpdate.log");
        first.FileReference.EntryNumber.Should().Be(84627);
        first.ParentFileReference.EntryNumber.Should().Be(297);
        first.UpdateTimestamp.Should().Be(DateTimeOffset.FromFileTime(132448315022266746));
    }

    [Test]
    public void UsnRecordsAfterZerosInAPage()
    {
        //this page starts with 0x140 bytes of zeros; the records after them used to be skipped with the page
        var usn = Usn.UsnFile.Load(TestFile("DFIRArtifactMuseum", "J", "Win2012R2", "StolenSzechuan", "$J"));

        var sam = usn.UsnEntries.Single(t => t.OffsetToData == 0x8C9220);
        sam.Name.Should().Be("SAM.LOG1");
        sam.UpdateSequenceNumber.Should().Be(0x8C9220);
        sam.FileReference.EntryNumber.Should().Be(76716);
        usn.UsnEntries.Should().Contain(t => t.OffsetToData == 0x8C9140 && t.Name == "~FontCache-S-1-5-18.dat");
    }

    private static byte[] UsnRecord(short major, string name, long timestamp, ulong entry = 100)
    {
        var extra = major == 3 ? 16 : 0;
        var n = Encoding.Unicode.GetBytes(name);
        var r = new byte[(0x3C + extra + n.Length + 7) & ~7];
        BitConverter.GetBytes(r.Length).CopyTo(r, 0);
        BitConverter.GetBytes(major).CopyTo(r, 4);
        BitConverter.GetBytes(entry | 3UL << 48).CopyTo(r, 8);
        BitConverter.GetBytes(5UL | 5UL << 48).CopyTo(r, 8 + 8 + extra / 2);
        BitConverter.GetBytes(0x1000UL).CopyTo(r, 24 + extra);
        BitConverter.GetBytes(timestamp).CopyTo(r, 32 + extra);
        BitConverter.GetBytes(0x100).CopyTo(r, 40 + extra);
        BitConverter.GetBytes((short)n.Length).CopyTo(r, 56 + extra);
        BitConverter.GetBytes((short)(0x3C + extra)).CopyTo(r, 58 + extra);
        n.CopyTo(r, 0x3C + extra);
        return r;
    }

    [Test]
    public void UsnVersions()
    {
        var ts = new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc).ToFileTimeUtc();
        var v4 = new byte[0x40];
        BitConverter.GetBytes(0x40).CopyTo(v4, 0);
        BitConverter.GetBytes((short)4).CopyTo(v4, 4);
        var bytes = UsnRecord(2, "two.txt", ts).Concat(UsnRecord(3, "three.txt", ts, 200)).Concat(v4)
            .Concat(UsnRecord(2, "after.txt", -5)).Concat(new byte[64]).ToArray();

        var usn = Usn.UsnFile.Load(Write(bytes));

        //a V3 or V4 record used to skip the rest of the page
        usn.UsnEntries.Select(t => (t.Name, t.MajorVersion)).Should()
            .Equal(("two.txt", (short)2), ("three.txt", (short)3), ("after.txt", (short)2));
        usn.UsnEntries[1].FileReference.EntryNumber.Should().Be(200);
        usn.UsnEntries[1].ParentFileReference.EntryNumber.Should().Be(5);
        usn.UsnEntries[1].UpdateTimestamp.Should().Be(new DateTimeOffset(2024, 3, 4, 5, 6, 7, TimeSpan.Zero));
        //an out of range timestamp used to throw and end the journal
        usn.UsnEntries[2].InvalidTimestamp.Should().BeTrue();
        usn.ParseErrors.Should().ContainSingle().Which.Should().Contain("invalid timestamp");
    }

    [Test]
    public void UsnEntryNumbersAre48Bits()
    {
        var bytes = UsnRecord(2, "big.txt", 0, 0x0001_0000_0005);

        var usn = Usn.UsnFile.Load(Write(bytes));

        usn.UsnEntries.Single().FileReference.EntryNumber.Should().Be(0x0001_0000_0005UL);
    }

    private static byte[] OPage(params (Guid ObjectId, uint Entry)[] entries)
    {
        //INDX page: header, then the node header at 0x18, entries from 0x40
        var page = new byte[4096];
        Encoding.ASCII.GetBytes("INDX").CopyTo(page, 0);
        BitConverter.GetBytes((short)0x28).CopyTo(page, 4);
        BitConverter.GetBytes((short)9).CopyTo(page, 6);
        BitConverter.GetBytes(0x28).CopyTo(page, 0x18); //entries at 0x18 + 0x28 = 0x40
        var index = 0x40;
        foreach (var (objectId, entry) in entries)
        {
            BitConverter.GetBytes((ushort)0x20).CopyTo(page, index);
            BitConverter.GetBytes((ushort)0x38).CopyTo(page, index + 2);
            BitConverter.GetBytes((ushort)0x58).CopyTo(page, index + 8);
            BitConverter.GetBytes((ushort)0x10).CopyTo(page, index + 10);
            objectId.ToByteArray().CopyTo(page, index + 0x10);
            BitConverter.GetBytes((ulong)entry | 1UL << 48).CopyTo(page, index + 0x20);
            index += 0x58;
        }

        BitConverter.GetBytes((ushort)0x10).CopyTo(page, index + 8); //last entry
        BitConverter.GetBytes((ushort)2).CopyTo(page, index + 12);
        BitConverter.GetBytes(index + 0x10 - 0x18).CopyTo(page, 0x1C); //size in use
        BitConverter.GetBytes(4096 - 0x18).CopyTo(page, 0x20);
        //fixups: update sequence number 1, original sector ends are 0
        BitConverter.GetBytes((short)1).CopyTo(page, 0x28);
        for (var sector = 1; sector <= 8; sector++)
        {
            BitConverter.GetBytes((short)1).CopyTo(page, sector * 512 - 2);
        }

        return page;
    }

    [Test]
    public void ObjectIdsFromEveryPage()
    {
        //Entries was created again for every page, so only the last page's entries were returned
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var bytes = OPage((a, 40), (b, 41)).Concat(OPage((c, 42))).ToArray();

        var o = O.OFile.Load(Write(bytes));

        o.Entries.Select(t => (t.ObjectId, t.MftReference.MftEntryNumber)).Should().Equal((a, 40u), (b, 41u), (c, 42u));
        o.ParseErrors.Should().BeEmpty();
    }
}
