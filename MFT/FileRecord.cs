using System;
using System.Collections.Generic;

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MFT.Attributes;
using MFT.Other;
using Serilog;
using Attribute = MFT.Attributes.Attribute;

namespace MFT;

public class FileRecord
{
    [Flags]
    public enum EntryFlag
    {
        IsFree = 0x0,
        InUse = 0x1,
        IsDirectory = 0x2,
        IsMetaDataRecord = 0x4,
        IsIndexView = 0x8
    }

    private const int BaadSig = 0x44414142;
    private const int FileSig = 0x454c4946;

    /// <summary>The fixed part of the record header (through the entry number at 0x2C)</summary>
    private const int HeaderSize = 0x30;

    private static readonly byte[] NoFixups = new byte[2];

    private readonly byte[] _fixupRaw;
    private List<string> _parseErrors;

    /// <summary>The smallest attribute: a resident attribute header</summary>
    private const int MinAttributeSize = 0x18;

    public FileRecord(byte[] rawBytes, long offset, bool recoverFromSlack) : this(rawBytes, offset, recoverFromSlack, true)
    {
    }

    /// <param name="applyFixups">
    ///     False for records that were never written to disk with fixups, such as the copies in $LogFile: there the
    ///     sector ends hold real data
    /// </param>
    internal FileRecord(byte[] rawBytes, long offset, bool recoverFromSlack, bool applyFixups)
    {
        Offset = offset;

        if (rawBytes.Length < HeaderSize)
        {
            Log.Debug("FILE record at offset 0x{Offset:X} is only 0x{Length:X} bytes", offset, rawBytes.Length);
            ParseErrors.Add($"Record is only 0x{rawBytes.Length:X} bytes; the header needs 0x{HeaderSize:X}");
            IsBad = true;
            return;
        }

        var sig = BitConverter.ToInt32(rawBytes, 0);

        switch (sig)
        {
            case FileSig:
                break;

            case BaadSig:
                Log.Debug("Bad signature at offset 0x{Offset:X}", offset);
                IsBad = true;
                return;
            default:
                //not initialized
                Log.Debug("Uninitialized entry (no signature) at offset 0x{Offset:X}", offset);
                IsUninitialized = true;
                return;
        }

        Log.Debug("Processing FILE record at offset 0x{Offset:X}", offset);

        Attributes = new List<Attribute>();

        FixupOffset = BitConverter.ToInt16(rawBytes, 0x4);
        FixupEntryCount = BitConverter.ToInt16(rawBytes, 0x6);

        //pull this early so we can check if its free in our fix up value messages
        EntryFlags = (EntryFlag)BitConverter.ToInt16(rawBytes, 0x16);

        //to build fixup info, take FixupEntryCount x 2 bytes as each are 2 bytes long
        var fixupTotalLength = FixupEntryCount * 2;

        if (applyFixups == false)
        {
            _fixupRaw = NoFixups;
            FixupOk = true;
        }
        else if (FixupOffset < 0 || FixupEntryCount < 1 || FixupOffset + fixupTotalLength > rawBytes.Length)
        {
            ParseErrors.Add(
                $"Fixup array at 0x{FixupOffset:X} with 0x{FixupEntryCount:X} entries does not fit in the record; fixups not applied");
            _fixupRaw = NoFixups;
        }
        else
        {
            var fixupBuffer = new byte[fixupTotalLength];
            Buffer.BlockCopy(rawBytes, FixupOffset, fixupBuffer, 0, fixupTotalLength);

            _fixupRaw = fixupBuffer;
            var fixupExpected = BitConverter.ToInt16(fixupBuffer, 0);

            FixupOk = true;

            //fixup verification: the first value is expected at the end of each sector, the rest are what goes there
            var counter = 512;
            for (var actual = 2; actual + 2 <= fixupBuffer.Length; actual += 2)
            {
                //adjust the offset to where we need to check
                var fixupOffset = counter - 2;

                if (fixupOffset + 2 > rawBytes.Length)
                {
                    //more fixups than sectors in the record
                    break;
                }

                var expected = BitConverter.ToInt16(rawBytes, fixupOffset);
                if (expected != fixupExpected && EntryFlags != 0x0)
                {
                    FixupOk = false;
                    Log.Warning(
                        "Offset: 0x{Offset:X} Entry/seq: 0x{EntryNumber:X}/0x{SequenceNumber:X} Fixup values do not match at 0x{FixupOffset:X}. Expected: 0x{FixupExpected:X2}, actual: 0x{Expected:X2}",
                        Offset, BitConverter.ToUInt32(rawBytes, 0x2c), BitConverter.ToUInt16(rawBytes, 0x10), fixupOffset,
                        fixupExpected, expected);
                }

                //replace fixup expected with actual bytes
                Buffer.BlockCopy(fixupBuffer, actual, rawBytes, fixupOffset, 2);

                counter += 512;
            }
        }

        LogSequenceNumber = BitConverter.ToInt64(rawBytes, 0x8);

        SequenceNumber = BitConverter.ToUInt16(rawBytes, 0x10);

        ReferenceCount = BitConverter.ToInt16(rawBytes, 0x12);

        FirstAttributeOffset = BitConverter.ToInt16(rawBytes, 0x14);

        ActualRecordSize = BitConverter.ToInt32(rawBytes, 0x18);

        AllocatedRecordSize = BitConverter.ToInt32(rawBytes, 0x1c);

        var entryBytes = new byte[8];

        Buffer.BlockCopy(rawBytes, 0x20, entryBytes, 0, 8);

        MftRecordToBaseRecord = new MftEntryInfo(entryBytes);

        FirstAvailablAttribueId = BitConverter.ToInt16(rawBytes, 0x28);

        if (FixupOffset >= HeaderSize)
        {
            EntryNumber = BitConverter.ToUInt32(rawBytes, 0x2c);
        }
        else
        {
            //NTFS 3.0 records have no entry number (the fixup array starts at 0x2A), so use the record's position
            EntryNumber = (uint)(offset / rawBytes.Length);
        }

        Log.Debug("FILE record entry/seq #: 0x{EntryNumber:X}/{SequenceNumber:X}", EntryNumber, SequenceNumber);

        //attributes run until the end marker. Deleted records can have their used size reduced while the
        //attributes after it are intact, so keep going past it (up to the allocated size), but there anything
        //that does not look like an attribute just ends the list quietly
        var end = AllocatedRecordSize > 0 && AllocatedRecordSize <= rawBytes.Length
            ? AllocatedRecordSize
            : rawBytes.Length;
        var used = ActualRecordSize > 0 && ActualRecordSize <= end ? ActualRecordSize : end;

        //start attribute processing at FirstAttributeOffset
        var index = (int)FirstAttributeOffset;

        if (index < HeaderSize - 8 || index > end)
        {
            ParseErrors.Add($"First attribute offset 0x{FirstAttributeOffset:X} is outside the record");
            index = end;
        }

        while (index + 8 <= end)
        {
            var attrType = (AttributeType)BitConverter.ToInt32(rawBytes, index);

            var attrSize = BitConverter.ToInt32(rawBytes, index + 4);

            if (attrSize == 0 || attrType == AttributeType.EndOfAttributes)
            {
                index += 8; //skip -1 type and 0 size
                break;
            }

            var pastUsedSize = index >= used;

            if (attrSize < MinAttributeSize || attrSize > end - index)
            {
                if (pastUsedSize == false)
                {
                    ParseErrors.Add(
                        $"Attribute {attrType} at 0x{index:X} has size 0x{attrSize:X}; 0x{end - index:X} bytes are left in the record");
                }

                break;
            }

            Log.Debug(
                "Found Attribute Type {AttrType} at absolute offset: 0x{Offset:X}", attrType.ToString(),
                index + offset);

            Log.Verbose(
                "ActualRecordSize: 0x{ActualRecordSize:X}, size: 0x{AttrSize:X}, index: 0x{Index:X}", ActualRecordSize,
                attrSize, index);

            var rawAttr = new byte[attrSize];
            Buffer.BlockCopy(rawBytes, index, rawAttr, 0, attrSize);

            Attribute attribute;
            var failed = false;
            try
            {
                attribute = ParseAttribute(attrType, rawAttr);
            }
            catch (Exception e)
            {
                attribute = null;
                failed = true;

                if (pastUsedSize)
                {
                    break;
                }

                ParseErrors.Add($"Attribute {attrType} at 0x{index:X} could not be parsed: {e.Message}");
                Log.Debug(e, "Offset 0x{Offset:X}: attribute {AttrType} at 0x{Index:X} could not be parsed", Offset,
                    attrType, index);

                if (attrType == AttributeType.ReparsePoint)
                {
                    Log.Error(e,
                        "There was an error parsing a ReparsePoint in FILE record at offset 0x{Offset:X}. Please extract via --dd and --do and send to saericzimmerman@gmail.com",
                        Offset);
                }
            }

            if (attribute == null && pastUsedSize)
            {
                //not an attribute type we know, past the used size: leftovers
                break;
            }

            if (attribute == null && failed == false)
            {
                ParseErrors.Add($"Unknown attribute type 0x{(int)attrType:X} at 0x{index:X}; skipped");
            }

            if (attribute != null)
            {
                Attributes.Add(attribute);
            }

            index += attrSize;
        }

        Log.Verbose("Slack starts at 0x{Index:X} Absolute offset: 0x{Offset:X}", index, index + offset);

        if (recoverFromSlack == false)
        {
            return;
        }
        
        var slackSpace = new byte[rawBytes.Length - index];
        Buffer.BlockCopy(rawBytes, index, slackSpace, 0, slackSpace.Length);

        var slackIe =  GetSlackFileEntries(slackSpace, 0, index,EntryNumber);

        if (slackIe.Count == 0)
        {
            return;
        }
      

        Log.Warning("");
        Log.Warning("Found {Count:N0} Index entries found in slack space!",slackIe.Count);
        foreach (var indexEntryI30 in slackIe)
        {
            Log.Warning("Name: {Name}, Index Flag: {Flag}, Offset: {Offset} MFT Entry/seq: {Entry}/{Seq}" ,indexEntryI30.FileInfo.FileName,indexEntryI30.Flag,$"0x{indexEntryI30.AbsoluteOffset:X}",$"0x{indexEntryI30.MftReferenceSelf?.MftEntryNumber:X}",$"0x{indexEntryI30.MftReferenceSelf?.MftSequenceNumber:X}");
            Log.Warning("File flags: {Flags}, Parent MFT Entry/seq: {Entry}/{Seq}",indexEntryI30.FileInfo.Flags,$"0x{indexEntryI30.FileInfo.ParentMftRecord.MftEntryNumber:X}",$"0x{indexEntryI30.FileInfo.ParentMftRecord.MftSequenceNumber:X}");
            Log.Warning("Created on:          {Date}",indexEntryI30.FileInfo.CreatedOn);
            Log.Warning("Content Modified on: {Date}",indexEntryI30.FileInfo.ContentModifiedOn);
            Log.Warning("Record Modified on:  {Date}",indexEntryI30.FileInfo.RecordModifiedOn);
            Log.Warning("Last Accessed on:    {Date}",indexEntryI30.FileInfo.LastAccessedOn);
            Log.Warning("");
        }


    }
    

