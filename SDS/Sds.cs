using System;
using System.Collections.Generic;
using MFT.Attributes;
using NLog;

namespace Secure
{
    public class Sds
    {
        public Sds(byte[] rawBytes)
        {
            var index = 0x0;

            var logger = LogManager.GetLogger("SDS");

            SdsEntries = new List<SdsEntry>();

            while (index <= rawBytes.Length)
            {
                var startingIndex = index;
                var hash = BitConverter.ToUInt32(rawBytes, index);
                var id = BitConverter.ToInt32(rawBytes, index + 4);
                var offset = BitConverter.ToInt64(rawBytes, index + 4 + 4);
                var size = BitConverter.ToInt32(rawBytes, index + 4 + 4 + 8);

                if (offset == 0 && size == 0)
                {
                    //end of page, so get to start of next section
                    while (index % 0x40000 != 0)
                    {
                        index += 1;
                    }

                    continue;
                }

                if (id > 0)
                {
                    logger.Debug(
                        $"Offset 0x{offset:X} Hash: 0x{hash:X} id: {id}  size 0x{size:X}");

                    var dataSize = size - 0x14;
                    var buff = new byte[dataSize];
                    Buffer.BlockCopy(rawBytes, (int) (offset + 0x14), buff, 0, dataSize);

                    var sk = new SKSecurityDescriptor(buff);
                    logger.Trace(sk);

                    var sde = new SdsEntry(hash, id, offset, size, sk, startingIndex);

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