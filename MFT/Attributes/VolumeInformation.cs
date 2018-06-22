using System;
using System.Text;

namespace MFT.Attributes
{
    public class VolumeInformation : Attribute
    {
        [Flags]
        public enum VolumeFlag
        {
            None = 0x0000,
            IsDirty = 0x0001,
            ResizeJournalLogFile = 0x0002,
            UpgradeOnNextMount = 0x0004,
            MountedOnNt4 = 0x0008,
            DeleteUsnUnderway = 0x0010,
            RepairObjectIDs = 0x0020,
            ModifiedByChkDsk = 0x8000
        }

        public VolumeInformation(byte[] rawBytes) : base(rawBytes)
        {
            var residentData = new ResidentData(rawBytes);

            //our data is in residentData.Data
            UnknownBytes = new byte[8];
            Buffer.BlockCopy(residentData.Data, ContentOffset, UnknownBytes, 0, 8);

            MajorVersion = residentData.Data[ContentOffset + 0x8];
            MinorVersion = residentData.Data[ContentOffset + 0x9];

            VolumeFlags = (VolumeFlag) BitConverter.ToInt16(residentData.Data, ContentOffset + 0xA);
        }

        public byte[] UnknownBytes { get; }
        public int MajorVersion { get; }
        public int MinorVersion { get; }
        public VolumeFlag VolumeFlags { get; }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendLine("**** VOLUME INFORMATION ****");

            sb.AppendLine(base.ToString());

            sb.AppendLine();

            sb.AppendLine(
                $"Volume Flags: {VolumeFlags} Major Version: 0x{MajorVersion:X} Minor Version: 0x{MinorVersion:X} Unknown Bytes: {BitConverter.ToString(UnknownBytes)} ");

            return sb.ToString();
        }
    }
}