using System;
using System.Collections.Generic;
using System.IO;
using MFT.Attributes;
using Serilog;

namespace Secure;

public class Sds
{
    public static uint LastOffset;

    public Sds(Stream fileStream)
    {
        uint index = 0x0;

        SdsEntries = new List<SdsEntry>();


        var rawBytes = new byte[fileStream.Length];
        fileStream.Read(rawBytes, 0, (int) fileStream.Length);

        while (index < rawBytes.Length)
        {
            if (index + 16 > rawBytes.Length)
                //out of data
                break;

            var hash = BitConverter.ToUInt32(rawBytes, (int) index);
            var id = BitConverter.ToUInt32(rawBytes, (int) index + 4);
            var offset = BitConverter.ToUInt64(rawBytes, (int) index + 4 + 4);
            var size = BitConverter.ToUInt32(rawBytes, (int) index + 4 + 4 + 8);

            if (index == LastOffset && hash == 0x0 && id == 0x0 && offset == 0x0 && size == 0x0)
            {
                //nothing here, go to next page
                index += 0x40000;
                continue;
            }

            LastOffset = index;

            Log.Debug("LastOffset is 0x{LastOffset}", LastOffset);

            if (offset == 0 && size == 0 || offset > (ulong) rawBytes.Length)
            {
                //end of page, so get to start of next section
                while (index % 0x40000 != 0) index += 1;

                continue;
            }

            if (id > 0 && offset < (ulong) rawBytes.Length) //size < 0x2000
            {
                Log.Debug(
                    "Starting index: 0x{LastOffset:X} Offset 0x{Offset:X} Hash: 0x{Hash:X} id: {Id}  size 0x{Size:X}",
                    LastOffset, offset, hash, id, size);

                var dataSize = size - 0x14;

                if (dataSize > rawBytes.Length - (int) LastOffset) break;

                var buff = new byte[dataSize];
                Buffer.BlockCopy(rawBytes, (int) (offset + 0x14), buff, 0, (int) dataSize);

                var sk = new SkSecurityDescriptor(buff);
                Log.Verbose("{Sk}", sk);

                var sde = new SdsEntry(hash, id, offset, size, sk, LastOffset);

                SdsEntries.Add(sde);
            }

            if (size == 0) size = 16;

            index += size;

            //padding calculation
            while (index % 16 != 0) index += 1;
        }
    }

    public List<SdsEntry> SdsEntries { get; }
}