    /// <returns>The attribute, or null for a type that is not handled</returns>
    internal static Attribute ParseAttribute(AttributeType attrType, byte[] rawAttr)
    {
        switch (attrType)
        {
            case AttributeType.StandardInformation:
                return new StandardInfo(rawAttr);
            case AttributeType.FileName:
                return new FileName(rawAttr);
            case AttributeType.Data:
                return new Data(rawAttr);
            case AttributeType.IndexAllocation:
                return new IndexAllocation(rawAttr);
            case AttributeType.IndexRoot:
                return new IndexRoot(rawAttr);
            case AttributeType.Bitmap:
                return new Bitmap(rawAttr);
            case AttributeType.VolumeVersionObjectId:
                return new ObjectId_(rawAttr);
            case AttributeType.SecurityDescriptor:
                return new SecurityDescriptor(rawAttr);
            case AttributeType.VolumeName:
                return new VolumeName(rawAttr);
            case AttributeType.VolumeInformation:
                return new VolumeInformation(rawAttr);
            case AttributeType.LoggedUtilityStream:
                return new LoggedUtilityStream(rawAttr);
            case AttributeType.ReparsePoint:
                return new ReparsePoint(rawAttr);
            case AttributeType.AttributeList:
                return new AttributeList(rawAttr);
            case AttributeType.Ea:
                return new ExtendedAttribute(rawAttr);
            case AttributeType.EaInformation:
                return new ExtendedAttributeInformation(rawAttr);
            default:
                return null;
        }
    }

