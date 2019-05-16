using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MFT.Other;
using NLog;

namespace O
{
    public class O
    {
        public O(Stream fileStream)
        {
            var logger = LogManager.GetLogger("O");

            var pageSize = 0x1000;

            var rawBytes2 = new byte[fileStream.Length];
            fileStream.Read(rawBytes2, 0, (int) fileStream.Length);

            var sig = 0x58444E49;

            var index2 = 0x0;

            while (index2 < rawBytes2.Length)
            {
                var index = 0x0;

                var rawBytes = new byte[pageSize];
                Buffer.BlockCopy(rawBytes2, index2, rawBytes, 0, pageSize);

                var headerBytes = new byte[4];

                fileStream.Seek(0, SeekOrigin.Begin);

                fileStream.Read(headerBytes, 0, 4);

                var sigActual = BitConverter.ToInt32(headerBytes, 0);

                if (sig != sigActual)
                {
                    {
                        throw new Exception("Invalid header! Expected 'INDX' Signature.");
                    }
                }

                Entries = new List<OEntry>();

                index += 4;

                var fixupOffset = BitConverter.ToInt16(rawBytes, index);
                index += 2;
                var numFixupPairs = BitConverter.ToInt16(rawBytes, index);
                index += 2;
                var logFileSequenceNumber = BitConverter.ToInt64(rawBytes, index);
                index += 8;
                var virtualClusterNumber = BitConverter.ToInt64(rawBytes, index);
                index += 8;

                var dataStartPosition = index;

                var indexValOffset = BitConverter.ToInt32(rawBytes, index);
                index += 4;
                var indexNodeSize = BitConverter.ToInt32(rawBytes, index);
                index += 4;
                var indexAllocatedSize = BitConverter.ToInt32(rawBytes, index);
                index += 4;
                var indexFlags = BitConverter.ToInt32(rawBytes, index);
                index += 4;

                var fixupTotalLength = numFixupPairs * 2;

                var fixupBuffer = new byte[fixupTotalLength];
                Buffer.BlockCopy(rawBytes, fixupOffset, fixupBuffer, 0, fixupTotalLength);


                var fixupData = new FixupData(fixupBuffer);

                var fixupOk = true;

                //fixup verification
                var counter = 512;
                foreach (var bytese in fixupData.FixupActual)
                {
                    //adjust the offset to where we need to check
                    var fixupOffset1 = counter - 2;

                    var expected = BitConverter.ToInt16(rawBytes, fixupOffset1);
                    if (expected != fixupData.FixupExpected)
                    {
                        fixupOk = false;
                        logger.Warn(
                            $"Fixup values do not match at 0x{fixupOffset1:X}. Expected: 0x{fixupData.FixupExpected:X2}, actual: 0x{expected:X2}");
                    }

                    //replace fixup expected with actual bytes. bytese has actual replacement values in it.
                    Buffer.BlockCopy(bytese, 0, rawBytes, fixupOffset1, 2);

                    counter += 512;
                }

                index += fixupTotalLength;

                while (index % 8 != 0)
                {
                    index += 1;
                }

                logger.Trace($"Overall offset: 0x{index2:X} Starting new INDEX ENTRY AREA at subindex {index:X}");

                while (index < rawBytes.Length)
                {
                    //var offsetToData = BitConverter.ToUInt16(rawBytes, index);
                    //var sizeOfData = BitConverter.ToUInt16(rawBytes, index+2);
                    var sizeOfIndexEntry = BitConverter.ToUInt16(rawBytes, index + 8);
                    //var sizeOfIndexKey = BitConverter.ToUInt16(rawBytes, index+10);
                    var flags = BitConverter.ToUInt16(rawBytes, index + 12);

                    if (sizeOfIndexEntry == 0x10)
                    {
                        sizeOfIndexEntry = 0x58;
                    }

                    if (flags == 3 || sizeOfIndexEntry == 0 || index + sizeOfIndexEntry > rawBytes.Length)
                    {
                        break;
                    }

                    var buff = new byte[sizeOfIndexEntry];
                    Buffer.BlockCopy(rawBytes, index, buff, 0, 0x58);

                    var oe = new OEntry(buff, index2 + index);

                    index += sizeOfIndexEntry;

                    if (oe.MftReference.MftEntryNumber == 0 && oe.MftReference.MftSequenceNumber == 0)
                    {
                        continue;
                    }

                    Entries.Add(oe);
                }

                index2 += pageSize;
            }
        }

