using System;
using System.Diagnostics;
using System.IO;
using NLog;

namespace Secure
{
    public class Sdh
    {
        public Sdh(Stream fileStream)
        {
//            var logger = LogManager.GetLogger("SDH");
//
//            var sig = 0x58444E49;
//
//         //   var index = 0x0;
//
//            var headerBytes = new byte[4];
//
//            fileStream.Read(headerBytes, 0, 4);
//
//
//            var sigActual = BitConverter.ToInt32(headerBytes, 0);
//
//            if (sig != sigActual)
//            {
//                {
//                    throw new Exception("Invalid header! Expected 'INDX' Signature.");
//                }
//            }
//
//            index += 4;
//
//            var fixupOffset = BitConverter.ToInt16(rawBytes, index);
//            index += 2;
//            var numFixupPairs = BitConverter.ToInt16(rawBytes, index);
//            index += 2;
//            var logFileSequenceNumber = BitConverter.ToInt64(rawBytes, index);
//            index += 8;
//            var virtualClusterNumber = BitConverter.ToInt64(rawBytes, index);
//            index += 8;
//
//            var dataStartPosition = index;
//
//            var indexValOffset = BitConverter.ToInt32(rawBytes, index);
//            index += 4;
//            var indexNodeSize = BitConverter.ToInt32(rawBytes, index);
//            index += 4;
//            var indexAllocatedSize = BitConverter.ToInt32(rawBytes, index);
//            index += 4;
//            var indexFlags = BitConverter.ToInt32(rawBytes, index);
//            index += 4;
//
//            var fixupTotalLength = numFixupPairs * 2;
//
//            var fixupBuffer = new byte[fixupTotalLength];
//            Buffer.BlockCopy(rawBytes, fixupOffset, fixupBuffer, 0, fixupTotalLength);
//
//
//            var fixupData = new FixupData(fixupBuffer);
//
//            var fixupOk = true;
//
//            //fixup verification
//            var counter = 512;
//            foreach (var bytese in fixupData.FixupActual)
//            {
//                //adjust the offset to where we need to check
//                var fixupOffset1 = counter - 2;
//
//                var expected = BitConverter.ToInt16(rawBytes, fixupOffset1);
//                if (expected != fixupData.FixupExpected)
//                {
//                    fixupOk = false;
//                    logger.Warn(
//                        $"Fixup values do not match at 0x{fixupOffset1:X}. Expected: 0x{fixupData.FixupExpected:X2}, actual: 0x{expected:X2}");
//                }
//
//                //replace fixup expected with actual bytes. bytese has actual replacement values in it.
//                Buffer.BlockCopy(bytese, 0, rawBytes, fixupOffset1, 2);
//
//                counter += 512;
//            }
//
//            index += fixupTotalLength;
//
//            while (index % 8 != 0)
//            {
//                index += 1;
//            }
//
//            //TODO finish?
//
//            //figure out how to use indexAllocated ot at least indexnode size. do we need both? indexallocated + 0x18 is start of next index record
//
//            while (index < dataStartPosition + indexAllocatedSize)
//            {
//                var startIndex = index;
//
//
//                var offsetToData = BitConverter.ToInt16(rawBytes, index);
//                index += 2;
//                var dataSize = BitConverter.ToInt16(rawBytes, index);
//                index += 2;
//
//                index += 4; //padding
//
//                var indexEntrySize = BitConverter.ToInt16(rawBytes, index);
//                index += 2;
//                var indexEntryKey = BitConverter.ToInt16(rawBytes, index);
//                index += 2;
//
//                var flags = BitConverter.ToInt16(rawBytes, index);
//                index += 2;
//
//                index += 2; //padding
//
//
//                var hash = BitConverter.ToInt32(rawBytes, index);
//                index += 4;
//
//                Debug.WriteLine(
//                    $"startIndex 0x {startIndex:X} OffsetToData: 0x {offsetToData:X} dataSize: 0x {dataSize:X} ");
//            }
        }
    }
}