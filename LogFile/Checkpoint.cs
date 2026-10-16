using System;
using System.Collections.Generic;
using System.Text;
using MFT.Attributes;
using MFT.Other;

namespace LogFile;

/// <summary>
///     The NTFS restart area: the client data of a client restart record, written at each checkpoint. It points to
///     the table dumps of the same checkpoint
/// </summary>
public class NtfsRestartArea
{
    /// <summary>Client data this long or longer is decoded</summary>
    public const int MinimumSize = 0x40;

    internal NtfsRestartArea(byte[] data)
    {
        MajorVersion = BitConverter.ToUInt32(data, 0x00);
        MinorVersion = BitConverter.ToUInt32(data, 0x04);
        StartOfCheckpointLsn = BitConverter.ToUInt64(data, 0x08);
        OpenAttributeTableLsn = BitConverter.ToUInt64(data, 0x10);
        AttributeNamesLsn = BitConverter.ToUInt64(data, 0x18);
        DirtyPageTableLsn = BitConverter.ToUInt64(data, 0x20);
        TransactionTableLsn = BitConverter.ToUInt64(data, 0x28);
        OpenAttributeTableLength = BitConverter.ToUInt32(data, 0x30);
        AttributeNamesLength = BitConverter.ToUInt32(data, 0x34);
        DirtyPageTableLength = BitConverter.ToUInt32(data, 0x38);
        TransactionTableLength = BitConverter.ToUInt32(data, 0x3C);
    }

    /// <summary>0: Windows 7 and older table layouts; 1: Windows 8 and later</summary>
    public uint MajorVersion { get; }

    public uint MinorVersion { get; }

    /// <summary>Where recovery starts</summary>
    public ulong StartOfCheckpointLsn { get; }

    /// <summary>The OpenAttributeTableDump record of this checkpoint (0 for none)</summary>
    public ulong OpenAttributeTableLsn { get; }

    /// <summary>The AttributeNamesDump record of this checkpoint (0 for none)</summary>
    public ulong AttributeNamesLsn { get; }

    public ulong DirtyPageTableLsn { get; }
    public ulong TransactionTableLsn { get; }
    public uint OpenAttributeTableLength { get; }
    public uint AttributeNamesLength { get; }
    public uint DirtyPageTableLength { get; }
    public uint TransactionTableLength { get; }

    public override string ToString() =>
        $"NTFS restart area {MajorVersion}.{MinorVersion}: checkpoint at LSN {StartOfCheckpointLsn}, open attribute table at LSN {OpenAttributeTableLsn}, attribute names at LSN {AttributeNamesLsn}";
}

/// <summary>
///     An entry of the open attribute table: an attribute NTFS had open, which log records refer to by its offset in
///     the table (<see cref="NtfsLogRecord.TargetAttribute" />). Comes from an OpenAttributeTableDump (a checkpoint)
///     or an OpenNonresidentAttribute record
/// </summary>
public class OpenAttribute
{
    /// <summary>Windows 8 and later</summary>
    public const int EntrySizeV1 = 0x28;

    /// <summary>Windows 7 and older</summary>
    public const int EntrySizeV0 = 0x2C;

    private OpenAttribute(ushort index, byte[] data, int offset, int entrySize, ulong definedAtLsn, bool fromCheckpoint)
    {
        Index = index;
        DefinedAtLsn = definedAtLsn;
        FromCheckpoint = fromCheckpoint;

        var reference = new byte[8];
        if (entrySize >= EntrySizeV0)
        {
            //AllocatedOrNextFree, a 32 bit pointer, file reference, LSN, dirty pages seen, 3 bytes, type, a name pointer, bytes per index buffer
            Buffer.BlockCopy(data, offset + 0x08, reference, 0, 8);
            LsnOfOpenRecord = BitConverter.ToUInt64(data, offset + 0x10);
            DirtyPagesSeen = data[offset + 0x18] != 0;
            AttributeType = (AttributeType)BitConverter.ToInt32(data, offset + 0x1C);
            BytesPerIndexBuffer = BitConverter.ToUInt32(data, offset + 0x28);
        }
        else
        {
            //AllocatedOrNextFree, bytes per index buffer, type, dirty pages seen, file reference, LSN, a 64 bit pointer
            BytesPerIndexBuffer = BitConverter.ToUInt32(data, offset + 0x04);
            AttributeType = (AttributeType)BitConverter.ToInt32(data, offset + 0x08);
            DirtyPagesSeen = data[offset + 0x0C] != 0;
            Buffer.BlockCopy(data, offset + 0x10, reference, 0, 8);
            LsnOfOpenRecord = BitConverter.ToUInt64(data, offset + 0x18);
        }

        FileReference = new MftEntryInfo(reference);
    }

