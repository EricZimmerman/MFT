using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NLog;

namespace Usn
{
    public class Usn
    {
        private const int PageSize = 0x1000;
        private readonly Logger _logger = LogManager.GetLogger("Usn");

        public static uint LastOffset = 0;

        

        public Usn(Stream fileStream, long startingOffset)
        {
            UsnEntries = new List<UsnEntry>();

            fileStream.Seek(startingOffset, SeekOrigin.Begin);

            var lastGoodPageOffset = startingOffset;

            _logger.Trace("Beginning processing");

            while (fileStream.Position<fileStream.Length)
            {
                _logger.Trace($"Starting fileStream.Position 0x {fileStream.Position:X8}.");

                LastOffset = (uint) fileStream.Position;

                var calcBuff = new byte[8];
                fileStream.Read(calcBuff,0,8);
                fileStream.Seek(-8, SeekOrigin.Current); //reverse to where we were

                var size = BitConverter.ToUInt32(calcBuff, 0);
                var majorVer = BitConverter.ToInt16(calcBuff,  4); //used for error checking

                if (size == 0)
                {
                    _logger.Trace($"Size is zero. Increasing index by 0x{PageSize:X}");

                    fileStream.Seek((lastGoodPageOffset + PageSize), SeekOrigin.Begin);
                    
                    lastGoodPageOffset += PageSize;

                    continue;
                }

                if (size > PageSize)
                {
                    _logger.Trace($"Junk data found at 0x {(fileStream.Position):X8}. Increasing index by 0x{PageSize:X}");

                    lastGoodPageOffset += PageSize;

                    fileStream.Seek((lastGoodPageOffset), SeekOrigin.Begin);

                    continue;
                }

                if (size < 0x38 || size > 0x250 || majorVer != 2
                ) //~ minimum length, so jump to next page || max defined as max filename length (0xFF) + min length (it should not be bigger than this)
                {
                    _logger.Trace($"Strange size or ver # incorrect at 0x {(fileStream.Position):X8}. Increasing index by 0x{PageSize:X}. Size: 0x{size:X} version: {majorVer}");
                    
                    fileStream.Seek((lastGoodPageOffset + PageSize), SeekOrigin.Begin);

                    lastGoodPageOffset += PageSize;

                    continue;
                }

                if (fileStream.Position % PageSize == 0)
                {
                    _logger.Debug($"Setting lastGoodPageOffset to 0x {fileStream.Position:X8}");

                    lastGoodPageOffset = fileStream.Position;
                }

                _logger.Trace($"Processing UsnEntry at 0x{startingOffset + fileStream.Position:X}");

                var buff = new byte[size];

                fileStream.Read(buff, 0, (int) size);

                var ue = new UsnEntry(buff, LastOffset);
                UsnEntries.Add(ue);
            }

            _logger.Debug($"Found {UsnEntries.Count:N0} records");
        }

        public List<UsnEntry> UsnEntries { get; }
    }
}