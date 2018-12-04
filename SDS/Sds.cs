using System;
using System.Collections.Generic;
using System.Diagnostics;
using MFT.Attributes;
using NLog;

namespace Secure
{
    public class Sds
    {
        public static uint LastOffset = 0;

        public Sds(byte[] rawBytes)
        {
            uint index = 0x0;

            var logger = LogManager.GetLogger("SDS");

            SdsEntries = new List<SdsEntry>();

            while (index < rawBytes.Length)
            {
                LastOffset = index;

                var hash = BitConverter.ToUInt32(rawBytes, (int) index);
                var id = BitConverter.ToUInt32(rawBytes,(int) index + 4);
                var offset = BitConverter.ToUInt64(rawBytes, (int) index + 4 + 4);
                var size = BitConverter.ToUInt32(rawBytes,(int)  index + 4 + 4 + 8);

                if (offset == 0 && size == 0)
                {
                    //end of page, so get to start of next section
                    while (index % 0x40000 != 0)
                    {
                        index += 1;
                    }

                    continue;
                }

                if (id > 0 && size<0x2000)
                {
                    logger.Debug(
                        $"Starting index: 0x{LastOffset:X} Offset 0x{offset:X} Hash: 0x{hash:X} id: {id}  size 0x{size:X}");

                    var dataSize = size - 0x14;

                    if (dataSize > rawBytes.Length - (int)LastOffset)
                    {
                        break;
                    }

                    var buff = new byte[dataSize];
                    Buffer.BlockCopy(rawBytes, (int) (offset + 0x14), buff, 0, (int) dataSize);

                    var sk = new SkSecurityDescriptor(buff);
                    logger.Trace(sk);

                    var sde = new SdsEntry(hash, id, offset, size, sk, LastOffset);

                    SdsEntries.Add(sde);

                }

                index += size;

                //padding calculation
                while (index % 16 != 0)
                {
                    index += 1;
                }
            }
        }

        public List<SdsEntry> SdsEntries { get; }
    }
}