    public static List<IndexEntryI30> GetSlackFileEntries(byte[] slackSpace, int pageNumber, int startOffset, uint entryNumber)
    {
        var ie = new List<IndexEntryI30>();

        var h = GetUnicodeHits(slackSpace);
        
        foreach (var hitInfo in h)
        {
            Log.Verbose("Processing slack offset {O} {H}", hitInfo.Offset, hitInfo.Hit);

            //contains offset to start of hit and hit, but we only need start of the string to know where to begin
            //the start of the record is 0x42 bytes from where the hit is
            //since we know the offset of the hit, subtract 2 to get length of decoded string.
            //multiply by 2 for # of bytes we need to read.
            //add this to get the total length of the data we need to read adn read into slackspace as needed

            try
            {
                if (hitInfo.Offset == 0)
                {
                    //we cant get a size or anything useful, so skip it
                    Log.Warning("Found possible slack index entry for {FileName} at {Offset}, but not enough data to interpret. Skipping...",hitInfo.Hit,$"0x{startOffset + hitInfo.Offset:X}");
                    continue;
                }
                var nameSize = slackSpace[hitInfo.Offset - 2];
                var start = hitInfo.Offset - 0x42;
                var end = hitInfo.Offset + nameSize * 2;

                var buffSize = end - start;

                if (start < 0)
                {
                    Log.Warning("Found possible slack index entry for {FileName} at {Offset}, but not enough data to interpret. Skipping...",hitInfo.Hit,$"0x{startOffset + hitInfo.Offset:X}");
                    continue;
                }
            
                var buff = new byte[buffSize];
                Buffer.BlockCopy(slackSpace, start, buff, 0, buffSize);

                var md5 = GetMd5(buff);

                var slackIndex = new IndexEntryI30(buff, startOffset + start - 0x10, pageNumber, true)
                {
                    Md5 = md5
                };
                
                //some cleanup of questionable stuff
                if (slackIndex.FileInfo.NameLength == 0)
                {
                    continue;
                }

                if (slackIndex.FileInfo.Flags < 0)
                {
                    continue;
                }

                Log.Debug("Slack {Ie}", slackIndex);
                ie.Add(slackIndex);
            }
            catch (Exception e)
            {
                if (entryNumber > 0)
                {
                    Log.Warning(e,"Error processing slack index entry in FILE Entry {Entry}! You may want to review this manually. Offset {Offset}. Name: {Name}. Error: {Error}",$"ox{entryNumber:X}",$"0x{startOffset + hitInfo.Offset:X}",hitInfo.Hit,e.Message);    
                }
                else
                {
                    Log.Warning(e,"Error processing slack index entry! You may want to review this manually. Offset {Offset}. Name: {Name}. Error: {Error}",$"0x{startOffset +hitInfo.Offset:X}",hitInfo.Hit,e.Message);
                }
                
            }
            
            
        }

        return ie;
    }
    
