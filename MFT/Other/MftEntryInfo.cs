using System;

namespace MFT.Other;

public class MftEntryInfo
{
    public MftEntryInfo(byte[] rawEntryBytes)
    {
        if (rawEntryBytes.Length != 8)
        {
            throw new ArgumentException("rawEntryBytes must be 8 bytes long!");
        }

        var sequenceNumber = BitConverter.ToUInt16(rawEntryBytes, 6);

        //a 48 bit entry number: the high 16 bits start at bit 32 (this used to multiply them by 2^24, in a uint)
        var low = BitConverter.ToUInt32(rawEntryBytes, 0);
        ulong high = BitConverter.ToUInt16(rawEntryBytes, 4);

        MftEntryNumber = low | (high << 32);
        MftSequenceNumber = sequenceNumber;
    }

    /// <summary>The 48 bit entry number (NTFS uses the low 32 bits)</summary>
    public ulong MftEntryNumber { get; set; }

    public ushort MftSequenceNumber { get; set; }

    public override string ToString()
    {
        return $"Entry/seq: 0x{MftEntryNumber:X}-0x{MftSequenceNumber:X}";
    }
}