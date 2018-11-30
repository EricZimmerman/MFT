using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Secure
{
   public class Sii
    {
        public Sii(byte[] rawBytes)
        {
            var sig = 0x58444E49;

            var index = 0x0;
            var sigActual = BitConverter.ToInt32(rawBytes, index);

            if (sig != sigActual)
            {
                {
                    throw new Exception("Invalid header! Expected 'INDX' Signature.");
                }

            }

            index += 4;

//            4 4 Security descriptor hash
//            8 4 Security descriptor identifier
//
//            12 8 Security descriptor data offset (in $SDS)
//
//            20  4 Security descriptor data size (in $SDS)

            while (index<rawBytes.Length)
            {
                var startIndex = index;

                var hash = BitConverter.ToInt32(rawBytes,index);
                index += 4;
                var id = BitConverter.ToInt32(rawBytes,index);
                index += 4;
                var offset = BitConverter.ToInt64(rawBytes, index);
                index += 8;
                var size = BitConverter.ToInt32(rawBytes,index);

                
                index += 4;

                Debug.WriteLine($"Hash: {hash} offset: 0x {offset:X} size 0x '{size:X}' startIndex 0x {startIndex}");


           //     index += 4;

            }
        }
    }
}