        public List<OEntry> Entries { get; }
    }

    public class OEntry
    {
        public enum OEntryFlag
        {
            HasSubNodes = 0x1,
            LastEntry = 0x2
        }

        public OEntry(byte[] rawBytes, int absoluteOffset)
        {
            using (var br = new BinaryReader(new MemoryStream(rawBytes)))
            {
                AbsoluteOffset = absoluteOffset;
                OffsetToData = br.ReadUInt16();
                DataSize = br.ReadUInt16();
                br.ReadInt32(); //padding
                IndexEntrySize = br.ReadUInt16();
                IndexKeySize = br.ReadUInt16();
                Flags = (OEntryFlag) br.ReadInt16();
                br.ReadInt16(); //padding
                ObjectId = new Guid(br.ReadBytes(16));
                MftReference = new MftEntryInfo(br.ReadBytes(8));
                BirthVolumeId = new Guid(br.ReadBytes(16));
                BirthObjectId = new Guid(br.ReadBytes(16));
                DomainId = new Guid(br.ReadBytes(16));

                var tempMac = ObjectId.ToString().Split('-').Last();
                ObjectIdMacAddress = Regex.Replace(tempMac, ".{2}", "$0:");

                tempMac = BirthObjectId.ToString().Split('-').Last();
                BirthVolumeIdMacAddress = Regex.Replace(tempMac, ".{2}", "$0:");

                ObjectIdCreatedOn = GetDateTimeOffsetFromGuid(ObjectId);
                BirthVolumeIdCreatedOn = GetDateTimeOffsetFromGuid(BirthObjectId);
            }
        }

        public int AbsoluteOffset { get; }
        public ushort OffsetToData { get; }
        public ushort DataSize { get; }
        public ushort IndexEntrySize { get; }
        public ushort IndexKeySize { get; }
        public OEntryFlag Flags { get; }

        public Guid ObjectId { get; }
        public MftEntryInfo MftReference { get; }
        public Guid BirthVolumeId { get; }
        public Guid BirthObjectId { get; }
        public Guid DomainId { get; }

        public string ObjectIdMacAddress { get; }
        public DateTimeOffset ObjectIdCreatedOn { get; }


        public string BirthVolumeIdMacAddress { get; }
        public DateTimeOffset BirthVolumeIdCreatedOn { get; }

        private DateTimeOffset GetDateTimeOffsetFromGuid(Guid guid)
        {
            // offset to move from 1/1/0001, which is 0-time for .NET, to gregorian 0-time of 10/15/1582
            var gregorianCalendarStart = new DateTimeOffset(1582, 10, 15, 0, 0, 0, TimeSpan.Zero);
            const int versionByte = 7;
            const int versionByteMask = 0x0f;
            const int versionByteShift = 4;
            const byte timestampByte = 0;

            var bytes = guid.ToByteArray();

            // reverse the version
            bytes[versionByte] &= versionByteMask;
            bytes[versionByte] |= 0x01 >> versionByteShift;

            var timestampBytes = new byte[8];
            Array.Copy(bytes, timestampByte, timestampBytes, 0, 8);

            var timestamp = BitConverter.ToInt64(timestampBytes, 0);
            var ticks = timestamp + gregorianCalendarStart.Ticks;

            return new DateTimeOffset(ticks, TimeSpan.Zero);
        }

        public override string ToString()
        {
            return
                $"Abs offset: {AbsoluteOffset} MFT Info: {MftReference} Object Id: {ObjectId} Birth Volume Id: {BirthVolumeId} Birth Object Id: {BirthObjectId} Domain Id: {DomainId} Flags: {Flags} ObjectId MAC: {ObjectIdMacAddress} ObjectIdCreatedOn: {ObjectIdCreatedOn.ToUniversalTime():yyyy-MM-dd HH:mm:ss.fffffff} BirthVolumeId MAC: {BirthVolumeIdMacAddress} BirthVolumeIdCreatedOn: {BirthVolumeIdCreatedOn.ToUniversalTime():yyyy-MM-dd HH:mm:ss.fffffff}";
        }
    }
}