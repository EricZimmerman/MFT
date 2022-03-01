using System;
using System.Text;

namespace MFT.Other;

public class AttributeInfo
{
    public AttributeInfo(byte[] rawBytes)
    {
        var buff = new byte[8];

        FirstVirtualClusterNumber = BitConverter.ToUInt64(rawBytes, 0x8);

        Buffer.BlockCopy(rawBytes, 0x10, buff, 0, 0x8);

        EntryInfo = new MftEntryInfo(buff);

        var nameSize = rawBytes[0x6];
        var nameOffset = rawBytes[0x7];

        if (nameSize > 0)
        {
            Name = Encoding.Unicode.GetString(rawBytes, nameOffset, nameSize * 2);
        }
    }

    public ulong FirstVirtualClusterNumber { get; }

    public MftEntryInfo EntryInfo { get; }
    public string Name { get; }

    public override string ToString()
    {
        var name = string.Empty;
        if (Name != null)
        {
            name = $" Name: {Name}";
        }

        return $"Entry info: {EntryInfo}{name} First Vcn: 0x{FirstVirtualClusterNumber:X}";
    }
}