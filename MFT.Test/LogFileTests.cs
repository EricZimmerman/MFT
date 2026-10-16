using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using LogFile;
using MFT.Attributes;
using NUnit.Framework;
using static MFT.Test.LogFileBuilder;

namespace MFT.Test;

/// <summary>
///     $LogFile: a real Server 2012 R2 log (checked against dfir_ntfs, which finds a subset of these records with the
///     same values), and built logs for each mechanism. Add regression tests here
/// </summary>
[TestFixture]
public class LogFileTests
{
    private static LogFile.LogFile Parse(byte[] bytes)
    {
        var task = Task.Run(() => new LogFile.LogFile(new MemoryStream(bytes)));
        task.Wait(TimeSpan.FromSeconds(60)).Should().BeTrue("parsing must finish");
        return task.Result;
    }

    private static byte[] Sample()
    {
        using var gz = new GZipStream(File.OpenRead(MftTests.TestFile("DFIRArtifactMuseum", "LogFile", "Win2012R2",
            "StolenSzechuan", "$LogFile.gz")), CompressionMode.Decompress);
        using var ms = new MemoryStream();
        gz.CopyTo(ms);
        return ms.ToArray();
    }

    [Test]
    public void ServerSample()
    {
        var lf = Parse(Sample());

        lf.MajorVersion.Should().Be(2);
        lf.PageSize.Should().Be(4096);
        lf.SequenceNumberBits.Should().Be(42);
        lf.RestartArea.LogPageDataOffset.Should().Be(0x40);
        lf.RestartPages.Should().HaveCount(2).And.OnlyContain(t => t.IsValid && t.FixupOk);

        lf.Records.Should().HaveCount(113738);
        lf.Records.Count(t => t.RecordType == LogRecordType.ClientRestart).Should().Be(12);
        lf.Records.Select(t => t.Lsn).Should().BeInAscendingOrder();

        var first = lf.Records[0];
        first.Lsn.Should().Be(8527269);
        first.TransactionId.Should().Be(24);
        first.Ntfs.RedoOperation.Should().Be(NtfsOperation.UpdateResidentValue);
        first.Ntfs.TargetMftEntry.Should().Be(84870);
        lf.Records[^1].Lsn.Should().Be(223591708);

        //UpdateFileNameAllocation: the new timestamps of a directory entry
        var update = lf.Records.First(t => t.Ntfs?.RedoOperation == NtfsOperation.UpdateFileNameAllocation && t.Ntfs.RedoData.Length > 0);
        update.Lsn.Should().Be(8527384);
        update.Ntfs.RedoFileNameUpdate.CreatedOn.Should().Be(DateTimeOffset.FromFileTime(0x01cf45338acf2329));
        update.Ntfs.RedoFileNameUpdate.LastAccessedOn.Should().Be(DateTimeOffset.FromFileTime(0x01d8c558a7194d78));

        //the one record whose LCN list is cut off (dfir_ntfs reports it too)
        lf.ParseErrors.Should().ContainSingle().Which.Should().Contain("LSN 9036281");
    }

    [Test]
    public void RecordSpanningPages()
    {
        var b = new LogFileBuilder();
        var big = Enumerable.Range(0, 9000).Select(t => (byte)t).ToArray();
        var data = NtfsRecord(7, 7, big.Take(4000).ToArray(), big.Skip(4000).Take(4000).ToArray());
        var next = b.Record(LogStart + DataOffset, data);
        b.Record(next, NtfsRecord(0x1A, 0x01));

        var lf = Parse(b.Build());

        lf.ParseErrors.Should().BeEmpty();
        lf.Records.Should().HaveCount(2);
        lf.Records[0].ClientData.Should().Equal(data);
        lf.Records[0].Ntfs.RedoData.Should().Equal(big.Take(4000));
        lf.Records[0].Ntfs.UndoData.Should().Equal(big.Skip(4000).Take(4000));
        lf.Records[1].Ntfs.RedoOperation.Should().Be(NtfsOperation.CommitTransaction);
        lf.Records[1].FileOffset.Should().Be(next);
    }

