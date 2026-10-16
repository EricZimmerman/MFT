using System;
using System.Collections.Generic;
using MFT;
using MFT.Attributes;
using MFT.Other;

namespace LogFile;

/// <summary>NTFS log operations (redo and undo)</summary>
public enum NtfsOperation : ushort
{
    Noop = 0x00,
    CompensationLogRecord = 0x01,
    InitializeFileRecordSegment = 0x02,
    DeallocateFileRecordSegment = 0x03,
    WriteEndOfFileRecordSegment = 0x04,
    CreateAttribute = 0x05,
    DeleteAttribute = 0x06,
    UpdateResidentValue = 0x07,
    UpdateNonresidentValue = 0x08,
    UpdateMappingPairs = 0x09,
    DeleteDirtyClusters = 0x0A,
    SetNewAttributeSizes = 0x0B,
    AddIndexEntryRoot = 0x0C,
    DeleteIndexEntryRoot = 0x0D,
    AddIndexEntryAllocation = 0x0E,
    DeleteIndexEntryAllocation = 0x0F,
    WriteEndOfIndexBuffer = 0x10,
    SetIndexEntryVcnRoot = 0x11,
    SetIndexEntryVcnAllocation = 0x12,
    UpdateFileNameRoot = 0x13,
    UpdateFileNameAllocation = 0x14,
    SetBitsInNonresidentBitMap = 0x15,
    ClearBitsInNonresidentBitMap = 0x16,
    HotFix = 0x17,
    EndTopLevelAction = 0x18,
    PrepareTransaction = 0x19,
    CommitTransaction = 0x1A,
    ForgetTransaction = 0x1B,
    OpenNonresidentAttribute = 0x1C,
    OpenAttributeTableDump = 0x1D,
    AttributeNamesDump = 0x1E,
    DirtyPageTableDump = 0x1F,
    TransactionTableDump = 0x20,
    UpdateRecordDataRoot = 0x21,
    UpdateRecordDataAllocation = 0x22,
    UpdateRelativeDataInIndex = 0x23,
    UpdateRelativeDataInIndex2 = 0x24,
    ZeroEndOfFileRecord = 0x25
}

/// <summary>An index entry from redo or undo data: the file it points to and its $FILE_NAME key</summary>
public class LoggedIndexEntry
{
    internal LoggedIndexEntry(byte[] data)
    {
        var reference = new byte[8];
        Buffer.BlockCopy(data, 0, reference, 0, 8);
        MftReference = new MftEntryInfo(reference);
        EntryLength = BitConverter.ToUInt16(data, 8);
        KeyLength = BitConverter.ToUInt16(data, 10);
        Flags = BitConverter.ToUInt32(data, 12);

        if (KeyLength >= 0x42 && 0x10 + KeyLength <= data.Length)
        {
            var key = new byte[KeyLength];
            Buffer.BlockCopy(data, 0x10, key, 0, KeyLength);
            FileInfo = new FileInfo(key);
        }
    }

    public MftEntryInfo MftReference { get; }
    public ushort EntryLength { get; }
    public ushort KeyLength { get; }
    public uint Flags { get; }

    /// <summary>The $FILE_NAME key, for $I30 entries (null otherwise)</summary>
    public FileInfo FileInfo { get; }
}

/// <summary>The duplicated information an UpdateFileNameRoot/Allocation writes into a directory's $FILE_NAME key</summary>
public class LoggedFileNameUpdate
{
    internal LoggedFileNameUpdate(byte[] data)
    {
        CreatedOn = NtfsLogRecord.FileTime(data, 0);
        ContentModifiedOn = NtfsLogRecord.FileTime(data, 8);
        RecordModifiedOn = NtfsLogRecord.FileTime(data, 16);
        LastAccessedOn = NtfsLogRecord.FileTime(data, 24);
        AllocatedSize = BitConverter.ToUInt64(data, 32);
        RealSize = BitConverter.ToUInt64(data, 40);
        FileAttributes = (StandardInfo.Flag)BitConverter.ToInt32(data, 48);
    }

