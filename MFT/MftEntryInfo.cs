using System;

namespace MFT
{
    public class MftEntryInfo
    {
        public MftEntryInfo(byte[] rawEntryBytes)
        {
            if (rawEntryBytes.Length != 8)
            {
                throw new ArgumentException("rawEntryBytes must be 8 bytes long!");
            }

            var sequenceNumber = BitConverter.ToUInt16(rawEntryBytes, 6);

            ulong entryIndex;

            ulong entryIndex1 = BitConverter.ToUInt32(rawEntryBytes, 0);
            ulong entryIndex2 = BitConverter.ToUInt16(rawEntryBytes, 4);

            if (entryIndex2 == 0)
            {
                entryIndex = entryIndex1;
            }
            else
            {
                entryIndex2 = entryIndex2 * 16777216; //2^24
                entryIndex = entryIndex1 + entryIndex2;
            }

            MftEntryNumber = entryIndex;
            MftSequenceNumber = sequenceNumber;

            if (sequenceNumber == 0)
            {
                MftSequenceNumber = null;
            }
        }

        public ulong? MftEntryNumber { get; set; }

        public int? MftSequenceNumber { get; set; }

        public override string ToString()
        {
            return $"Entry: 0x{MftEntryNumber:X}, Seq: 0x{MftSequenceNumber:X}";
        }
    }
}