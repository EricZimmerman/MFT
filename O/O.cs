using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MFT.Other;
using Serilog;

namespace O;

public class O
{
    /// <summary>OEntry reads this many bytes: the index entry header (0x10) and the $O key and data (0x48)</summary>
    private const int EntrySize = 0x58;

    public O(Stream fileStream)
    {
        var pageSize = 0x1000;

        var rawBytes2 = new byte[fileStream.Length];
        var read = 0;
        while (read < rawBytes2.Length)
        {
            var n = fileStream.Read(rawBytes2, read, rawBytes2.Length - read);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        var sig = 0x58444E49;

        //this used to be created again for every page, so only the last page's entries were returned
        Entries = new List<OEntry>();

        for (var index2 = 0; index2 + 0x28 <= read; index2 += pageSize)
        {
            var rawBytes = new byte[pageSize];
            Buffer.BlockCopy(rawBytes2, index2, rawBytes, 0, Math.Min(pageSize, read - index2));

            var sigActual = BitConverter.ToInt32(rawBytes, 0);

            if (sigActual != sig)
            {
                if (index2 == 0)
                {
                    throw new Exception("Invalid header! Expected 'INDX' Signature.");
                }

                if (sigActual != 0)
                {
                    ParseErrors.Add($"Page at 0x{index2:X} has no 'INDX' signature; skipped");
                }

                continue;
            }

            var fixupOffset = BitConverter.ToInt16(rawBytes, 4);
            var numFixupPairs = BitConverter.ToInt16(rawBytes, 6);

            //the index node header is at 0x18; its offsets are relative to it
            var indexValOffset = BitConverter.ToInt32(rawBytes, 0x18);
            var indexNodeSize = BitConverter.ToInt32(rawBytes, 0x18 + 4);

            if (fixupOffset > 0 && numFixupPairs > 0 && fixupOffset + numFixupPairs * 2 <= rawBytes.Length)
            {
                var fixupBuffer = new byte[numFixupPairs * 2];
                Buffer.BlockCopy(rawBytes, fixupOffset, fixupBuffer, 0, fixupBuffer.Length);

                var fixupData = new FixupData(fixupBuffer);

                //fixup verification
                var counter = 512;
                foreach (var bytese in fixupData.FixupActual)
                {
                    //adjust the offset to where we need to check
                    var fixupOffset1 = counter - 2;

                    if (fixupOffset1 + 2 > rawBytes.Length)
                    {
                        break;
                    }

                    var expected = BitConverter.ToInt16(rawBytes, fixupOffset1);
                    if (expected != fixupData.FixupExpected)
                    {
                        Log.Warning(
                            "Fixup values do not match at 0x{FixupOffset1:X}. Expected: 0x{FixupExpected:X2}, actual: 0x{Expected:X2}",
                            fixupOffset1, fixupData.FixupExpected, expected);
                    }

                    //replace fixup expected with actual bytes. bytese has actual replacement values in it.
                    Buffer.BlockCopy(bytese, 0, rawBytes, fixupOffset1, 2);

                    counter += 512;
                }
            }
            else
            {
                ParseErrors.Add($"Page at 0x{index2:X}: fixup array does not fit; fixups not applied");
            }

            var index = 0x18 + indexValOffset;
            var end = Math.Min(rawBytes.Length, 0x18 + indexNodeSize);

            if (indexValOffset < 0x10 || index > end)
            {
                ParseErrors.Add($"Page at 0x{index2:X}: entries at 0x{indexValOffset:X} do not fit in the page");
                continue;
            }

            Log.Verbose("Overall offset: 0x{Index2:X} Starting new INDEX ENTRY AREA at subindex {Index:X}", index2,
                index);

            while (end - index >= 0x10)
            {
                var sizeOfIndexEntry = BitConverter.ToUInt16(rawBytes, index + 8);

                var flags = BitConverter.ToUInt16(rawBytes, index + 12);

                if ((flags & 2) == 2 || sizeOfIndexEntry < 0x10 || sizeOfIndexEntry > end - index)
                {
                    //the last entry, or damaged
                    break;
                }

                if (sizeOfIndexEntry < EntrySize || end - index < EntrySize)
                {
                    ParseErrors.Add(
                        $"Page at 0x{index2:X}: entry at 0x{index2 + index:X} is 0x{sizeOfIndexEntry:X} bytes, too small for an $O entry");
                    index += sizeOfIndexEntry;
                    continue;
                }

                var buff = new byte[EntrySize];
                Buffer.BlockCopy(rawBytes, index, buff, 0, EntrySize);

                var oe = new OEntry(buff, index2 + index);

                index += sizeOfIndexEntry;

                if (oe.MftReference.MftEntryNumber == 0 && oe.MftReference.MftSequenceNumber == 0)
                {
                    continue;
                }

                Entries.Add(oe);
            }
        }
    }

    /// <summary>Pages or entries that could not be read; the others are kept</summary>
    public List<string> ParseErrors { get; } = new();

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
        using var br = new BinaryReader(new MemoryStream(rawBytes));
        AbsoluteOffset = absoluteOffset;
        OffsetToData = br.ReadUInt16();
        DataSize = br.ReadUInt16();
        br.ReadInt32(); //padding
        IndexEntrySize = br.ReadUInt16();
        IndexKeySize = br.ReadUInt16();
        Flags = (OEntryFlag)br.ReadInt16();
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