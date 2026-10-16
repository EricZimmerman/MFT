using System;

namespace LogFile;

public enum LogRecordType : uint
{
    /// <summary>An update by the client (for NTFS: an NtfsLogRecord)</summary>
    ClientRecord = 1,

    /// <summary>The client's restart area (for NTFS: a checkpoint)</summary>
    ClientRestart = 2
}

/// <summary>An LFS log record: the 0x30 byte LFS_RECORD_HEADER and the client data after it</summary>
public class LogRecord
{
    /// <summary>LFS_RECORD_HEADER size</summary>
    public const int HeaderSize = 0x30;

    internal LogRecord(byte[] header, long fileOffset)
    {
        FileOffset = fileOffset;
        Lsn = BitConverter.ToUInt64(header, 0);
        ClientPreviousLsn = BitConverter.ToUInt64(header, 8);
        ClientUndoNextLsn = BitConverter.ToUInt64(header, 16);
        ClientDataLength = BitConverter.ToUInt32(header, 24);
        ClientSequenceNumber = BitConverter.ToUInt16(header, 28);
        ClientIndex = BitConverter.ToUInt16(header, 30);
        RecordType = (LogRecordType)BitConverter.ToUInt32(header, 32);
        TransactionId = BitConverter.ToUInt32(header, 36);
        Flags = BitConverter.ToUInt16(header, 40);
    }

    /// <summary>Where the record header is in the file</summary>
    public long FileOffset { get; }

    public ulong Lsn { get; }

    /// <summary>The client's previous record (0 for none)</summary>
    public ulong ClientPreviousLsn { get; }

    /// <summary>The next record to undo for this transaction</summary>
    public ulong ClientUndoNextLsn { get; }

    public uint ClientDataLength { get; }
    public ushort ClientSequenceNumber { get; }
    public ushort ClientIndex { get; }
    public LogRecordType RecordType { get; }
    public uint TransactionId { get; }

    /// <summary>0x1: the record continues on the next page(s)</summary>
    public ushort Flags { get; }

    /// <summary>The LSN's sequence number (how many times the log has wrapped)</summary>
    public ulong SequenceNumber { get; internal set; }

    /// <summary>The client data (the part that could be read, when <see cref="Truncated" />)</summary>
    public byte[] ClientData { get; internal set; } = Array.Empty<byte>();

    /// <summary>The record continues into a page that is missing, damaged or not a log record page</summary>
    public bool Truncated { get; internal set; }

    /// <summary>For client records of the NTFS client: the NTFS operation (null for restart records)</summary>
    public NtfsLogRecord Ntfs { get; internal set; }

    /// <summary>For client restart records: the NTFS restart area of the checkpoint</summary>
    public NtfsRestartArea Restart { get; internal set; }

    /// <summary>
    ///     The first record of this record's transaction, from <see cref="ClientPreviousLsn" /> (which links the records
    ///     of a transaction); for a transaction that began before the oldest record in the log, the LSN its chain leaves
    ///     the log at. Transaction ids are slots that are reused, so this is what tells transactions apart
    /// </summary>
    public ulong TransactionFirstLsn { get; internal set; }

    public override string ToString() =>
        $"LSN {Lsn} at 0x{FileOffset:X}: {RecordType}, transaction {TransactionId}, previous {ClientPreviousLsn}{(Ntfs != null ? ", " + Ntfs : "")}{(Truncated ? " (truncated)" : "")}";
}