    public DateTimeOffset? CreatedOn { get; }
    public DateTimeOffset? ContentModifiedOn { get; }
    public DateTimeOffset? RecordModifiedOn { get; }
    public DateTimeOffset? LastAccessedOn { get; }
    public ulong AllocatedSize { get; }
    public ulong RealSize { get; }
    public StandardInfo.Flag FileAttributes { get; }
}

/// <summary>
///     The client data of an NTFS log record: redo and undo operations, where they apply, and their data.
///     Redo data says what the change was, undo data what was there before
/// </summary>
public class NtfsLogRecord
{
    /// <summary>The header before the LCN list</summary>
    public const int HeaderSize = 0x20;

    public const int DefaultFileRecordSize = 1024;

    /// <summary>Operations whose target is a file record segment (an $MFT entry)</summary>
    private static readonly HashSet<NtfsOperation> FileRecordOperations = new()
    {
        NtfsOperation.InitializeFileRecordSegment, NtfsOperation.DeallocateFileRecordSegment,
        NtfsOperation.WriteEndOfFileRecordSegment, NtfsOperation.CreateAttribute, NtfsOperation.DeleteAttribute,
        NtfsOperation.UpdateResidentValue, NtfsOperation.UpdateMappingPairs, NtfsOperation.SetNewAttributeSizes,
        NtfsOperation.AddIndexEntryRoot, NtfsOperation.DeleteIndexEntryRoot, NtfsOperation.SetIndexEntryVcnRoot,
        NtfsOperation.UpdateFileNameRoot, NtfsOperation.UpdateRecordDataRoot, NtfsOperation.ZeroEndOfFileRecord
    };

    /// <param name="clientData">The log record's client data</param>
    /// <param name="clusterSize">Bytes per cluster, to work out the target $MFT entry</param>
    /// <param name="parseErrors">Where problems are reported</param>
    /// <param name="lsn">The record's LSN, for the messages</param>
    internal NtfsLogRecord(byte[] clientData, int clusterSize, List<string> parseErrors, ulong lsn)
    {
        RedoOperation = (NtfsOperation)BitConverter.ToUInt16(clientData, 0);
        UndoOperation = (NtfsOperation)BitConverter.ToUInt16(clientData, 2);
        RedoOffset = BitConverter.ToUInt16(clientData, 4);
        RedoLength = BitConverter.ToUInt16(clientData, 6);
        UndoOffset = BitConverter.ToUInt16(clientData, 8);
        UndoLength = BitConverter.ToUInt16(clientData, 10);
        TargetAttribute = BitConverter.ToUInt16(clientData, 12);
        LcnsToFollow = BitConverter.ToUInt16(clientData, 14);
        RecordOffset = BitConverter.ToUInt16(clientData, 16);
        AttributeOffset = BitConverter.ToUInt16(clientData, 18);
        ClusterBlockOffset = BitConverter.ToUInt16(clientData, 20);
        TargetBlockSize = BitConverter.ToUInt16(clientData, 22);
        TargetVcn = BitConverter.ToInt64(clientData, 24);

        Lcns = new List<long>();
        for (var i = 0; i < LcnsToFollow; i++)
        {
            var at = HeaderSize + i * 8;
            if (at + 8 > clientData.Length)
            {
                parseErrors.Add($"LSN {lsn}: {LcnsToFollow} LCNs do not fit in 0x{clientData.Length:X} bytes of client data");
                break;
            }

            Lcns.Add(BitConverter.ToInt64(clientData, at));
        }

        RedoData = Slice(clientData, RedoOffset, RedoLength, "redo", parseErrors, lsn);
        UndoData = Slice(clientData, UndoOffset, UndoLength, "undo", parseErrors, lsn);

        if (FileRecordOperations.Contains(RedoOperation) || FileRecordOperations.Contains(UndoOperation))
        {
            var recordSize = TargetBlockSize > 0 ? TargetBlockSize * 512 : DefaultFileRecordSize;
            TargetMftEntry = (TargetVcn * clusterSize + ClusterBlockOffset * 512L) / recordSize;
        }

        try
        {
            Decode(parseErrors, lsn);
        }
        catch (Exception e)
        {
            parseErrors.Add($"LSN {lsn}: {RedoOperation}/{UndoOperation} data could not be decoded: {e.Message}");
        }
    }