    [Test]
    public void NewerFastPageIsUsed()
    {
        //the log area page has an old record; the fast page copy of it is newer
        var b = new LogFileBuilder();
        b.Record(LogStart + DataOffset, NtfsRecord(0x1A, 0), sequence: 1);
        var newer = new LogFileBuilder();
        newer.Record(LogStart + DataOffset, NtfsRecord(0x1B, 0), sequence: 2);
        var newerImage = newer.Build();
        var image = b.Build();
        Buffer.BlockCopy(newerImage, LogStart, image, 2 * PageSize, PageSize); //fast page 0

        var lf = Parse(image);

        lf.Records.Should().ContainSingle().Which.Ntfs.RedoOperation.Should().Be(NtfsOperation.ForgetTransaction);
        lf.Pages.Single(t => t.Offset == 2 * PageSize).CopyOf.Should().Be(LogStart);
    }

    [Test]
    public void FastPageWithTheSameLastLsnIsNotNewer()
    {
        //a copy whose last record continues into the next page has a higher last end LSN, but the same last LSN
        var b = new LogFileBuilder();
        var next = b.Record(LogStart + DataOffset, NtfsRecord(0x1A, 0));
        b.Record(next, NtfsRecord(7, 7, new byte[5000]));
        var page = LogStart;
        b.FastCopy(0, page, lastEndLsn: Lsn(next));
        var image = b.Build();
        image[2 * PageSize + 0x100] ^= 0xFF; //would damage the first record if this copy were used

        var lf = Parse(image);

        lf.Records.Should().HaveCount(2);
        lf.Pages.Single(t => t.Offset == 2 * PageSize).CopyOf.Should().Be(-1);
    }

    [Test]
    public void CopiesSupplyPagesPastTheEndOfTheFile()
    {
        //collected $LogFiles can be cut short; the fast copy of a later page still has its records
        var b = new LogFileBuilder();
        var lastPage = LogStart + 15L * PageSize;
        b.Record(LogStart + DataOffset, NtfsRecord(0x1A, 0));
        b.Record(lastPage + DataOffset, NtfsRecord(0x1B, 0));
        b.FastCopy(0, lastPage);

        var lf = Parse(b.Build(truncateTo: (int)lastPage));

        lf.Records.Select(t => t.Ntfs.RedoOperation).Should().Equal(NtfsOperation.CommitTransaction, NtfsOperation.ForgetTransaction);
        lf.ParseErrors.Should().ContainSingle().Which.Should().Contain("pages after the end of the file are missing");
    }

    [Test]
    public void OldRecordsMidPageAreFound()
    {
        //an older pass left records in the middle of a page; the newer record at the start of the page is followed
        //by leftover bytes, so no run of records reaches the old ones
        var b = new LogFileBuilder();
        b.Record(LogStart + DataOffset, NtfsRecord(0x1A, 0), sequence: 3);
        b.Poke(LogStart + DataOffset + 0x58, Enumerable.Repeat((byte)0xEE, 64).ToArray());
        var old = b.Record(LogStart + 0x200, NtfsRecord(7, 7, new byte[16], new byte[16]), sequence: 2);
        b.Record(old, NtfsRecord(0x1B, 0), sequence: 2);

        var lf = Parse(b.Build());

        lf.Records.Should().HaveCount(3);
        lf.Records.Select(t => t.SequenceNumber).Should().Equal(2UL, 2UL, 3UL);
    }

    [Test]
    public void TornPageIsReported()
    {
        var b = new LogFileBuilder();
        b.Record(LogStart + DataOffset, NtfsRecord(0x1A, 0));
        var image = b.Build();
        BitConverter.GetBytes((ushort)0x9999).CopyTo(image, LogStart + 1022); //a sector end without the sequence number

        var lf = Parse(image);

        lf.ParseErrors.Should().Contain(t => t.Contains("torn write"));
        lf.Records.Should().ContainSingle();
    }

    [Test]
    public void TruncatedRecordKeepsWhatIsThere()
    {
        var b = new LogFileBuilder();
        b.Record(LogStart + 14L * PageSize + DataOffset, NtfsRecord(7, 7, new byte[6000]));
        var image = b.Build();
        //the page it continues into is not a log page
        Array.Clear(image, LogStart + 15 * PageSize, 4);

        var lf = Parse(image);

        var r = lf.Records.Should().ContainSingle().Subject;
        r.Truncated.Should().BeTrue();
        r.ClientData.Length.Should().Be(PageSize - DataOffset - 0x30);
        lf.ParseErrors.Should().Contain(t => t.Contains("could be read"));
    }

