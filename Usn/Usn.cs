using System;
using System.Collections.Generic;
using System.IO;
using Serilog;

namespace Usn;

/// <summary>
///     $UsnJrnl:$J. Records are 8 byte aligned: length (4), major version (2), minor version (2), then the record.
///     V2 and V3 records are returned; V4 (range tracking extents, no name) are skipped by their length. Zeros
///     between records are skipped 8 bytes at a time; anything that is not a record skips to the next 4 KB page.
/// </summary>
public class Usn
{
    private const int PageSize = 0x1000;

    /// <summary>Records are never larger than this: the V3 fixed part plus a 255 character name, rounded up</summary>
    private const int MaxRecordSize = 0x260;

    private const int MinRecordSize = 0x38;

    /// <summary>Offset of the last record read (kept for compatibility; it describes the last file loaded)</summary>
    public static uint LastOffset;

    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[0x100000];
    private long _bufferStart;
    private int _bufferLength;

    public Usn(Stream fileStream, long startingOffset)
    {
        UsnEntries = new List<UsnEntry>();

        _stream = fileStream;
        _bufferStart = startingOffset;

        Log.Verbose("Beginning processing");

        var position = startingOffset;

        while (Ensure(position, 8))
        {
            var at = (int)(position - _bufferStart);
            var size = BitConverter.ToUInt32(_buffer, at);
            var majorVer = BitConverter.ToInt16(_buffer, at + 4); //used for error checking

            if (size == 0)
            {
                //records are 8 byte aligned; this used to skip to the next page and lose the records after the zeros
                position += 8;
                continue;
            }

            var valid = size >= MinRecordSize && size <= MaxRecordSize && size % 8 == 0 &&
                        majorVer is 2 or 3 or 4 && Ensure(position, (int)size);

            if (valid == false)
            {
                Log.Verbose(
                    "Strange size or ver # incorrect at 0x{Position:X8}. Skipping to the next page. Size: 0x{Size:X} version: {MajorVer}",
                    position, size, majorVer);
                position = (position / PageSize + 1) * PageSize;
                continue;
            }

            LastOffset = (uint)position;

            if (majorVer == 4)
            {
                //range tracking: extents of a change, no name
                position += size;
                continue;
            }

            Log.Verbose("Processing UsnEntry at 0x{Position:X}", position);

            var buff = new byte[size];
            Buffer.BlockCopy(_buffer, (int)(position - _bufferStart), buff, 0, (int)size);

            try
            {
                var ue = new UsnEntry(buff, position);
                if (ue.InvalidTimestamp)
                {
                    ParseErrors.Add($"Record at 0x{position:X} has an invalid timestamp");
                }

                UsnEntries.Add(ue);
            }
            catch (Exception e)
            {
                ParseErrors.Add($"Record at 0x{position:X} could not be parsed: {e.Message}");
            }

            position += size;
        }

        Log.Debug("Found {Count:N0} records", UsnEntries.Count);
    }

    /// <summary>Makes <paramref name="count" /> bytes at <paramref name="position" /> available in the buffer</summary>
    private bool Ensure(long position, int count)
    {
        if (position >= _bufferStart && position + count <= _bufferStart + _bufferLength)
        {
            return true;
        }

        _stream.Seek(position, SeekOrigin.Begin);
        _bufferStart = position;
        _bufferLength = 0;

        while (_bufferLength < _buffer.Length)
        {
            var n = _stream.Read(_buffer, _bufferLength, _buffer.Length - _bufferLength);
            if (n == 0)
            {
                break;
            }

            _bufferLength += n;
        }

        return count <= _bufferLength;
    }

    public List<UsnEntry> UsnEntries { get; }

    /// <summary>Records that could not be parsed or have invalid timestamps; the others are kept</summary>
    public List<string> ParseErrors { get; } = new();
}
