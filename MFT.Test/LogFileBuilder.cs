using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MFT.Test;

/// <summary>
///     Builds LFS 2.0 $LogFiles: two restart pages, 32 fast pages, then the log area. Records are placed at file offsets
///     (their LSN is the sequence number and the offset / 8) and continue across page data areas like real ones.
///     Page headers are filled in from the records, and fixups are applied, by <see cref="Build" />
/// </summary>
public sealed class LogFileBuilder
{
    public const int PageSize = 0x1000;
    public const int DataOffset = 0x40;
    public const int SequenceNumberBits = 40;
    public const int LogStart = 34 * PageSize;

    private readonly byte[] _image;
    private readonly Dictionary<long, (ulong Last, ulong LastEnd, int Next)> _pages = new();
    private readonly HashSet<long> _explicitHeaders = new();

    public LogFileBuilder(int logPages = 16)
    {
        _image = new byte[LogStart + logPages * PageSize];
        CurrentLsn = 0;
    }

    public ulong CurrentLsn { get; set; }
    public long FileSize => _image.Length;

    public static ulong Lsn(long offset, ulong sequence = 1) => sequence << (64 - SequenceNumberBits) | (ulong)offset >> 3;

    /// <summary>NTFS client data: the 0x20 byte header, LCNs, then redo and undo data</summary>
    public static byte[] NtfsRecord(ushort redoOp, ushort undoOp, byte[] redo = null, byte[] undo = null, long vcn = 0,
        ushort clusterBlockOffset = 0, ushort targetAttribute = 0x18, long[] lcns = null, ushort targetBlockSize = 2)
    {
        redo ??= Array.Empty<byte>();
        undo ??= Array.Empty<byte>();
        lcns ??= new[] { 1234L };
        var redoOffset = 0x20 + lcns.Length * 8;
        var undoOffset = redoOffset + ((redo.Length + 7) & ~7);
        var d = new byte[undoOffset + undo.Length];
        BitConverter.GetBytes(redoOp).CopyTo(d, 0);
        BitConverter.GetBytes(undoOp).CopyTo(d, 2);
        BitConverter.GetBytes((ushort)redoOffset).CopyTo(d, 4);
        BitConverter.GetBytes((ushort)redo.Length).CopyTo(d, 6);
        BitConverter.GetBytes((ushort)undoOffset).CopyTo(d, 8);
        BitConverter.GetBytes((ushort)undo.Length).CopyTo(d, 10);
        BitConverter.GetBytes(targetAttribute).CopyTo(d, 12);
        BitConverter.GetBytes((ushort)lcns.Length).CopyTo(d, 14);
        BitConverter.GetBytes(clusterBlockOffset).CopyTo(d, 20);
        BitConverter.GetBytes(targetBlockSize).CopyTo(d, 22);
        BitConverter.GetBytes(vcn).CopyTo(d, 24);
        for (var i = 0; i < lcns.Length; i++)
        {
            BitConverter.GetBytes(lcns[i]).CopyTo(d, 0x20 + i * 8);
        }

        redo.CopyTo(d, redoOffset);
        undo.CopyTo(d, undoOffset);
        return d;
    }

    /// <summary>Writes a record at <paramref name="offset" />; returns where the next record goes</summary>
    public long Record(long offset, byte[] clientData, uint transaction = 1, ulong sequence = 1, uint type = 1,
        ulong previous = 0)
    {
        var lsn = Lsn(offset, sequence);
        var header = new byte[0x30];
        BitConverter.GetBytes(lsn).CopyTo(header, 0);
        BitConverter.GetBytes(previous).CopyTo(header, 8);
        BitConverter.GetBytes((uint)clientData.Length).CopyTo(header, 24);
        BitConverter.GetBytes(type).CopyTo(header, 32);
        BitConverter.GetBytes(transaction).CopyTo(header, 36);

        var startPage = offset / PageSize * PageSize;
        header.CopyTo(_image, offset);
        Note(startPage, lsn, null, (int)(offset - startPage) + 0x30);

        var at = offset + 0x30;
        var page = startPage;
        var copied = 0;
        while (copied < clientData.Length)
        {
            if (at >= page + PageSize)
            {
                page += PageSize;
                at = page + DataOffset;
                Header(page)[20] = 1; //touch
            }

            var n = (int)Math.Min(page + PageSize - at, clientData.Length - copied);
            Buffer.BlockCopy(clientData, copied, _image, (int)at, n);
            copied += n;
            at += n;
        }

        //the record ends in this page
        Note(page, null, lsn, (int)(at - page));
        CurrentLsn = Math.Max(CurrentLsn, lsn);

        var next = (at + 7) & ~7L;
        if (page + PageSize - next < 0x30)
        {
            next = page + PageSize + DataOffset;
        }

        return next;
    }

    private byte[] Header(long page)
    {
        if (_pages.ContainsKey(page) == false)
        {
            _pages[page] = (0, 0, DataOffset);
        }

        return new byte[24];
    }