    [Test]
    public void NotALogFile()
    {
        FluentActions.Invoking(() => Parse(Enumerable.Repeat((byte)0xFF, 0x10000).ToArray())).Should()
            .Throw<LogFileParseException>().WithMessage("*not been initialized*");
        FluentActions.Invoking(() => Parse(new byte[0x10000])).Should().Throw<LogFileParseException>()
            .WithMessage("No valid restart page*");
        FluentActions.Invoking(() => Parse(new byte[0x100])).Should().Throw<LogFileParseException>();
    }

    [Test]
    public void IndexEntriesAndFileRecordsAreDecoded()
    {
        //AddIndexEntryAllocation redo: an $I30 entry (MFT reference, sizes, flags, $FILE_NAME key)
        var key = RecordBuilder.FileNameContent(5, 5, "secret.docx", 1, new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc), 4321);
        var entry = new byte[0x10 + key.Length];
        BitConverter.GetBytes(77UL | 3UL << 48).CopyTo(entry, 0);
        BitConverter.GetBytes((ushort)entry.Length).CopyTo(entry, 8);
        BitConverter.GetBytes((ushort)key.Length).CopyTo(entry, 10);
        key.CopyTo(entry, 0x10);

        //InitializeFileRecordSegment redo: a FILE record as it is in memory (no fixups)
        var record = RecordBuilder.Record(77, 1,
            new[] { RecordBuilder.StandardInfo(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc)), RecordBuilder.FileName(5, 5, "secret.docx") },
            sequence: 3, applyFixups: false);
        var used = BitConverter.ToInt32(record, 0x18);

        var b = new LogFileBuilder();
        var next = b.Record(LogStart + DataOffset, NtfsRecord((ushort)NtfsOperation.AddIndexEntryAllocation,
            (ushort)NtfsOperation.DeleteIndexEntryAllocation, entry, targetAttribute: 0x30));
        //MFT entry 77 with 4 KB clusters and 1 KB records: VCN 19, cluster block offset 2 (x 512 bytes)
        b.Record(next, NtfsRecord((ushort)NtfsOperation.InitializeFileRecordSegment, 0, record.Take(used).ToArray(), vcn: 19,
            clusterBlockOffset: 2));

        var lf = Parse(b.Build());

        var index = lf.Records[0].Ntfs.RedoIndexEntry;
        index.MftReference.MftEntryNumber.Should().Be(77);
        index.MftReference.MftSequenceNumber.Should().Be(3);
        index.FileInfo.FileName.Should().Be("secret.docx");
        index.FileInfo.ParentMftRecord.MftEntryNumber.Should().Be(5);
        index.FileInfo.LogicalSize.Should().Be(4321);
        index.FileInfo.CreatedOn.Should().Be(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero));

        var init = lf.Records[1].Ntfs;
        init.TargetMftEntry.Should().Be(77);
        init.FileRecord.EntryNumber.Should().Be(77);
        init.FileRecord.SequenceNumber.Should().Be(3);
        init.FileRecord.ParseErrors.Should().BeEmpty();
        init.FileRecord.Attributes.OfType<FileName>().Single().FileInfo.FileName.Should().Be("secret.docx");
    }

    [Test]
    public void AttributesAreDecoded()
    {
        var created = RecordBuilder.FileName(5, 5, "new name.txt");
        var deleted = RecordBuilder.DataResident(Encoding.ASCII.GetBytes("gone"), "ads");

        var b = new LogFileBuilder();
        var next = b.Record(LogStart + DataOffset, NtfsRecord((ushort)NtfsOperation.CreateAttribute, (ushort)NtfsOperation.DeleteAttribute, created));
        b.Record(next, NtfsRecord((ushort)NtfsOperation.DeleteAttribute, (ushort)NtfsOperation.CreateAttribute, undo: deleted));

        var lf = Parse(b.Build());

        lf.Records[0].Ntfs.RedoAttribute.Should().BeOfType<FileName>().Which.FileInfo.FileName.Should().Be("new name.txt");
        var data = lf.Records[1].Ntfs.UndoAttribute.Should().BeOfType<Data>().Subject;
        data.Name.Should().Be("ads");
        data.ResidentData.Data.Should().Equal(Encoding.ASCII.GetBytes("gone"));
    }

    [Test]
    public void CheckpointsAndOpenAttributesOfTheServerSample()
    {
        var lf = Parse(Sample());

        var restart = lf.Records.First(t => t.Restart != null);
        restart.Lsn.Should().Be(9570436);
        restart.Restart.MajorVersion.Should().Be(1);
        restart.Restart.OpenAttributeTableLsn.Should().Be(9565025);
        restart.Restart.AttributeNamesLsn.Should().Be(9566135);
        lf.Records.Count(t => t.Restart != null).Should().Be(12);

        var dump = lf.Records.Single(t => t.Lsn == 9565025).Ntfs.OpenAttributeTable;
        dump.Should().HaveCount(211);
        dump[0].Index.Should().Be(0x18);
        dump[0].FileReference.MftEntryNumber.Should().Be(0);
        dump[0].AttributeType.Should().Be(AttributeType.Data);
        dump[1].FileReference.MftEntryNumber.Should().Be(5);
        dump[1].AttributeName.Should().Be("$I30");
        dump[1].BytesPerIndexBuffer.Should().Be(4096);

        //an $O index entry ($ObjId), and a write to $UsnJrnl:$J
        Target(lf, 10078455).Should().Be((0x19UL, (ushort)1, AttributeType.IndexAllocation, "$O"));
        Target(lf, 10078529).Should().Be((0x14AB0UL, (ushort)2, AttributeType.Data, "$J"));
        //before the first checkpoint: from it (the attribute was open before the log starts)
        Target(lf, 8527384).Should().Be((0x37FUL, (ushort)1, AttributeType.IndexAllocation, "$I30"));
        //file record operations change $MFT:$DATA
        Target(lf, 8527269).Should().Be((0UL, (ushort)1, AttributeType.Data, ""));

        var transaction = lf.Records.Where(t => t.TransactionFirstLsn == 8527697).ToList();
        transaction.Select(t => t.Lsn).Should().Equal(8527697, 8527720, 8527745, 8527770);
        transaction.Select(t => t.TransactionId).Distinct().Should().Equal(24u);
        lf.Records.Select(t => t.TransactionFirstLsn).Distinct().Should().HaveCount(38552);
    }

    private static (ulong, ushort, AttributeType, string) Target(LogFile.LogFile lf, ulong lsn)
    {
        var t = lf.Records.Single(r => r.Lsn == lsn).Ntfs.TargetOpenAttribute;
        return (t.FileReference.MftEntryNumber, t.FileReference.MftSequenceNumber, t.AttributeType, t.AttributeName);
    }

    /// <summary>An open attribute table entry as Windows 8 and later write it (0x28 bytes)</summary>
    private static byte[] OpenEntry(ulong entry, ushort sequence, AttributeType type, uint bytesPerIndexBuffer = 0)
    {
        var e = new byte[0x28];
        BitConverter.GetBytes(0xFFFFFFFF).CopyTo(e, 0);
        BitConverter.GetBytes(bytesPerIndexBuffer).CopyTo(e, 4);
        BitConverter.GetBytes((int)type).CopyTo(e, 8);
        BitConverter.GetBytes(entry | (ulong)sequence << 48).CopyTo(e, 0x10);
        return e;
    }

    [Test]
    public void TargetAttributesAreResolved()
    {
        var b = new LogFileBuilder();
        var dir = Encoding.Unicode.GetBytes("$I30");
        var tableLsn = 0UL;
        var namesLsn = 0UL;

        //an attribute opened at offset 0x40, then used
        long at = LogStart + DataOffset;
        at = b.Record(at, NtfsRecord((ushort)NtfsOperation.OpenNonresidentAttribute, 0, OpenEntry(0x100, 2, AttributeType.IndexAllocation, 4096), dir, targetAttribute: 0x40));
        var used = at;
        at = b.Record(at, NtfsRecord((ushort)NtfsOperation.UpdateNonresidentValue, 0, new byte[8], targetAttribute: 0x40), transaction: 7);
        at = b.Record(at, NtfsRecord((ushort)NtfsOperation.SetBitsInNonresidentBitMap, 0, new byte[8], targetAttribute: 0x40), transaction: 7,
            previous: Lsn(used));

        //a mount: a restart area without tables, so 0x40 is something else from here
        at = b.Record(at, new byte[0x70], type: 2);

        //used before this stretch's checkpoint, which defines it
        var early = at;
        at = b.Record(at, NtfsRecord((ushort)NtfsOperation.UpdateNonresidentValue, 0, new byte[8], targetAttribute: 0x40));

        var table = new byte[0x18 + 2 * 0x28];
        BitConverter.GetBytes((ushort)0x28).CopyTo(table, 0);
        BitConverter.GetBytes((ushort)2).CopyTo(table, 2);
        OpenEntry(0, 1, AttributeType.Data).CopyTo(table, 0x18);
        OpenEntry(0x200, 1, AttributeType.Data).CopyTo(table, 0x40);
        tableLsn = Lsn(at);
        at = b.Record(at, NtfsRecord((ushort)NtfsOperation.OpenAttributeTableDump, 0, table));

        var names = new byte[4 + 4 + 2 + 4];
        BitConverter.GetBytes((ushort)0x40).CopyTo(names, 0);
        BitConverter.GetBytes((ushort)4).CopyTo(names, 2);
        Encoding.Unicode.GetBytes("$J").CopyTo(names, 4);
        namesLsn = Lsn(at);
        at = b.Record(at, NtfsRecord((ushort)NtfsOperation.AttributeNamesDump, 0, names));

        var restart = new byte[0x70];
        BitConverter.GetBytes(1).CopyTo(restart, 0);
        BitConverter.GetBytes(tableLsn).CopyTo(restart, 0x10);
        BitConverter.GetBytes(namesLsn).CopyTo(restart, 0x18);
        at = b.Record(at, restart, type: 2);

        var later = at;
        b.Record(at, NtfsRecord((ushort)NtfsOperation.UpdateNonresidentValue, 0, new byte[8], targetAttribute: 0x40));

        var lf = Parse(b.Build());

        (ulong, ushort, AttributeType, string) Of(long offset) => Target(lf, Lsn(offset));

        Of(used).Should().Be((0x100UL, (ushort)2, AttributeType.IndexAllocation, "$I30"));
        Of(early).Should().Be((0x200UL, (ushort)1, AttributeType.Data, "$J"));
        Of(later).Should().Be((0x200UL, (ushort)1, AttributeType.Data, "$J"));
        lf.Records.Single(t => t.Lsn == Lsn(later)).Ntfs.TargetOpenAttributeInferred.Should().BeFalse();

        var r = lf.Records.Single(t => t.Lsn == tableLsn + 0).Ntfs.OpenAttributeTable;
        r.Select(t => t.Index).Should().Equal((ushort)0x18, (ushort)0x40);
        lf.Records.Single(t => t.Restart?.OpenAttributeTableLsn == tableLsn).Restart.AttributeNamesLsn.Should().Be(namesLsn);

        //the two records of transaction 7
        lf.Records.Where(t => t.TransactionId == 7).Select(t => t.TransactionFirstLsn).Distinct().Should().Equal(Lsn(used));
    }

    [Test]
    public void UnloggedDataIsNotAnError()
    {
        //ZeroEndOfFileRecord gives only a length: the bytes to zero
        var d = NtfsRecord((ushort)NtfsOperation.ZeroEndOfFileRecord, 0);
        BitConverter.GetBytes((ushort)0x2F0).CopyTo(d, 6); //redo length, with no data

        var b = new LogFileBuilder();
        b.Record(LogStart + DataOffset, d);

        var lf = Parse(b.Build());

        lf.ParseErrors.Should().BeEmpty();
        lf.Records[0].Ntfs.RedoLength.Should().Be(0x2F0);
        lf.Records[0].Ntfs.RedoData.Should().BeEmpty();
    }

    [Test]
    public void RandomCorruptionNeverThrows()
    {
        var sample = Sample().Take(0x40000 + 64 * PageSize).ToArray();
        var random = new Random(3);

        for (var i = 0; i < 40; i++)
        {
            var bytes = (byte[])sample.Clone();
            for (var c = 0; c < 400; c++)
            {
                bytes[random.Next(0x2000, bytes.Length)] = (byte)random.Next(256);
            }

            var lf = Parse(bytes);
            foreach (var r in lf.Records)
            {
                _ = r.ToString();
            }
        }
    }
}
