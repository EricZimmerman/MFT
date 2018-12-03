using System;
using System.Collections.Generic;
using NLog;

namespace Usn
{
    public class Usn
    {
        private const int PageSize = 0x1000;
        private readonly Logger _logger = LogManager.GetLogger("Usn");

        public Usn(byte[] rawBytes, long startingOffset)
        {
            uint index = 0;

            UsnEntries = new List<UsnEntry>();

            var lastGoodPageOffset = startingOffset;

            while (index < rawBytes.Length)
            {
                var size = BitConverter.ToUInt32(rawBytes, (int) index);
                var majorVer = BitConverter.ToInt16(rawBytes, (int) index + 4); //used for error checking

                if (size == 0)
                {
                    index = (uint) (lastGoodPageOffset + PageSize);
                    lastGoodPageOffset += PageSize;
                    continue;
                }

                if (size > PageSize)
                {
                    _logger.Trace($"Junk data found at 0x {index:X8}. Increasing index by 0x{PageSize:X}");

                    index += PageSize;
                    lastGoodPageOffset += PageSize;

                    continue;
                }

                if (size < 0x38 || size > 0x137 || majorVer != 2
                ) //~ minimum length, so jump to next page || max defined as max filename length (0xFF) + min length (it should not be bigger than this)
                {
                    _logger.Trace(
                        $"Strange size or ver # incorrect at 0x {index:X8}. Increasing index by 0x{PageSize:X}");

                    index = (uint) (lastGoodPageOffset + PageSize);
                    lastGoodPageOffset += PageSize;

                    continue;
                }

                if (index % PageSize == 0)
                {
                    _logger.Debug($"Setting lastGoodPageOffset to 0x {index:X8}");

                    lastGoodPageOffset = index;
                }

                _logger.Trace($"Processing UsnEntry at 0x{startingOffset + index:X}");
                var buff = new byte[size];
                Buffer.BlockCopy(rawBytes, (int) index, buff, 0, (int) size);

                var ue = new UsnEntry(buff, startingOffset + index);
                UsnEntries.Add(ue);

                index += size;
            }

            _logger.Debug($"Found {UsnEntries.Count:N0} records");
        }

        public List<UsnEntry> UsnEntries { get; }
    }
}