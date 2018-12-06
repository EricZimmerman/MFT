using System;
using System.Collections.Generic;
using NLog;

namespace LogFile
{
    public class LogFile
    {
        private const int PageSize = 0x1000;

        public static uint LastOffset;
        private readonly Logger _logger = LogManager.GetLogger("LogFile");

        private const int RstrSig = 0x52545352;
        private  const int RcrdSig = 0x44524352;
        private const int ChkdSig = 0x52545351;

        public LogFile(byte[] rawBytes)
        {
            //preliminary sig check to get us started
            const int sig = 0x52545352;

            var index = 0x0;
            var sigCheck = BitConverter.ToInt32(rawBytes, index);

            if (sig != sigCheck)
            {
                {
                    throw new Exception("Invalid header! Expected 'RSTR' Signature.");
                }
            }

            NormalPageArea = new List<LogPageRcrd>();

            while (index < rawBytes.Length)
            {
                LastOffset = (uint) index;

                var buff = new byte[PageSize];
                Buffer.BlockCopy(rawBytes, index, buff, 0, PageSize);

                _logger.Debug($"Processing log page at offset 0x{index:X}");


                var sigActual = BitConverter.ToInt32(rawBytes, index);

                switch (sigActual)
                {
                    case RstrSig:
                        var lprstr = new LogPageRstr(buff, index);

                        if (index == 0)
                        {
                            PrimaryRstrPage = lprstr;
                        }
                        else
                        {
                            SecondaryRstrPage = lprstr;
                        }
                        
                        break;
                    case RcrdSig:
                        var lprcrd = new LogPageRcrd(buff, index);
                        if (index == 0x2000)
                        {
                            BufferPrimary = lprcrd;
                        }
                        else if (index == 0x3000)
                        {
                            BufferSecondary = lprcrd;
                        }
                        else
                        {
                            NormalPageArea.Add(lprcrd);    
                        }
                        
                        break;
//                case chkd_sig: //havent seen one of these to test
//                    PageType = PageTypes.Chkd;
//                    break;
                    default: 
                        throw new Exception($"Invalid signature at offset 0x{index:X}! Expected 'RCRD|RSTR|SHKD' signature.");
                }


                index += PageSize;
            }
        }

        public List<LogPageRcrd> NormalPageArea { get; }
        public LogPageRstr PrimaryRstrPage { get; }
        public LogPageRstr SecondaryRstrPage { get; }

        public LogPageRcrd BufferPrimary { get; }
        public LogPageRcrd BufferSecondary { get; }
    }
}