using System;
using System.Collections.Generic;

namespace MFT.Other
{
    public class FixupData
    {
        public FixupData(byte[] fixupDataRaw)
        {
            FixupExpected = BitConverter.ToInt16(fixupDataRaw, 0);
            FixupActual = new List<byte[]>();

            var index = 2;

            while (index < fixupDataRaw.Length)
            {
                var b = new byte[2];
                Buffer.BlockCopy(fixupDataRaw, index, b, 0, 2);
                FixupActual.Add(b);
                index += 2;
            }
        }

        /// <summary>
        ///     the data expected at the end of each 512 byte chunk
        /// </summary>
        public short FixupExpected { get; }

        /// <summary>
        ///     The actual bytes to be overlayed before processing a record, in order
        /// </summary>
        public List<byte[]> FixupActual { get; }
    }
}