    private static byte[] Slice(byte[] data, int offset, int length, string what, List<string> parseErrors, ulong lsn)
    {
        if (length == 0)
        {
            return Array.Empty<byte>();
        }

        if (offset >= data.Length)
        {
            //not logged: ZeroEndOfFileRecord and zero-filled updates only give the length
            return Array.Empty<byte>();
        }

        if (offset + length > data.Length)
        {
            parseErrors.Add($"LSN {lsn}: {what} data at 0x{offset:X} (0x{length:X} bytes) is cut off at 0x{data.Length:X}");
            length = data.Length - offset;
        }

        var b = new byte[length];
        Buffer.BlockCopy(data, offset, b, 0, length);
        return b;
    }

    internal static DateTimeOffset? FileTime(byte[] data, int offset)
    {
        var raw = BitConverter.ToInt64(data, offset);
        if (raw <= 0 || raw > 2650467743999999999)
        {
            return null;
        }

        return DateTimeOffset.FromFileTime(raw).ToUniversalTime();
    }

    private void Decode(List<string> parseErrors, ulong lsn)
    {
        //checkpoint table dumps, and attributes opened since
        switch (RedoOperation)
        {
            case NtfsOperation.OpenAttributeTableDump:
                OpenAttributeTable = OpenAttribute.ParseTable(RedoData, lsn, parseErrors);
                return;
            case NtfsOperation.AttributeNamesDump:
                AttributeNames = OpenAttribute.ParseNames(RedoData);
                return;
            case NtfsOperation.OpenNonresidentAttribute:
                OpenedAttribute = OpenAttribute.FromOpenRecord(TargetAttribute, RedoData, UndoData, lsn);
                return;
        }

        //a whole file record: InitializeFileRecordSegment redo (a new or reused record)
        if (RedoOperation == NtfsOperation.InitializeFileRecordSegment && RedoData.Length >= 0x30 &&
            BitConverter.ToInt32(RedoData, 0) == 0x454c4946)
        {
            var record = new byte[Math.Max(RedoData.Length, DefaultFileRecordSize)];
            Buffer.BlockCopy(RedoData, 0, record, 0, RedoData.Length);
            //copies in the log were never written with fixups, so the sector ends are real data
            FileRecord = new FileRecord(record, TargetMftEntry ?? 0, false, false);
        }

        //a whole attribute: created (redo) or deleted (undo)
        if (RedoOperation == NtfsOperation.CreateAttribute)
        {
            RedoAttribute = ParseAttribute(RedoData);
        }

        if (UndoOperation == NtfsOperation.CreateAttribute)
        {
            //undo of a DeleteAttribute recreates it
            UndoAttribute = ParseAttribute(UndoData);
        }

        //index entries: added (redo) or deleted (undo, which re-adds them)
        if (RedoOperation is NtfsOperation.AddIndexEntryRoot or NtfsOperation.AddIndexEntryAllocation && RedoData.Length >= 0x10)
        {
            RedoIndexEntry = new LoggedIndexEntry(RedoData);
        }

        if (UndoOperation is NtfsOperation.AddIndexEntryRoot or NtfsOperation.AddIndexEntryAllocation && UndoData.Length >= 0x10)
        {
            UndoIndexEntry = new LoggedIndexEntry(UndoData);
        }

        if (RedoOperation is NtfsOperation.UpdateFileNameRoot or NtfsOperation.UpdateFileNameAllocation && RedoData.Length >= 0x38)
        {
            RedoFileNameUpdate = new LoggedFileNameUpdate(RedoData);
        }

        if (UndoOperation is NtfsOperation.UpdateFileNameRoot or NtfsOperation.UpdateFileNameAllocation && UndoData.Length >= 0x38)
        {
            UndoFileNameUpdate = new LoggedFileNameUpdate(UndoData);
        }
    }

    private static MFT.Attributes.Attribute ParseAttribute(byte[] data)
    {
        if (data.Length < 0x18)
        {
            return null;
        }

        var size = BitConverter.ToInt32(data, 4);
        if (size < 0x18 || size > data.Length)
        {
            size = data.Length;
        }

        var raw = new byte[size];
        Buffer.BlockCopy(data, 0, raw, 0, size);
        return MFT.FileRecord.ParseAttribute((AttributeType)BitConverter.ToInt32(data, 0), raw);
    }

