using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Serilog;

namespace LogFile;

/// <summary>
///     NTFS $LogFile (the LFS journal).
///     <para>
///         Two restart pages, then (LFS 1.1) two tail pages or (LFS 2.0) 32 fast pages that are copies of recent log
///         pages, then the circular log area. Every page is protected by fixups. An LSN is a sequence number (the
///         high <see cref="RestartArea.SequenceNumberBits" /> bits) and the file offset / 8 (the rest), so a record is
///         only accepted where its own LSN says it is; records from earlier passes through the log are kept too.
///         Records are 8 byte aligned, never start within the last 0x30 bytes of a page, and continue past a page
///         end into the next page's data area. A copy that is newer than the page it copies is used instead of it,
///         also for pages past the end of a $LogFile that was not collected whole.
///     </para>
/// </summary>
public class LogFile
{
    /// <summary>Pages between the restart pages and the log area in LFS 2.0</summary>
    private const int FastPages = 32;

    /// <summary>Pages between the restart pages and the log area in LFS 1.1</summary>
    private const int TailPages = 2;

    /// <summary>Unprotected log record pages, by their offset in the log area</summary>
    private readonly Dictionary<long, byte[]> _pageData = new();

    private readonly Dictionary<long, LogRecordPage> _pagesByOffset = new();
    private readonly HashSet<long> _visited = new();
    private readonly Dictionary<ulong, LogRecord> _records = new();
    private readonly long _logStart;
    private readonly long _logEnd;
    private readonly int _dataOffset;

