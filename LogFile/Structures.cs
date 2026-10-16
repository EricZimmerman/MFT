using System;
using System.Collections.Generic;
using System.Text;

namespace LogFile;

/// <summary>Thrown when the data is not a usable $LogFile (no valid restart page). Other problems go to ParseErrors</summary>
public class LogFileParseException : Exception
{
    public LogFileParseException(string message, long offset = -1, string field = null) : base(message)
    {
        Offset = offset;
        Field = field;
    }

    /// <summary>Byte offset of the failing read, or -1 when unknown</summary>
    public long Offset { get; }

    /// <summary>Name of the structure being read when parsing failed, if known</summary>
    public string Field { get; }
}

internal static class Fixups
{
    /// <summary>
    ///     Puts back the last two bytes of each 512 byte sector from the update sequence array (USA) of a multi-sector
    ///     structure (RSTR, RCRD pages). Returns false when the USA does not fit or a sector end does not hold the
    ///     update sequence number (a torn write)
    /// </summary>
    public static bool Apply(byte[] page)
    {
        var usaOffset = BitConverter.ToUInt16(page, 4);
        var usaCount = BitConverter.ToUInt16(page, 6);

        if (usaCount < 2 || usaOffset < 8 || usaOffset + usaCount * 2 > page.Length)
        {
            return false;
        }

        var usn = BitConverter.ToUInt16(page, usaOffset);
        var ok = true;

        for (var i = 1; i < usaCount && i * 512 <= page.Length; i++)
        {
            var end = i * 512 - 2;
            if (BitConverter.ToUInt16(page, end) != usn)
            {
                ok = false;
            }

            page[end] = page[usaOffset + i * 2];
            page[end + 1] = page[usaOffset + i * 2 + 1];
        }

        return ok;
    }
}

/// <summary>An LFS client (NTFS uses one, named "NTFS")</summary>
public class LogClient
{
    internal LogClient(byte[] page, int offset)
    {
        OldestLsn = BitConverter.ToUInt64(page, offset);
        ClientRestartLsn = BitConverter.ToUInt64(page, offset + 8);
        PreviousClient = BitConverter.ToUInt16(page, offset + 16);
        NextClient = BitConverter.ToUInt16(page, offset + 18);
        SequenceNumber = BitConverter.ToUInt16(page, offset + 20);
        var nameLength = (int)Math.Min(BitConverter.ToUInt32(page, offset + 24), 128);
        Name = Encoding.Unicode.GetString(page, offset + 28, Math.Min(nameLength, page.Length - offset - 28));
    }

    /// <summary>Records older than this are no longer needed by the client</summary>
    public ulong OldestLsn { get; }

    /// <summary>The client's restart record (NTFS: the last checkpoint)</summary>
    public ulong ClientRestartLsn { get; }

    public ushort PreviousClient { get; }
    public ushort NextClient { get; }
    public ushort SequenceNumber { get; }
    public string Name { get; }

    public override string ToString() => $"Client '{Name}': oldest LSN {OldestLsn}, restart LSN {ClientRestartLsn}";
}

/// <summary>LFS_RESTART_AREA, at the restart offset of a restart page</summary>
public class RestartArea
{
    /// <summary>Size of one LFS_CLIENT_RECORD</summary>
    private const int ClientRecordSize = 0xA0;

    internal RestartArea(byte[] page, int offset)
    {
        CurrentLsn = BitConverter.ToUInt64(page, offset);
        LogClients = BitConverter.ToUInt16(page, offset + 8);
        ClientFreeList = BitConverter.ToUInt16(page, offset + 10);
        ClientInUseList = BitConverter.ToUInt16(page, offset + 12);
        Flags = BitConverter.ToUInt16(page, offset + 14);
        SequenceNumberBits = BitConverter.ToInt32(page, offset + 16);
        RestartAreaLength = BitConverter.ToUInt16(page, offset + 20);
        ClientArrayOffset = BitConverter.ToUInt16(page, offset + 22);
        FileSize = BitConverter.ToInt64(page, offset + 24);
        LastLsnDataLength = BitConverter.ToUInt32(page, offset + 32);
        RecordHeaderLength = BitConverter.ToUInt16(page, offset + 36);
        LogPageDataOffset = BitConverter.ToUInt16(page, offset + 38);
        RestartLogOpenCount = BitConverter.ToUInt32(page, offset + 40);

        Clients = new List<LogClient>();
        for (var i = 0; i < LogClients && i < 8; i++)
        {
            var clientOffset = offset + ClientArrayOffset + i * ClientRecordSize;
            if (clientOffset + ClientRecordSize > page.Length)
            {
                break;
            }

            Clients.Add(new LogClient(page, clientOffset));
        }
    }

