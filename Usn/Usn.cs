using System;
using System.Collections.Generic;
using NLog;

namespace Usn
{
    public class Usn
    {
        private readonly Logger _logger = LogManager.GetLogger("Usn");

        public Usn(byte[] rawBytes)
        {
            var index = 0;

            UsnEntries = new List<UsnEntry>();

            while (index < rawBytes.Length)
            {
                var size = BitConverter.ToInt32(rawBytes, index);

                if (size == 0)
                {
                    break;
                }

                var buff = new byte[size];
                Buffer.BlockCopy(rawBytes, index, buff, 0, size);

                var ue = new UsnEntry(buff);
                UsnEntries.Add(ue);

                index += size;
            }
        }

        public List<UsnEntry> UsnEntries { get; }
    }
}