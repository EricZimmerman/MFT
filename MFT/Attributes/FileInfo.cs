using System;
using System.Text;
using MFT.Other;
using NLog;

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
            var entryBytes = new byte[8];

            Buffer.BlockCopy(rawBytes, 0, entryBytes, 0, 8);

            ParentMftRecord = new MftEntryInfo(entryBytes);

            var createdRaw = BitConverter.ToInt64(rawBytes, 0x8);
            if (createdRaw > 0)
            {
                try
                {
                    CreatedOn = DateTimeOffset.FromFileTime(createdRaw).ToUniversalTime();
                }
                catch (Exception )
                {
                    var l = LogManager.GetLogger("FileInfo");
                    l.Warn($"Invalid ConCreatedOntentModifiedOn timestamp! Enable --debug for record information");
                }
            }

            var contentModRaw = BitConverter.ToInt64(rawBytes, 0x10);
            if (contentModRaw > 0)
            {
                try
                {
                    ContentModifiedOn = DateTimeOffset.FromFileTime(contentModRaw).ToUniversalTime();
                }
                catch (Exception )
                {
                    var l = LogManager.GetLogger("FileInfo");
                    l.Warn($"Invalid ContentModifiedOn timestamp! Enable --debug for record information");
                }
            }

            var recordModRaw = BitConverter.ToInt64(rawBytes, 0x18);
            if (recordModRaw > 0)
            {
                try
                {
                    RecordModifiedOn = DateTimeOffset.FromFileTime(recordModRaw).ToUniversalTime();
                }
                catch (Exception )
                {
                    var l = LogManager.GetLogger("FileInfo");
                    l.Warn($"Invalid RecordModifiedOn timestamp! Enable --debug for record information");
                }
            }
             
            var lastAccessRaw = BitConverter.ToInt64(rawBytes, 0x20);
            if (lastAccessRaw > 0)
            {
                try
                {
                    LastAccessedOn = DateTimeOffset.FromFileTime(lastAccessRaw).ToUniversalTime();
                }
                catch (Exception )
                {
                    var l = LogManager.GetLogger("FileInfo");
                    l.Warn($"Invalid LastAccessedOn timestamp! Enable --debug for record information");
                }
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

            sb.AppendLine(
                $"File name: {FileName} (Length: 0x{NameLength:X})\r\nFlags: {Flags.ToString().Replace(", ", "|")}, Name Type: {NameType}, " +
                $"Reparse Value: 0x{ReparseValue:X}, Physical Size: 0x{PhysicalSize:X}, Logical Size: 0x{LogicalSize:X}" +
                $"\r\nParent Mft Record: {ParentMftRecord}" +
                $"\r\n\r\nCreated On:\t\t{CreatedOn?.ToString(MftFile.DateTimeFormat)}" +
                $"\r\nContent Modified On:\t{ContentModifiedOn?.ToString(MftFile.DateTimeFormat)}" +
                $"\r\nRecord Modified On:\t{RecordModifiedOn?.ToString(MftFile.DateTimeFormat)}" +
                $"\r\nLast Accessed On:\t{LastAccessedOn?.ToString(MftFile.DateTimeFormat)}");

            return sb.ToString();
        }
    }
}