using System;
using System.Diagnostics;
using System.IO;

namespace Secure;

public class Sii
{
    public Sii(Stream fileStream)
    {
        //TODO finish? see Sdh for framework

        var sig = 0x58444E49;

        //   var index = 0x0;

        var headerBytes = new byte[4];

        fileStream.Read(headerBytes, 0, 4);

        var sigActual = BitConverter.ToInt32(headerBytes, 0);

        if (sig != sigActual)
        {
            throw new Exception("Invalid header! Expected 'INDX' Signature.");
        }

        //  index += 4;

//            4 4 Security descriptor hash
//            8 4 Security descriptor identifier
//
//            12 8 Security descriptor data offset (in $SDS)
//
//            20  4 Security descriptor data size (in $SDS)

        var hashBuffer = new byte[4];
        var idBuffer = new byte[4];
        var offsetBuffer = new byte[8];
        var sizeBuffer = new byte[4];

        while (fileStream.Position < fileStream.Length)
        {
            var startIndex = fileStream.Position;

            fileStream.Read(hashBuffer, 0, 4);

            var hash = BitConverter.ToInt32(hashBuffer, 0);
            //   index += 4;

            fileStream.Read(idBuffer, 0, 4);

            var id = BitConverter.ToInt32(idBuffer, 0);
            //    index += 4;

            fileStream.Read(offsetBuffer, 0, 8);

            var offset = BitConverter.ToInt64(offsetBuffer, 0);
            //     index += 8;

            fileStream.Read(sizeBuffer, 0, 4);

            var size = BitConverter.ToInt32(sizeBuffer, 0);
            //  index += 4;

            Debug.WriteLine($"Hash: {hash} offset: 0x {offset:X} size 0x '{size:X}' startIndex 0x {startIndex}");


            //     index += 4;
        }
    }
}