    private static string GetMd5(byte[] input)
    {
        using var myHash = MD5.Create();
        var byteArrayResult =
            myHash.ComputeHash(input);
        return
            string.Concat(Array.ConvertAll(byteArrayResult,
                h => h.ToString("X2")));
    }


    /// <summary>Runs of 3 or more printable ASCII characters</summary>
    private static readonly Regex UnicodeRuns = new("[\u0020-\u007E]{3,}", RegexOptions.Compiled);

    private static List<HitInfo> GetUnicodeHits(byte[] bytes)
    {
        var uniString = Encoding.Unicode.GetString(bytes);

        var hits = new List<HitInfo>();

        foreach (Match match in UnicodeRuns.Matches(uniString))
        {
            if (match.Value.Trim().Length == 0)
            {
                continue;
            }

            var actualOffset = match.Index * 2;

            var hi = new HitInfo(actualOffset, match.Value.Trim());
            hits.Add(hi);
        }

        return hits;
    }

    public List<Attribute> Attributes { get; }

    /// <summary>
    ///     Problems found while parsing the record: a header or fixup array that does not fit, attributes with bad
    ///     sizes, attributes that could not be parsed or of unknown types. The attributes that could be read are kept.
    /// </summary>
    public List<string> ParseErrors => _parseErrors ??= new List<string>();

    /// <summary>True when <see cref="ParseErrors" /> has anything (without creating the list, as reading it does)</summary>
    public bool HasParseErrors => _parseErrors is { Count: > 0 };

    /// <summary>Built when read: a loaded $MFT keeps only the raw fixup array of each record</summary>
    public FixupData FixupData => new(_fixupRaw ?? NoFixups);

    public bool IsBad { get; }
    public bool IsUninitialized { get; }

    public long Offset { get; }
    public uint EntryNumber { get; }
    public short FirstAttributeOffset { get; }
    public int ActualRecordSize { get; }
    public int AllocatedRecordSize { get; }
    public MftEntryInfo MftRecordToBaseRecord { get; }
    public short FirstAvailablAttribueId { get; }
    public short ReferenceCount { get; }
    public ushort SequenceNumber { get; }
    public EntryFlag EntryFlags { get; }
    public long LogSequenceNumber { get; }
    public short FixupEntryCount { get; }
    public short FixupOffset { get; }
    public bool FixupOk { get; }

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine(
            $"Entry-seq #: 0x{EntryNumber:X}-0x{SequenceNumber:X}, Offset: 0x{Offset:X}, Flags: {EntryFlags.ToString().Replace(", ", "|")}, Log Sequence #: 0x{LogSequenceNumber:X}, Mft Record To Base Record: {MftRecordToBaseRecord}\r\nReference Count: 0x{ReferenceCount:X}, Fixup Data: {FixupData} (Fixup OK: {FixupOk})\r\n");

        foreach (var attribute in Attributes)
        {
            sb.AppendLine(attribute.ToString());
        }

        return sb.ToString();
    }
}

public class HitInfo
{
    public HitInfo(int offset, string hit)
    {
        Offset = offset;
        Hit = hit;
    }

    public int Offset { get; set; }
    public string Hit { get; set; }

    public override string ToString()
    {
        return $"0x{Offset:X}: {Hit}";
    }
}