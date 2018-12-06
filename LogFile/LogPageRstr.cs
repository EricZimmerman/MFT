using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using NLog;

namespace LogFile
{
  public  class LogPageRstr
    {
        private readonly Logger _logger = LogManager.GetLogger("LogFile");
        private const int RstrSig = 0x52545352;
        private const int ChkdSig = 0x52545351;

        public int Offset { get; }

        public LogPageRstr(byte[] rawBytes, int offset)
        {
            var index = 0x0;

            var sigCheck = BitConverter.ToInt32(rawBytes, index);

            if (sigCheck != RstrSig || sigCheck != ChkdSig)
            {
                {
                    throw new Exception("Invalid signature! Expected 'RSTR|CHKD' signature.");
                }
            }

            Offset = offset;

            index += 4;

            var fixupOffset = BitConverter.ToInt16(rawBytes, index);
            index += 2;
            var numFixupPairs = BitConverter.ToInt16(rawBytes, index);
            index += 2;

            var checkDiskLsn = BitConverter.ToInt64(rawBytes, index);
            index += 8;
            var systemPageSize = BitConverter.ToInt32(rawBytes, index);
            index += 4;
            
            var logPageSize = BitConverter.ToInt32(rawBytes, index);
            index += 4;

            var restartOffset = BitConverter.ToInt16(rawBytes, index);
            index += 2;

            var minorFormatVersion = BitConverter.ToInt16(rawBytes, index);
            index += 2;

            var majorFormatVersion = BitConverter.ToInt16(rawBytes, index);
            index += 2;
            

            var fixupTotalLength = numFixupPairs * 2;

            var fixupBuffer = new byte[fixupTotalLength];
            Buffer.BlockCopy(rawBytes, fixupOffset, fixupBuffer, 0, fixupTotalLength);

            var fixupData = new FixupData(fixupBuffer);

            var fixupOk = true;

            //fixup verification
            var counter = 512;
            foreach (var bytese in fixupData.FixupActual)
            {
                //adjust the offset to where we need to check
                var fixupOffset1 = counter - 2;

                var expected = BitConverter.ToInt16(rawBytes, fixupOffset1);
                if (expected != fixupData.FixupExpected)
                {
                    fixupOk = false;
                    _logger.Warn($"Fixup values do not match at 0x{fixupOffset1:X}. Expected: 0x{fixupData.FixupExpected:X2}, actual: 0x{expected:X2}");
                }

                //replace fixup expected with actual bytes. bytese has actual replacement values in it.
                Buffer.BlockCopy(bytese, 0, rawBytes, fixupOffset1, 2);

                counter += 512;
            }

            index += fixupTotalLength;

            while (index % 8 != 0)
            {
                index += 1;
            }

            var currentLsn = BitConverter.ToInt64(rawBytes, index);
            index += 8;
            var logClient = BitConverter.ToInt16(rawBytes, index);
            index += 2;
            var clientList = BitConverter.ToInt16(rawBytes, index);
            index += 2;
            var flags = BitConverter.ToInt32(rawBytes, index);
            index += 4;
            

            Debug.WriteLine($"at abs offset: 0x{(offset+index):X}, restartOffset: 0x{restartOffset:X}");
        }
    }
}
