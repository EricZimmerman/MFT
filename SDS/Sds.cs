using System;
using System.Diagnostics;
using NLog;
using Secure;

namespace SDS
{
    public class Sds
    {
        public Sds(byte[] rawBytes)
        {
            var index = 0x0;

            var logger = LogManager.GetLogger("SDS");

            while (index <= rawBytes.Length)
            {
                var startingOffset = index;

                if (startingOffset == 0x20B4A0)
                {
                    Debug.WriteLine(1);
                }

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
                        $"Offset 0x {startingOffset:X} Hash: 0x {hash:X} id: {id} offset: 0x {offset:X} size 0x '{size:X}'");

                    var dataSize = size - 0x14;
                    var buff = new byte[dataSize];
                    Buffer.BlockCopy(rawBytes, startingOffset + 0x14, buff, 0, dataSize);

                    var sk = new SecurityDescriptor(buff);

                    //  Debug.WriteLine($"Offset 0x {startingOffset:X} {sk}");
                }


                index += size;

                //padding calculation
                while (index % 16 != 0)
                {
                    index += 1;
                }
            }
        }
    }
}