    /// <summary>The entry's offset in the table: the value of TargetAttribute in the records that use it</summary>
    public ushort Index { get; }

    /// <summary>The file the attribute belongs to</summary>
    public MftEntryInfo FileReference { get; }

    public AttributeType AttributeType { get; }

    /// <summary>
    ///     The attribute's name (such as $I30 or $J), empty when it has none, null when it is not known: Windows 7
    ///     open records do not hold the name, so it comes from a later names dump of the same attribute
    /// </summary>
    public string AttributeName { get; internal set; }


    /// <summary>Index buffer size, for index allocations</summary>
    public uint BytesPerIndexBuffer { get; }

    public bool DirtyPagesSeen { get; }

    /// <summary>When NTFS opened the attribute (as stored; Windows 7 logs do not use the log's LSNs here)</summary>
    public ulong LsnOfOpenRecord { get; }

    /// <summary>The record this came from: the OpenAttributeTableDump or the OpenNonresidentAttribute</summary>
    public ulong DefinedAtLsn { get; }

    /// <summary>True when from a checkpoint's table dump, false when from an OpenNonresidentAttribute record</summary>
    public bool FromCheckpoint { get; }

    /// <summary>The allocated entries of a restart table (OpenAttributeTableDump redo data)</summary>
    internal static List<OpenAttribute> ParseTable(byte[] data, ulong lsn, List<string> parseErrors)
    {
        var entries = new List<OpenAttribute>();
        if (data.Length < RestartTableHeaderSize)
        {
            return entries;
        }

        var entrySize = BitConverter.ToUInt16(data, 0);
        var count = BitConverter.ToUInt16(data, 2);
        if (entrySize != EntrySizeV1 && entrySize != EntrySizeV0)
        {
            parseErrors.Add($"LSN {lsn}: open attribute table entries of 0x{entrySize:X} bytes are not known");
            return entries;
        }

        for (var i = 0; i < count; i++)
        {
            var offset = RestartTableHeaderSize + i * entrySize;
            if (offset + entrySize > data.Length || offset > ushort.MaxValue)
            {
                parseErrors.Add($"LSN {lsn}: open attribute table has {count} entries, but only {i} fit in 0x{data.Length:X} bytes");
                break;
            }

            if (BitConverter.ToUInt32(data, offset) == Allocated)
            {
                entries.Add(new OpenAttribute((ushort)offset, data, offset, entrySize, lsn, true));
            }
        }

        return entries;
    }

    /// <summary>OpenNonresidentAttribute: the entry is the redo data, the name the undo data (Windows 8 and later)</summary>
    internal static OpenAttribute FromOpenRecord(ushort index, byte[] redo, byte[] undo, ulong lsn)
    {
        if (redo.Length != EntrySizeV1 && redo.Length != EntrySizeV0)
        {
            return null;
        }

        var entry = new OpenAttribute(index, redo, 0, redo.Length, lsn, false);
        if (redo.Length == EntrySizeV1)
        {
            entry.AttributeName = undo.Length >= 2 ? Encoding.Unicode.GetString(undo, 0, undo.Length & ~1) : string.Empty;
        }

        return entry;
    }

    /// <summary>AttributeNamesDump redo data: the names of named open attributes, by their table offset</summary>
    internal static Dictionary<ushort, string> ParseNames(byte[] data)
    {
        var names = new Dictionary<ushort, string>();
        var at = 0;
        while (at + 4 <= data.Length)
        {
            var index = BitConverter.ToUInt16(data, at);
            var length = BitConverter.ToUInt16(data, at + 2);
            if (index == 0 && length == 0)
            {
                break;
            }

            if (at + 4 + length > data.Length)
            {
                break;
            }

            names[index] = Encoding.Unicode.GetString(data, at + 4, length & ~1);

            //the name, then a NUL character
            at += 4 + length + 2;
        }

        return names;
    }

    private const int RestartTableHeaderSize = 0x18;
    private const uint Allocated = 0xFFFFFFFF;

    public override string ToString() =>
        $"0x{Index:X}: {FileReference} {AttributeType}{(string.IsNullOrEmpty(AttributeName) ? "" : ":" + AttributeName)} (LSN {DefinedAtLsn})";
}
