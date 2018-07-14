using System;
using System.Text;

namespace MFT.Attributes
{
    public enum AttributeType
    {
        EndOfAttributes = -0x1,
        Unused = 0x0,
        StandardInformation = 0x10,
        AttributeList = 0x20,
        FileName = 0x30,
        VolumeVersionObjectId = 0x40,
        SecurityDescriptor = 0x50,
        VolumeName = 0x60,
        VolumeInformation = 0x70,
        Data = 0x80,
        IndexRoot = 0x90,
        IndexAllocation = 0xa0,
        Bitmap = 0xb0,
        ReparsePoint = 0xc0,
        EaInformation = 0xd0,
        Ea = 0xe0,
        PropertySet = 0xf0,
        LoggedUtilityStream = 0x100,
        UserDefinedAttribute = 0x1000
    }

    [Flags]
    public enum AttributeDataFlag
    {
        Compressed = 0x0001,
        Encrypted = 0x4000,
        Sparse = 0x8000
    }

    public abstract class Attribute
    {
        protected Attribute(byte[] rawBytes)
        {
            AttributeNumber = BitConverter.ToInt16(rawBytes, 0xE);

            AttributeType = (AttributeType) BitConverter.ToInt32(rawBytes, 0);
            AttributeSize = BitConverter.ToInt32(rawBytes, 4);

            IsResident = rawBytes[0x8] == 0;

            NameSize = rawBytes[0x09];
            NameOffset = BitConverter.ToInt16(rawBytes, 0xA);

            AttributeDataFlag = (AttributeDataFlag) BitConverter.ToInt16(rawBytes, 0xC);

            AttributeContentLength = BitConverter.ToInt32(rawBytes, 0x10);
            ContentOffset = BitConverter.ToInt16(rawBytes, 0x14);

            Name = string.Empty;
            if (NameSize > 0)
            {
                Name = Encoding.Unicode.GetString(rawBytes, NameOffset, NameSize * 2);
            }
        }

        public AttributeType AttributeType { get; }
        public int AttributeSize { get; }
        public int AttributeContentLength { get; }
        public int NameSize { get; }
        public int NameOffset { get; }

        public AttributeDataFlag AttributeDataFlag { get; }

        public string Name { get; }
        public int AttributeNumber { get; }

        public bool IsResident { get; }

        public short ContentOffset { get; }

        public override string ToString()
        {
            var name = string.Empty;

            if (NameSize > 0)
            {
                name = $", Name: {Name}";
            }

            var flags = string.Empty;

            if (AttributeDataFlag > 0)
            {
                flags = $" Attribute flags: {AttributeDataFlag.ToString().Replace(", ", "|")},";
            }

            return
                $"Type: {AttributeType}, Attribute #: 0x{AttributeNumber:X},{flags} Size: 0x{AttributeSize:X}, Content size: 0x{AttributeContentLength:X}, Name size: 0x{NameSize:X}{name}, Content offset: 0x{ContentOffset:X}, Resident: {IsResident}";
        }
    }
}