    /// <param name="fileStream">The $LogFile</param>
    /// <param name="clusterSize">Bytes per cluster of the volume (from $Boot), to work out target $MFT entries</param>
    /// <exception cref="LogFileParseException">No valid restart page, or the log was never used</exception>
    public LogFile(Stream fileStream, int clusterSize = 4096)
    {
        ClusterSize = clusterSize;

        if (fileStream.Length > int.MaxValue)
        {
            throw new LogFileParseException($"$LogFile is 0x{fileStream.Length:X} bytes; at most 2 GB is supported");
        }

        var bytes = new byte[fileStream.Length];
        var read = 0;
        while (read < bytes.Length)
        {
            var n = fileStream.Read(bytes, read, bytes.Length - read);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        FileLength = read;

        if (read < 0x1000)
        {
            throw new LogFileParseException($"$LogFile is only 0x{read:X} bytes", 0, "restart page");
        }

        if (bytes.Take(0x1000).All(b => b == 0xFF))
        {
            //mkntfs and Windows leave it like this until the volume is first mounted
            throw new LogFileParseException("$LogFile has not been initialized (all 0xFF)", 0, "restart page");
        }

        RestartPages = new List<RestartPage>();
        foreach (var offset in new long[] { 0, PageSizeAt(bytes, 0) })
        {
            if (offset + 0x200 > read)
            {
                continue;
            }

            RestartPages.Add(new RestartPage(Copy(bytes, offset, (int)Math.Min(PageSizeAt(bytes, offset), read - offset)), offset));
        }

        //the restart area with the most recent current LSN is the one in use
        var current = RestartPages.Where(t => t.IsValid && t.LogPageSize is >= 0x200 and <= 0x10000 &&
                                              (t.LogPageSize & (t.LogPageSize - 1)) == 0 &&
                                              t.RestartArea.SequenceNumberBits is > 3 and < 64)
            .OrderByDescending(t => t.RestartArea.CurrentLsn).FirstOrDefault();

        if (current == null)
        {
            throw new LogFileParseException("No valid restart page ('RSTR' or 'CHKD') found", 0, "restart page");
        }

        foreach (var rp in RestartPages.Where(t => t.FixupOk == false))
        {
            ParseErrors.Add($"Restart page at 0x{rp.Offset:X}: fixups do not match (torn write)");
        }

        CurrentRestartPage = current;
        PageSize = current.LogPageSize;
        MajorVersion = current.MajorVersion;
        MinorVersion = current.MinorVersion;
        SequenceNumberBits = current.RestartArea.SequenceNumberBits;
        _dataOffset = current.RestartArea.LogPageDataOffset;

        if (_dataOffset < 0x28 || _dataOffset >= PageSize - LogRecord.HeaderSize)
        {
            ParseErrors.Add($"Log page data offset 0x{_dataOffset:X} is not usable; using 0x40");
            _dataOffset = 0x40;
        }

        var copies = MajorVersion >= 2 ? FastPages : TailPages;
        _logStart = (2L + copies) * PageSize;

        //the log is as big as the restart area says; a file cut short is missing the pages after its end
        var logSize = current.RestartArea.FileSize;
        if (logSize < _logStart + PageSize || logSize > int.MaxValue)
        {
            logSize = read;
        }

        _logEnd = logSize - Math.Max(0, logSize - _logStart) % PageSize;

        if (read < _logEnd)
        {
            ParseErrors.Add($"$LogFile is 0x{read:X} bytes, but the log is 0x{_logEnd:X}; the pages after the end of the file are missing");
        }

        Log.Debug("LFS {Major}.{Minor}, page size 0x{PageSize:X}, log area 0x{Start:X}-0x{End:X}", MajorVersion,
            MinorVersion, PageSize, _logStart, _logEnd);

        ReadPages(bytes, read, copies);

        //every page start can begin a run of records; runs also continue across pages
        foreach (var page in _pageData.Keys.OrderBy(t => t).ToList())
        {
            Walk(page + _dataOffset);
        }

        //records from earlier passes through the log can start in the middle of a page that newer records only
        //partly overwrote, where no run reaches them: look for headers whose LSN says they are right there
        foreach (var page in _pageData.Keys.OrderBy(t => t).ToList())
        {
            var data = _pageData[page];
            for (var at = _dataOffset; at + LogRecord.HeaderSize <= PageSize; at += 8)
            {
                if (_visited.Contains(page + at) == false &&
                    LsnToOffset(BitConverter.ToUInt64(data, at)) == page + at)
                {
                    Walk(page + at);
                }
            }
        }

        Records = _records.Values.OrderBy(t => t.Lsn).ToList();

        foreach (var record in Records.Where(t => t.RecordType == LogRecordType.ClientRecord &&
                                                 t.ClientData.Length >= NtfsLogRecord.HeaderSize))
        {
            record.Ntfs = new NtfsLogRecord(record.ClientData, ClusterSize, ParseErrors, record.Lsn);
        }
    }

    private static byte[] Copy(byte[] bytes, long offset, int length)
    {
        var b = new byte[length];
        Buffer.BlockCopy(bytes, (int)offset, b, 0, length);
        return b;
    }

    private static int PageSizeAt(byte[] bytes, long offset)
    {
        var size = BitConverter.ToInt32(bytes, (int)offset + 20);
        return size is >= 0x200 and <= 0x10000 && (size & (size - 1)) == 0 ? size : 0x1000;
    }

    /// <summary>Unprotects every log page, and puts newer tail/fast copies in place of the pages they copy</summary>
    private void ReadPages(byte[] bytes, int read, int copies)
    {
        for (var page = _logStart; page + PageSize <= Math.Min(read, _logEnd); page += PageSize)
        {
            var data = Copy(bytes, page, PageSize);
            var p = new LogRecordPage(data, page, false);
            _pagesByOffset[page] = p;

            if (p.IsRecordPage)
            {
                _pageData[page] = data;
                if (p.FixupOk == false)
                {
                    ParseErrors.Add($"Log page at 0x{page:X}: fixups do not match (torn write)");
                }
            }
        }

        var copyPages = new List<LogRecordPage>();

        for (var i = 0; i < copies; i++)
        {
            var offset = (2L + i) * PageSize;
            if (offset + PageSize > Math.Min(read, _logStart))
            {
                break;
            }

            var data = Copy(bytes, offset, PageSize);
            var copy = new LogRecordPage(data, offset, true);
            copyPages.Add(copy);

            if (copy.IsRecordPage == false || copy.LastEndLsn == 0)
            {
                continue;
            }

            //the page it copies is where its last record ends
            var target = LsnToOffset(copy.LastEndLsn) / PageSize * PageSize;
            if (target < _logStart || target >= _logEnd)
            {
                continue;
            }

            //newer means a higher last LSN in the page (LFS 2.0). A copy can have the same last LSN with a higher last
            //end LSN when its last record continues into the next page in the log area; that copy is not newer.
            //LFS 1.1 tail pages have their file offset in that field, so they are compared by the last end LSN
            if (_pagesByOffset.TryGetValue(target, out var existing) && existing.IsRecordPage)
            {
                var copyNewer = MajorVersion >= 2
                    ? copy.LastLsnOrFileOffset > existing.LastLsnOrFileOffset
                    : copy.LastEndLsn > existing.LastEndLsn;

                if (copyNewer == false)
                {
                    continue;
                }
            }

            //the copy is newer (or the page is missing): use its contents for that page
            copy.CopyOf = target;
            _pageData[target] = data;
            _pagesByOffset[target] = new LogRecordPage(Copy(data, 0, PageSize), target, false);
        }

        Pages.AddRange(_pagesByOffset.Values);
        Pages.AddRange(copyPages);
        Pages.Sort((a, b) => a.Offset.CompareTo(b.Offset));
    }

    private long NextPage(long pageOffset)
    {
        var next = pageOffset + PageSize;
        return next >= _logEnd ? _logStart : next;
    }

    /// <summary>Reads records from <paramref name="position" /> on, for as long as each one is where its LSN says</summary>
    private void Walk(long position)
    {
        while (true)
        {
            var page = position / PageSize * PageSize;
            var at = (int)(position - page);

            if (_pageData.TryGetValue(page, out var data) == false || PageSize - at < LogRecord.HeaderSize ||
                _visited.Add(position) == false)
            {
                return;
            }

            var lsn = BitConverter.ToUInt64(data, at);
            if (lsn == 0 || LsnToOffset(lsn) != position)
            {
                _visited.Remove(position);
                return;
            }

            var record = new LogRecord(Copy(data, at, LogRecord.HeaderSize), position)
                { SequenceNumber = LsnToSequenceNumber(lsn) };

            if (record.RecordType is not (LogRecordType.ClientRecord or LogRecordType.ClientRestart) ||
                record.ClientDataLength > _logEnd - _logStart)
            {
                return;
            }

            //the client data, continuing into the following pages' data areas
            var clientData = new byte[record.ClientDataLength];
            var copied = 0;
            var dataPage = page;
            var dataAt = at + LogRecord.HeaderSize;

            while (copied < clientData.Length)
            {
                var available = Math.Min(PageSize - dataAt, clientData.Length - copied);
                if (available > 0)
                {
                    Buffer.BlockCopy(data, dataAt, clientData, copied, available);
                    copied += available;
                    dataAt += available;
                }

                if (copied == clientData.Length)
                {
                    break;
                }

                dataPage = NextPage(dataPage);
                if (dataPage == page || _pageData.TryGetValue(dataPage, out data) == false)
                {
                    record.Truncated = true;
                    break;
                }

                dataAt = _dataOffset;
            }

            if (record.Truncated)
            {
                clientData = Copy(clientData, 0, copied);
                ParseErrors.Add(
                    $"LSN {lsn} at 0x{position:X}: 0x{record.ClientDataLength:X} bytes of client data, only 0x{copied:X} could be read");
            }

            record.ClientData = clientData;

            if (_records.ContainsKey(lsn) == false)
            {
                _records.Add(lsn, record);
            }

            if (record.Truncated)
            {
                return;
            }

            //the next record is 8 byte aligned after this one, or at the next page's data area
            var next = (dataAt + 7) & ~7;
            position = PageSize - next < LogRecord.HeaderSize ? NextPage(dataPage) + _dataOffset : dataPage + next;
        }
    }

    /// <summary>The file offset an LSN refers to</summary>
    public long LsnToOffset(ulong lsn) => (long)((lsn << SequenceNumberBits) >> (SequenceNumberBits - 3));

    /// <summary>How many times the log had wrapped when the LSN was written</summary>
    public ulong LsnToSequenceNumber(ulong lsn) => lsn >> (64 - SequenceNumberBits);

    /// <summary>Bytes per cluster, used for target $MFT entries (4096 unless given)</summary>
    public int ClusterSize { get; }

    /// <summary>The number of bytes read (less than the log size when the file was not collected whole)</summary>
    public long FileLength { get; }

    public List<RestartPage> RestartPages { get; }

    /// <summary>The restart page in use (the most recent current LSN)</summary>
    public RestartPage CurrentRestartPage { get; }

    public RestartArea RestartArea => CurrentRestartPage.RestartArea;

    public int PageSize { get; }

    /// <summary>1 (Windows 7 and older) or 2 (Windows 8 and later)</summary>
    public short MajorVersion { get; }

    public short MinorVersion { get; }
    public int SequenceNumberBits { get; }

    /// <summary>The log area's pages as they were used (with newer copies in place), and the copies themselves</summary>
    public List<LogRecordPage> Pages { get; } = new();

    /// <summary>Every log record found, by LSN</summary>
    public List<LogRecord> Records { get; }

    /// <summary>Torn pages, truncated records, missing pages and data that could not be decoded; the rest is kept</summary>
    public List<string> ParseErrors { get; } = new();
}
