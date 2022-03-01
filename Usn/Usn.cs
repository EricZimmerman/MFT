using System;
using System.Collections.Generic;
using System.IO;
using Serilog;

namespace Usn;

public class Usn
{
    private const int PageSize = 0x1000;

    public static uint LastOffset;

    public Usn(Stream fileStream, long startingOffset)
    {
        UsnEntries = new List<UsnEntry>();

        fileStream.Seek(startingOffset, SeekOrigin.Begin);

        var lastGoodPageOffset = startingOffset;

        Log.Verbose("Beginning processing");

        while (fileStream.Position < fileStream.Length)
        {
            Log.Verbose("Starting fileStream.Position 0x{Position:X8}", fileStream.Position);

            LastOffset = (uint)fileStream.Position;

            var calcBuff = new byte[8];
            fileStream.Read(calcBuff, 0, 8);
            fileStream.Seek(-8, SeekOrigin.Current); //reverse to where we were

            var size = BitConverter.ToUInt32(calcBuff, 0);
            var majorVer = BitConverter.ToInt16(calcBuff, 4); //used for error checking

            if (size == 0)
            {
                Log.Verbose("Size is zero. Increasing index by 0x{PageSize:X}", PageSize);

                fileStream.Seek(lastGoodPageOffset + PageSize, SeekOrigin.Begin);

                lastGoodPageOffset += PageSize;

                continue;
            }

            if (size > PageSize)
            {
                Log.Verbose("Junk data found at 0x{Position:X8}. Increasing index by 0x{PageSize:X}",
                    fileStream.Position, PageSize);

                lastGoodPageOffset += PageSize;

                fileStream.Seek(lastGoodPageOffset, SeekOrigin.Begin);

                continue;
            }

            if (size < 0x38 || size > 0x250 || majorVer != 2
               ) //~ minimum length, so jump to next page || max defined as max filename length (0xFF) + min length (it should not be bigger than this)
            {
                Log.Verbose(
                    "Strange size or ver # incorrect at 0x{Position:X8}. Increasing index by 0x{PageSize:X}. Size: 0x{Size:X} version: {MajorVer}",
                    fileStream.Position, PageSize, size, majorVer);

                fileStream.Seek(lastGoodPageOffset + PageSize, SeekOrigin.Begin);

                lastGoodPageOffset += PageSize;

                continue;
            }

            if (fileStream.Position % PageSize == 0)
            {
                Log.Debug("Setting lastGoodPageOffset to 0x{Position:X8}", fileStream.Position);

                lastGoodPageOffset = fileStream.Position;
            }

            Log.Verbose("Processing UsnEntry at 0x{Position:X}", startingOffset + fileStream.Position);

            var buff = new byte[size];

            fileStream.Read(buff, 0, (int)size);

            var ue = new UsnEntry(buff, LastOffset);
            UsnEntries.Add(ue);
        }

        Log.Debug("Found {Count:N0} records", UsnEntries.Count);
    }

    public List<UsnEntry> UsnEntries { get; }
}