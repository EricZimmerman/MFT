using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MFT.Attributes;

namespace MFT.Other
{
  public  class IndexEntry
    {
       

        [Flags]
        public enum IndexEntryFlag
        {
            IndexEntryNode = 0x1,
            IndexEntryEnd = 0x2,
     
        }

        public IndexEntry(byte[] rawBytes)
        {
            var index = 0;
            var size = BitConverter.ToInt16(rawBytes, index);
            index += 2;

            var indexKeyDataSize = BitConverter.ToInt16(rawBytes, index);
            index += 2;

            var indexFlags = (IndexEntryFlag) BitConverter.ToInt32(rawBytes, index);
            index +=4;

            if ((indexFlags & IndexEntryFlag.IndexEntryEnd) == IndexEntryFlag.IndexEntryEnd)
            {
                return;
            }

            if ((indexFlags & IndexEntryFlag.IndexEntryNode) == IndexEntryFlag.IndexEntryNode)
            {
                return;
            }

            if (indexKeyDataSize == 0x10)
            {
                //indicates no more index entries
                return;
            }

            if (indexKeyDataSize == 0x04)
            {
                //too small to do anything with
                return;
            }

            if (indexKeyDataSize > 0)
            {
                 var mftInfoBytes = new byte[8];
                Buffer.BlockCopy(rawBytes,index,mftInfoBytes,0,8);
                index += 8;

                 ParentMftRecord = new MftEntryInfo(mftInfoBytes);

                var createdRaw = BitConverter.ToInt64(rawBytes, index);
                if (createdRaw > 0)
                {
                    CreatedOn = DateTimeOffset.FromFileTime(createdRaw).ToUniversalTime();
                }

                index += 8;

                var contentModRaw = BitConverter.ToInt64(rawBytes, index);
                if (contentModRaw > 0)
                {
                    ContentModifiedOn = DateTimeOffset.FromFileTime(contentModRaw).ToUniversalTime();
                }

                index += 8;

                var recordModRaw = BitConverter.ToInt64(rawBytes, index);
                if (recordModRaw > 0)
                {
                    RecordModifiedOn = DateTimeOffset.FromFileTime(recordModRaw).ToUniversalTime();
                }

                index += 8;

                var lastAccessRaw = BitConverter.ToInt64(rawBytes, index);
                if (lastAccessRaw > 0)
                {
                    LastAccessedOn = DateTimeOffset.FromFileTime(lastAccessRaw).ToUniversalTime();
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

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendLine();

            sb.AppendLine($"File name: {FileName} (Len:{NameLength}) Flags: {Flags}, NameType: {NameType} " +
                          $"ReparseValue: {ReparseValue} PhysicalSize: {PhysicalSize}, LogicalSize: {LogicalSize}" +
                          $"\r\nMFTRecordToBaseRecord: {ParentMftRecord} " +
                          $"\r\nCreatedOn: {CreatedOn?.ToString(MftFile.DateTimeFormat)}" +
                          $"\r\nContentModifiedOn: {ContentModifiedOn?.ToString(MftFile.DateTimeFormat)}" +
                          $"\r\nRecordModifiedOn: {RecordModifiedOn?.ToString(MftFile.DateTimeFormat)}" +
                          $"\r\nLastAccessedOn: {LastAccessedOn?.ToString(MftFile.DateTimeFormat)}");

            return sb.ToString();
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
    }
}
