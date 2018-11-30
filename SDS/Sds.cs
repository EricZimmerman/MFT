using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SDS
{
    public class Sds
    {
        public Sds(byte[] rawBytes)
        {
            var index = 0x2;

            while (index<=rawBytes.Length)
            {
                var hash = BitConverter.ToInt32(rawBytes,index);
                index += 4;
                var id = BitConverter.ToInt32(rawBytes,index);
                index += 4;
                var offset = BitConverter.ToInt64(rawBytes, index);
                index += 8;
                var size = BitConverter.ToInt32(rawBytes,index);

                Debug.WriteLine($"Hash: {hash} id: {id} offset: {offset} size 0x '{size:X}' at 0x {index}");

                index += 4;

                


                index += size;

            }
        }

    }
}
