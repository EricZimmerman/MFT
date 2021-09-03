using System;
using System.Text;
using MFT.Attributes;
using NLog;

namespace MFT.Other
{
    public class IndexEntry
    {
        public IndexEntry(byte[] rawBytes)
        {
            var index = 0;
           // var size = BitConverter.ToInt16(rawBytes, index);
            index += 2;

            var indexKeyDataSize = BitConverter.ToInt16(rawBytes, index);
            index += 2;

            var indexFlags = (IndexRoot.IndexFlag) BitConverter.ToInt32(rawBytes, index);
            index += 4;

            if ((indexFlags & IndexRoot.IndexFlag.IsLast) == IndexRoot.IndexFlag.IsLast)
            {
                return;
            }

            if (indexKeyDataSize == 0x10)
            {
                //indicates no more index entries
                return;
            }

            if (indexKeyDataSize <= 0x40)
            {
                //too small to do anything with
                return;
            }

            if (indexKeyDataSize > 0)
            {
                var mftInfoBytes = new byte[8];
                Buffer.BlockCopy(rawBytes, index, mftInfoBytes, 0, 8);
                index += 8;

                ParentMftRecord = new MftEntryInfo(mftInfoBytes);

                var createdRaw = BitConverter.ToInt64(rawBytes, index);
                if (createdRaw > 0)
                {
                    try
                    {
                        CreatedOn = DateTimeOffset.FromFileTime(createdRaw).ToUniversalTime();
                    }
                    catch (Exception e)
                    {
                        var l = LogManager.GetLogger("IndexEntry");
                        l.Warn($"Invalid CreatedOn timestamp. Enable --debug for more details");
                    }
                    
                }

                index += 8;

                var contentModRaw = BitConverter.ToInt64(rawBytes, index);
                if (contentModRaw > 0)
                {
                    try
                    {
                        ContentModifiedOn = DateTimeOffset.FromFileTime(contentModRaw).ToUniversalTime();
                    }
                    catch (Exception e)
                    {
                        var l = LogManager.GetLogger("IndexEntry");
                        l.Warn($"Invalid ContentModifiedOn timestamp. Enable --debug for more details");
                    }
                   
                }

                index += 8;

                var recordModRaw = BitConverter.ToInt64(rawBytes, index);
                if (recordModRaw > 0)
                {
                    try
                    {
                        RecordModifiedOn = DateTimeOffset.FromFileTime(recordModRaw).ToUniversalTime();
                    }
                    catch (Exception e)
                    {
                        var l = LogManager.GetLogger("IndexEntry");
                        l.Warn($"Invalid RecordModifiedOn timestamp. Enable --debug for more details");
                    }
                   
                }

                index += 8;

                var lastAccessRaw = BitConverter.ToInt64(rawBytes, index);
                if (lastAccessRaw > 0)
                {
                    try
                    {
                        LastAccessedOn = DateTimeOffset.FromFileTime(lastAccessRaw).ToUniversalTime();
                    }
                    catch (Exception e)
                    {
                        var l = LogManager.GetLogger("IndexEntry");
                        l.Warn($"Invalid LastAccessedOn timestamp. Enable --debug for more details");
                    }
                   
                }

                index += 8;


                PhysicalSize = BitConverter.ToUInt64(rawBytes, index);
                index += 8;
                LogicalSize = BitConverter.ToUInt64(rawBytes, index);
                index += 8;

                Flags = (StandardInfo.Flag) BitConverter.ToInt32(rawBytes, index);
                index += 4;


                ReparseValue = BitConverter.ToInt32(rawBytes, index);
                index += 4;

                NameLength = rawBytes[index];
                index += 1;
                NameType = (NameTypes) rawBytes[index];
                index += 1;

                FileName = Encoding.Unicode.GetString(rawBytes, index, NameLength * 2);
            }

            //index += 2; //padding
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
                $"File name: {FileName} (Len:0x{NameLength:X}) Flags: {Flags.ToString().Replace(", ", "|")}, Name Type: {NameType} " +
                $"Reparse Value: 0x{ReparseValue:X} Physical Size: 0x{PhysicalSize:X}, Logical Size: 0x{LogicalSize:X}" +
                $"\r\nParent Mft Record: {ParentMftRecord} " +
                $"\r\nCreated On:\t\t{CreatedOn?.ToString(MftFile.DateTimeFormat)}" +
                $"\r\nContent Modified On:\t{ContentModifiedOn?.ToString(MftFile.DateTimeFormat)}" +
                $"\r\nRecord Modified On:\t{RecordModifiedOn?.ToString(MftFile.DateTimeFormat)}" +
                $"\r\nLast Accessed On:\t{LastAccessedOn?.ToString(MftFile.DateTimeFormat)}");

            return sb.ToString();
        }
    }
}