using System;

namespace Usn;

public class MftInformation
{
    public MftInformation(byte[] rawBytes)
    {
        if (rawBytes.Length != 8)
        {
            throw new ArgumentException("rawBytes must be 8 bytes long!");
        }

        var sequenceNumber = BitConverter.ToUInt16(rawBytes, 6);

        ulong entryIndex = 0;

        ulong entryIndex1 = BitConverter.ToUInt32(rawBytes, 0);
        ulong entryIndex2 = BitConverter.ToUInt16(rawBytes, 4);

        if (entryIndex2 == 0)
        {
            entryIndex = entryIndex1;
        }
        else
        {
            //the entry number is 48 bits: the high 16 bits start at bit 32 (this used to multiply by 2^24)
            entryIndex = entryIndex1 | (entryIndex2 << 32);
        }

        EntryNumber = entryIndex;
        SequenceNumber = sequenceNumber;
    }

    public ulong EntryNumber { get; set; }

    public uint SequenceNumber { get; set; }

    public override string ToString()
    {
        return $"Entry 0x{EntryNumber:X}, Seq 0x{SequenceNumber:X}";
    }
}