    private void Note(long page, ulong? last, ulong? lastEnd, int next)
    {
        _pages.TryGetValue(page, out var p);
        _pages[page] = (last ?? p.Last, lastEnd ?? p.LastEnd, Math.Max(next, p.Next));
    }

    /// <summary>Copies a log page (as it will be built) to a fast page, optionally with a different last LSN</summary>
    public void FastCopy(int fastIndex, long page, ulong? lastLsn = null, ulong? lastEndLsn = null)
    {
        Buffer.BlockCopy(_image, (int)page, _image, (2 + fastIndex) * PageSize, PageSize);
        var p = _pages[page];
        _pages[(2 + fastIndex) * PageSize] = (lastLsn ?? p.Last, lastEndLsn ?? p.LastEnd, p.Next);
    }

    /// <summary>Overwrites bytes in the unprotected image (before <see cref="Build" />)</summary>
    public void Poke(long offset, params byte[] bytes) => bytes.CopyTo(_image, offset);

    public byte[] Build(int? truncateTo = null)
    {
        var image = (byte[])_image.Clone();

        foreach (var restart in new long[] { 0, PageSize })
        {
            var page = new byte[PageSize];
            Encoding.ASCII.GetBytes("RSTR").CopyTo(page, 0);
            BitConverter.GetBytes((ushort)0x1E).CopyTo(page, 4);
            BitConverter.GetBytes((ushort)9).CopyTo(page, 6);
            BitConverter.GetBytes(PageSize).CopyTo(page, 16);
            BitConverter.GetBytes(PageSize).CopyTo(page, 20);
            BitConverter.GetBytes((ushort)0x30).CopyTo(page, 24);
            BitConverter.GetBytes((short)2).CopyTo(page, 28);
            var ra = 0x30;
            BitConverter.GetBytes(restart == 0 ? CurrentLsn : CurrentLsn / 2).CopyTo(page, ra);
            BitConverter.GetBytes((ushort)1).CopyTo(page, ra + 8);
            BitConverter.GetBytes((ushort)0xFFFF).CopyTo(page, ra + 10);
            BitConverter.GetBytes(SequenceNumberBits).CopyTo(page, ra + 16);
            BitConverter.GetBytes((ushort)0xE0).CopyTo(page, ra + 20);
            BitConverter.GetBytes((ushort)0x40).CopyTo(page, ra + 22);
            BitConverter.GetBytes((long)image.Length).CopyTo(page, ra + 24);
            BitConverter.GetBytes((ushort)0x30).CopyTo(page, ra + 36);
            BitConverter.GetBytes((ushort)DataOffset).CopyTo(page, ra + 38);
            var client = ra + 0x40;
            BitConverter.GetBytes((ushort)0xFFFF).CopyTo(page, client + 16);
            BitConverter.GetBytes((ushort)0xFFFF).CopyTo(page, client + 18);
            BitConverter.GetBytes(8).CopyTo(page, client + 24);
            Encoding.Unicode.GetBytes("NTFS").CopyTo(page, client + 28);
            Protect(page, 0x1E);
            page.CopyTo(image, restart);
        }

        foreach (var (pageOffset, h) in _pages)
        {
            var page = new byte[PageSize];
            Buffer.BlockCopy(image, (int)pageOffset, page, 0, PageSize);
            Encoding.ASCII.GetBytes("RCRD").CopyTo(page, 0);
            BitConverter.GetBytes((ushort)0x28).CopyTo(page, 4);
            BitConverter.GetBytes((ushort)9).CopyTo(page, 6);
            BitConverter.GetBytes(h.Last).CopyTo(page, 8);
            BitConverter.GetBytes(h.LastEnd != 0 ? 1u : 0u).CopyTo(page, 16);
            BitConverter.GetBytes((ushort)1).CopyTo(page, 20);
            BitConverter.GetBytes((ushort)1).CopyTo(page, 22);
            BitConverter.GetBytes((ushort)h.Next).CopyTo(page, 24);
            BitConverter.GetBytes(h.LastEnd).CopyTo(page, 32);
            Protect(page, 0x28);
            page.CopyTo(image, pageOffset);
        }

        return truncateTo.HasValue ? image.Take(truncateTo.Value).ToArray() : image;
    }

    /// <summary>Fixups: each sector's last two bytes go in the update sequence array, replaced by the sequence number</summary>
    private static void Protect(byte[] page, int usaOffset)
    {
        BitConverter.GetBytes((ushort)0x0042).CopyTo(page, usaOffset);
        for (var i = 1; i <= PageSize / 512; i++)
        {
            var end = i * 512 - 2;
            page[usaOffset + i * 2] = page[end];
            page[usaOffset + i * 2 + 1] = page[end + 1];
            BitConverter.GetBytes((ushort)0x0042).CopyTo(page, end);
        }
    }
}
