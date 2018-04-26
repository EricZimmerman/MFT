using System;
using System.Text;

namespace MFT.Attributes
{
    [Flags]
    public enum NameTypes
    {
        Posix = 0x0,
        Windows = 0x1,
        Dos = 0x2,
        DosWindows = 0x3
    }

    public class FileInfo
    {
        public FileInfo(byte[] rawBytes)
        {
//            if (rawBytes.Length == 0x04)
//            {
//                return;
//            }

            var entryBytes = new byte[8];

            Buffer.BlockCopy(rawBytes, 0, entryBytes, 0, 8);

            ParentMftRecord = new MftEntryInfo(entryBytes);

            var createdRaw = BitConverter.ToInt64(rawBytes, 0x8);
            if (createdRaw > 0)
            {
                CreatedOn = DateTimeOffset.FromFileTime(createdRaw).ToUniversalTime();
            }

            var contentModRaw = BitConverter.ToInt64(rawBytes, 0x10);
            if (contentModRaw > 0)
            {
                ContentModifiedOn = DateTimeOffset.FromFileTime(contentModRaw).ToUniversalTime();
            }

            var recordModRaw = BitConverter.ToInt64(rawBytes, 0x18);
            if (recordModRaw > 0)
            {
                RecordModifiedOn = DateTimeOffset.FromFileTime(recordModRaw).ToUniversalTime();
            }

            var lastAccessRaw = BitConverter.ToInt64(rawBytes, 0x20);
            if (lastAccessRaw > 0)
            {
                LastAccessedOn = DateTimeOffset.FromFileTime(lastAccessRaw).ToUniversalTime();
            }


            PhysicalSize = BitConverter.ToUInt64(rawBytes, 0x28);
            LogicalSize = BitConverter.ToUInt64(rawBytes, 0x30);

            Flags = (StandardInfo.Flag) BitConverter.ToInt32(rawBytes, 0x38);

            ReparseValue = BitConverter.ToInt32(rawBytes, 0x3c);

            NameLength = rawBytes[0x40];
            NameType = (NameTypes) rawBytes[0x41];

            FileName = Encoding.Unicode.GetString(rawBytes, 0x42, NameLength * 2);
        }

        public int ReparseValue { get; }
        public byte NameLength { get; }
        public NameTypes NameType { get; }
        public string FileName { get; }
        public ulong PhysicalSize { get; }
        public ulong LogicalSize { get; }
        public DateTimeOffset? CreatedOn { get; }
        public DateTimeOffset? ContentModifiedOn { get; }
        public DateTimeOffset? RecordModifiedOn { get; }
        public DateTimeOffset? LastAccessedOn { get; }
        public StandardInfo.Flag Flags { get; }

        public MftEntryInfo ParentMftRecord { get; }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendLine();

            sb.AppendLine($"File name: {FileName} (Len:0x{NameLength:X}) Flags: {Flags}, NameType: {NameType} " +
                          $"ReparseValue: {ReparseValue} PhysicalSize: 0x{PhysicalSize:X}, LogicalSize: 0x{LogicalSize:X}" +
                          $"\r\nParentMftRecord: {ParentMftRecord} " +
                          $"\r\nCreatedOn: {CreatedOn?.ToString(MftFile.DateTimeFormat)}" +
                          $"\r\nContentModifiedOn: {ContentModifiedOn?.ToString(MftFile.DateTimeFormat)}" +
                          $"\r\nRecordModifiedOn: {RecordModifiedOn?.ToString(MftFile.DateTimeFormat)}" +
                          $"\r\nLastAccessedOn: {LastAccessedOn?.ToString(MftFile.DateTimeFormat)}");

            return sb.ToString();
        }
    }
}