    public NtfsOperation RedoOperation { get; }
    public NtfsOperation UndoOperation { get; }
    public ushort RedoOffset { get; }
    public ushort RedoLength { get; }
    public ushort UndoOffset { get; }
    public ushort UndoLength { get; }

    /// <summary>Index into the open attribute table of the attribute being changed</summary>
    public ushort TargetAttribute { get; }

    public ushort LcnsToFollow { get; }

    /// <summary>Offset of the change in the target: the attribute in a file record, or the entry in an index buffer</summary>
    public ushort RecordOffset { get; }

    /// <summary>Offset of the change within that attribute or entry</summary>
    public ushort AttributeOffset { get; }

    /// <summary>In 512 byte units, from the start of the target cluster</summary>
    public ushort ClusterBlockOffset { get; }

    /// <summary>The target block size in 512 byte units (0 in older logs)</summary>
    public ushort TargetBlockSize { get; }

    public long TargetVcn { get; }
    public List<long> Lcns { get; }

    /// <summary>
    ///     The redo bytes. Empty when the operation logs only a length (ZeroEndOfFileRecord, zero-filled updates):
    ///     <see cref="RedoLength" /> still has it
    /// </summary>
    public byte[] RedoData { get; }

    /// <summary>The undo bytes (see <see cref="RedoData" />)</summary>
    public byte[] UndoData { get; }

    /// <summary>For operations on a file record segment: the $MFT entry number</summary>
    public long? TargetMftEntry { get; }

    /// <summary>InitializeFileRecordSegment: the file record as it was written</summary>
    public FileRecord FileRecord { get; private set; }

    /// <summary>CreateAttribute: the attribute that was created</summary>
    public MFT.Attributes.Attribute RedoAttribute { get; private set; }

    /// <summary>DeleteAttribute: the attribute that was deleted (from the undo data)</summary>
    public MFT.Attributes.Attribute UndoAttribute { get; private set; }

    /// <summary>AddIndexEntryRoot/Allocation: the entry added (for $I30: a file name, its parent and timestamps)</summary>
    public LoggedIndexEntry RedoIndexEntry { get; private set; }

    /// <summary>DeleteIndexEntryRoot/Allocation: the entry deleted (from the undo data)</summary>
    public LoggedIndexEntry UndoIndexEntry { get; private set; }

    /// <summary>UpdateFileNameRoot/Allocation: the new timestamps and sizes in a directory entry</summary>
    public LoggedFileNameUpdate RedoFileNameUpdate { get; private set; }

    /// <summary>UpdateFileNameRoot/Allocation: the previous timestamps and sizes</summary>
    public LoggedFileNameUpdate UndoFileNameUpdate { get; private set; }

    /// <summary>OpenAttributeTableDump: the attributes NTFS had open at the checkpoint</summary>
    public List<OpenAttribute> OpenAttributeTable { get; private set; }

    /// <summary>AttributeNamesDump: the names of the open attributes, by table offset</summary>
    public Dictionary<ushort, string> AttributeNames { get; private set; }

    /// <summary>OpenNonresidentAttribute: the attribute that was opened, at table offset <see cref="TargetAttribute" /></summary>
    public OpenAttribute OpenedAttribute { get; private set; }

    /// <summary>
    ///     The open attribute <see cref="TargetAttribute" /> referred to at this record's LSN: the file and attribute
    ///     changed (for operations on file records, $MFT's $DATA). Null when no checkpoint or open record defines it
    /// </summary>
    public OpenAttribute TargetOpenAttribute { get; internal set; }

    /// <summary>
    ///     True when <see cref="TargetOpenAttribute" /> comes from across missing log data (no checkpoint or open
    ///     record defines it in this record's stretch of the log), so the table offset may have been reused since
    /// </summary>
    public bool TargetOpenAttributeInferred { get; internal set; }

    public override string ToString() =>
        $"{RedoOperation}/{UndoOperation} target {(TargetMftEntry.HasValue ? $"MFT entry {TargetMftEntry}" : $"attribute {TargetAttribute}")}, redo 0x{RedoLength:X}, undo 0x{UndoLength:X}";
}