    /// <summary>The LSN of the last record written when this restart area was</summary>
    public ulong CurrentLsn { get; }

    public ushort LogClients { get; }
    public ushort ClientFreeList { get; }
    public ushort ClientInUseList { get; }

    /// <summary>0x2: the volume was cleanly dismounted</summary>
    public ushort Flags { get; }

    /// <summary>The number of high bits of an LSN that are a sequence number; the rest is the file offset / 8</summary>
    public int SequenceNumberBits { get; }

    public ushort RestartAreaLength { get; }
    public ushort ClientArrayOffset { get; }

    /// <summary>The size of the log, which may be less than the file</summary>
    public long FileSize { get; }

    public uint LastLsnDataLength { get; }
    public ushort RecordHeaderLength { get; }

    /// <summary>Where log records start in each log record page (after the page header and its fixups)</summary>
    public ushort LogPageDataOffset { get; }

    public uint RestartLogOpenCount { get; }

    public List<LogClient> Clients { get; }

    public bool CleanDismount => (Flags & 0x2) == 0x2;
}

/// <summary>One of the two restart pages at the start of the file ("RSTR", or "CHKD" after chkdsk)</summary>
public class RestartPage
{
    internal RestartPage(byte[] page, long offset)
    {
        Offset = offset;
        Signature = Encoding.ASCII.GetString(page, 0, 4);
        FixupOk = Fixups.Apply(page);
        ChkdskLsn = BitConverter.ToUInt64(page, 8);
        SystemPageSize = BitConverter.ToInt32(page, 16);
        LogPageSize = BitConverter.ToInt32(page, 20);
        RestartOffset = BitConverter.ToUInt16(page, 24);
        MinorVersion = BitConverter.ToInt16(page, 26);
        MajorVersion = BitConverter.ToInt16(page, 28);

        if (RestartOffset >= 0x1E && RestartOffset + 0x30 <= page.Length)
        {
            RestartArea = new RestartArea(page, RestartOffset);
        }
    }

    public long Offset { get; }
    public string Signature { get; }
    public bool FixupOk { get; }
    public ulong ChkdskLsn { get; }
    public int SystemPageSize { get; }
    public int LogPageSize { get; }
    public ushort RestartOffset { get; }
    public short MinorVersion { get; }

    /// <summary>1 (Windows 7 and older) or 2 (Windows 8 and later)</summary>
    public short MajorVersion { get; }

    /// <summary>Null when the restart offset is outside the page</summary>
    public RestartArea RestartArea { get; }

    public bool IsValid => (Signature == "RSTR" || Signature == "CHKD") && RestartArea != null;

    public override string ToString() =>
        $"{Signature} at 0x{Offset:X}: version {MajorVersion}.{MinorVersion}, current LSN {RestartArea?.CurrentLsn}";
}

/// <summary>A log record page header ("RCRD")</summary>
public class LogRecordPage
{
    internal LogRecordPage(byte[] page, long offset, bool isCopy)
    {
        Offset = offset;
        IsCopy = isCopy;
        Signature = Encoding.ASCII.GetString(page, 0, 4);
        if (Signature != "RCRD")
        {
            return;
        }

        FixupOk = Fixups.Apply(page);
        LastLsnOrFileOffset = BitConverter.ToUInt64(page, 8);
        Flags = BitConverter.ToUInt32(page, 16);
        PageCount = BitConverter.ToUInt16(page, 20);
        PagePosition = BitConverter.ToUInt16(page, 22);
        NextRecordOffset = BitConverter.ToUInt16(page, 24);
        LastEndLsn = BitConverter.ToUInt64(page, 32);
    }

    public long Offset { get; }

    /// <summary>A tail (LFS 1.1) or fast (LFS 2.0) copy of a page in the log area</summary>
    public bool IsCopy { get; }

    /// <summary>For a copy: the offset of the page it was used for, or -1 when it was older than that page</summary>
    public long CopyOf { get; internal set; } = -1;

    public string Signature { get; }
    public bool IsRecordPage => Signature == "RCRD";
    public bool FixupOk { get; }

    /// <summary>The last LSN in the page (the file offset in LFS 1.1 tail pages)</summary>
    public ulong LastLsnOrFileOffset { get; }

    /// <summary>0x1: a log record ends in this page</summary>
    public uint Flags { get; }

    public ushort PageCount { get; }
    public ushort PagePosition { get; }

    /// <summary>Where the free space in the page starts</summary>
    public ushort NextRecordOffset { get; }

    /// <summary>The LSN of the last record that ends in this page</summary>
    public ulong LastEndLsn { get; }

    public override string ToString() =>
        $"{Signature} at 0x{Offset:X}{(IsCopy ? " (copy)" : "")}: last end LSN {LastEndLsn}, page {PagePosition}/{PageCount}";
}
