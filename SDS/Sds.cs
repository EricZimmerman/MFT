using System;
using System.Collections.Generic;
using System.IO;
using MFT.Attributes;
using Serilog;

namespace Secure;

/// <summary>
///     $Secure:$SDS. The stream is 256 KB blocks, each followed by a 256 KB mirror of it. Each entry is: hash (4),
///     id (4), offset of the entry in the stream (8), size including this 0x14 byte header (4), then a self-relative
///     security descriptor; entries are 16 byte aligned and don't cross a block. Unused space in a block is zero.
/// </summary>
public class Sds
{
    private const int BlockSize = 0x40000;
    private const int HeaderSize = 0x14;

    /// <summary>File offset of the last entry read (kept for compatibility; it describes the last file loaded)</summary>
    public static uint LastOffset;

    public Sds(Stream fileStream)
    {
        SdsEntries = new List<SdsEntry>();

        var rawBytes = new byte[fileStream.Length];
        var read = 0;
        while (read < rawBytes.Length)
        {
            var n = fileStream.Read(rawBytes, read, rawBytes.Length - read);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        //data blocks are at 0, 0x80000, 0x100000, ... and each is mirrored in the 256 KB after it
        for (long block = 0; block < read; block += 2L * BlockSize)
        {
            var end = (int)Math.Min(block + BlockSize, read);
            var index = (int)block;

            while (end - index >= HeaderSize)
            {
                var hash = BitConverter.ToUInt32(rawBytes, index);
                var id = BitConverter.ToUInt32(rawBytes, index + 4);
                var offset = BitConverter.ToUInt64(rawBytes, index + 4 + 4);
                var size = BitConverter.ToUInt32(rawBytes, index + 4 + 4 + 8);

                if (hash == 0 && id == 0 && offset == 0 && size == 0)
                {
                    //the rest of this block is unused
                    break;
                }

                if (size < HeaderSize || size > end - index || id > 0 && offset != (ulong)index)
                {
                    //not an entry: report it, then look for the next one (its offset field is its own position)
                    ParseErrors.Add(
                        $"Bad entry at 0x{index:X} (id 0x{id:X}, offset 0x{offset:X}, size 0x{size:X}); looking for the next entry");
                    index = Resync(rawBytes, index + 16, end);
                    continue;
                }

                if (id > 0)
                {
                    AddEntry(rawBytes, index, hash, id, offset, size, false);

                    //the mirror copy normally matches; if it doesn't, keep it too
                    var mirror = index + BlockSize;
                    if (mirror + size <= read && SameBytes(rawBytes, index, mirror, (int)size) == false)
                    {
                        var mirrorSize = BitConverter.ToUInt32(rawBytes, mirror + 16);
                        if (mirrorSize >= HeaderSize && mirror + mirrorSize <= read)
                        {
                            Log.Debug("Mirror of SDS entry 0x{Id:X} at 0x{Mirror:X} differs", id, mirror);
                            AddEntry(rawBytes, mirror, BitConverter.ToUInt32(rawBytes, mirror),
                                BitConverter.ToUInt32(rawBytes, mirror + 4), BitConverter.ToUInt64(rawBytes, mirror + 8),
                                mirrorSize, true);
                        }
                    }
                }

                //id 0 with a size: a freed entry, skip it
                index += (int)((size + 15) & ~15u);
            }
        }
    }

    private void AddEntry(byte[] rawBytes, int index, uint hash, uint id, ulong offset, uint size, bool fromMirror)
    {
        LastOffset = (uint)index;

        Log.Debug(
            "Starting index: 0x{LastOffset:X} Offset 0x{Offset:X} Hash: 0x{Hash:X} id: {Id}  size 0x{Size:X}",
            index, offset, hash, id, size);

        var buff = new byte[size - HeaderSize];
        Buffer.BlockCopy(rawBytes, index + HeaderSize, buff, 0, buff.Length);

        try
        {
            var sk = new SkSecurityDescriptor(buff);
            Log.Verbose("{Sk}", sk);

            SdsEntries.Add(new SdsEntry(hash, id, offset, size, sk, (ulong)index) { FromMirror = fromMirror });
        }
        catch (Exception e)
        {
            ParseErrors.Add($"Security descriptor of entry 0x{id:X} at 0x{index:X} could not be parsed: {e.Message}");
        }
    }

    private static int Resync(byte[] rawBytes, int index, int end)
    {
        for (; end - index >= HeaderSize; index += 16)
        {
            var id = BitConverter.ToUInt32(rawBytes, index + 4);
            var offset = BitConverter.ToUInt64(rawBytes, index + 8);
            var size = BitConverter.ToUInt32(rawBytes, index + 16);

            if (id > 0 && offset == (ulong)index && size >= HeaderSize && size <= end - index)
            {
                return index;
            }
        }

        return end;
    }

    private static bool SameBytes(byte[] bytes, int a, int b, int length)
    {
        for (var i = 0; i < length; i++)
        {
            if (bytes[a + i] != bytes[b + i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>One per security id, from the data blocks, plus mirror copies that differ (FromMirror)</summary>
    public List<SdsEntry> SdsEntries { get; }

    /// <summary>Entries that could not be read; the others are kept</summary>
    public List<string> ParseErrors { get; } = new();
}
