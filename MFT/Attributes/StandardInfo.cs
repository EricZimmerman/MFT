using System;
using System.Text;

namespace MFT.Attributes
{
    public class StandardInfo : Attribute
    {
        [Flags]
        public enum Flag
        {
            ReadOnly = 0x01,
            Hidden = 0x02,
            System = 0x04,
            VolumeLabel = 0x08,
            Directory = 0x010,
            Archive = 0x020,
            Device = 0x040,
            Normal = 0x080,
            Temporary = 0x0100,
            SparseFile = 0x0200,
            ReparsePoint = 0x0400,
            Compressed = 0x0800,
            Offline = 0x01000,
            NotContentIndexed = 0x02000,
            Encrypted = 0x04000,
            Unknown = 0x08000,
            Virtual = 0x010000,
            IsDirectory = 0x10000000,
            IsIndexView = 0x20000000
        }

        public StandardInfo(byte[] rawBytes) : base(rawBytes)
        {
            var createdRaw = BitConverter.ToInt64(rawBytes, 0x18);
            if (createdRaw > 0)
            {
                CreatedOn = DateTimeOffset.FromFileTime(BitConverter.ToInt64(rawBytes, 0x18)).ToUniversalTime();
            }

            var contentModRaw = BitConverter.ToInt64(rawBytes, 0x20);
            if (contentModRaw > 0)
            {
                ContentModifiedOn = DateTimeOffset.FromFileTime(BitConverter.ToInt64(rawBytes, 0x20)).ToUniversalTime();
            }


            var recordModRaw = BitConverter.ToInt64(rawBytes, 0x28);
            if (recordModRaw > 0)
            {
                RecordModifiedOn = DateTimeOffset.FromFileTime(BitConverter.ToInt64(rawBytes, 0x28)).ToUniversalTime();
            }


            var lastAccessRaw = BitConverter.ToInt64(rawBytes, 0x30);
            if (lastAccessRaw > 0)
            {
                LastAccessedOn = DateTimeOffset.FromFileTime(BitConverter.ToInt64(rawBytes, 0x30)).ToUniversalTime();
            }

            Flags = (Flag) BitConverter.ToInt32(rawBytes, 0x38);

            MaxVersion = BitConverter.ToInt32(rawBytes, 0x3C);
            VersionNumber = BitConverter.ToInt32(rawBytes, 0x40);
            ClassId = BitConverter.ToInt32(rawBytes, 0x44);

            if (rawBytes.Length <= 0x48)
            {
                return;
            }

            OwnerId = BitConverter.ToInt32(rawBytes, 0x48);
            SecurityId = BitConverter.ToInt32(rawBytes, 0x4C);
            QuotaCharged = BitConverter.ToInt32(rawBytes, 0x50);
            UpdateSequenceNumber = BitConverter.ToInt64(rawBytes, 0x58);
        }

        public int MaxVersion { get; }
        public int VersionNumber { get; }
        public int ClassId { get; }
        public int OwnerId { get; }
        public int SecurityId { get; }
        public int QuotaCharged { get; }
        public long UpdateSequenceNumber { get; }

        public DateTimeOffset? CreatedOn { get; }
        public DateTimeOffset? ContentModifiedOn { get; }
        public DateTimeOffset? RecordModifiedOn { get; }
        public DateTimeOffset? LastAccessedOn { get; }

        public Flag Flags { get; }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendLine("**** " + GetType().Name.ToUpperInvariant() + " ****");

            sb.AppendLine(base.ToString());

            sb.AppendLine();

            sb.AppendLine(
                $"Flags: {Flags.ToString()} MaxVersion: 0x{MaxVersion:X} VersionNumber: 0x{VersionNumber:X}, ClassId: 0x{ClassId:X}" +
                $"OwnerId: 0x{OwnerId:X} SecurityId: 0x{SecurityId:X}, QuotaCharged: 0x{QuotaCharged:X}" +
                $"\r\nUpdateSequenceNumber: 0x{UpdateSequenceNumber:X}" +
                $"\r\nCreatedOn: {CreatedOn?.ToString(MftFile.DateTimeFormat)}" +
                $"\r\nContentModifiedOn: {ContentModifiedOn?.ToString(MftFile.DateTimeFormat)}" +
                $"\r\nRecordModifiedOn: {RecordModifiedOn?.ToString(MftFile.DateTimeFormat)}" +
                $"\r\nLastAccessedOn: {LastAccessedOn?.ToString(MftFile.DateTimeFormat)}");

            return sb.ToString();
        }
    }
}