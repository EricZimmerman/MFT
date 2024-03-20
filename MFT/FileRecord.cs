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

    public FileRecord(byte[] rawBytes, int offset, bool recoverFromSlack)
    {
        Offset = offset;

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

        //to build fixup info, take FixupEntryCount x 2 bytes as each are 2 bytes long
        var fixupTotalLength = FixupEntryCount * 2;

        var fixupBuffer = new byte[fixupTotalLength];
        Buffer.BlockCopy(rawBytes, FixupOffset, fixupBuffer, 0, fixupTotalLength);

        //pull this early so we can check if its free in our fix up value messages
        EntryFlags = (EntryFlag)BitConverter.ToInt16(rawBytes, 0x16);

        FixupData = new FixupData(fixupBuffer);

        FixupOk = true;

        //fixup verification
        var counter = 512;
        foreach (var bytese in FixupData.FixupActual)
        {
            //adjust the offset to where we need to check
            var fixupOffset = counter - 2;

            var expected = BitConverter.ToInt16(rawBytes, fixupOffset);
            if (expected != FixupData.FixupExpected && EntryFlags != 0x0)
            {
                FixupOk = false;
                Log.Warning(
                    "Offset: 0x{Offset:X} Entry/seq: 0x{EntryNumber:X}/0x{SequenceNumber:X} Fixup values do not match at 0x{FixupOffset:X}. Expected: 0x{FixupExpected:X2}, actual: 0x{Expected:X2}",
                    Offset, EntryNumber, SequenceNumber, fixupOffset, FixupData.FixupExpected, expected);
            }

            //replace fixup expected with actual bytes. bytese has actual replacement values in it.
            Buffer.BlockCopy(bytese, 0, rawBytes, fixupOffset, 2);

            counter += 512;
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

        EntryNumber = BitConverter.ToUInt32(rawBytes, 0x2c);

        Log.Debug("FILE record entry/seq #: 0x{EntryNumber:X}/{SequenceNumber:X}", EntryNumber, SequenceNumber);

       
        //start attribute processing at FirstAttributeOffset

        var index = (int)FirstAttributeOffset;

        while (index < ActualRecordSize)
        {
            var attrType = (AttributeType)BitConverter.ToInt32(rawBytes, index);

            var attrSize = BitConverter.ToInt32(rawBytes, index + 4);

            if (attrSize == 0 || attrType == AttributeType.EndOfAttributes)
            {
                index += 8; //skip -1 type and 0 size
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

            switch (attrType)
            {
                case AttributeType.StandardInformation:
                    var si = new StandardInfo(rawAttr);
                    Attributes.Add(si);
                    break;

                case AttributeType.FileName:
                    var fi = new FileName(rawAttr);
                    Attributes.Add(fi);
                    break;

                case AttributeType.Data:
                    var d = new Data(rawAttr);
                    Attributes.Add(d);
                    break;

                case AttributeType.IndexAllocation:
                    var ia = new IndexAllocation(rawAttr);
                    Attributes.Add(ia);
                    break;

                case AttributeType.IndexRoot:
                    var ir = new IndexRoot(rawAttr);
                    Attributes.Add(ir);
                    break;

                case AttributeType.Bitmap:
                    var bm = new Bitmap(rawAttr);
                    Attributes.Add(bm);
                    break;

                case AttributeType.VolumeVersionObjectId:
                    var oi = new ObjectId_(rawAttr);
                    Attributes.Add(oi);
                    break;

                case AttributeType.SecurityDescriptor:
                    var sd = new SecurityDescriptor(rawAttr);
                    Attributes.Add(sd);
                    break;

                case AttributeType.VolumeName:
                    var vn = new VolumeName(rawAttr);
                    Attributes.Add(vn);
                    break;

                case AttributeType.VolumeInformation:
                    var vi = new VolumeInformation(rawAttr);
                    Attributes.Add(vi);
                    break;

                case AttributeType.LoggedUtilityStream:
                    var lus = new LoggedUtilityStream(rawAttr);
                    Attributes.Add(lus);
                    break;

                case AttributeType.ReparsePoint:
                    try
                    {
                        var rp = new ReparsePoint(rawAttr);

                        Attributes.Add(rp);
                    }
                    catch (Exception e)
                    {
                        Log.Error(e,
                            "There was an error parsing a ReparsePoint in FILE record at offset 0x{Offset:X}. Please extract via --dd and --do and send to saericzimmerman@gmail.com",
                            Offset);
                    }

                    break;

                case AttributeType.AttributeList:
                    var al = new AttributeList(rawAttr);
                    Attributes.Add(al);
                    break;

                case AttributeType.Ea:
                    var ea = new ExtendedAttribute(rawAttr);
                    Attributes.Add(ea);
                    break;

                case AttributeType.EaInformation:
                    var eai = new ExtendedAttributeInformation(rawAttr);
                    Attributes.Add(eai);
                    break;

                default:
                    throw new Exception($"Add me: {attrType} (0x{attrType:X})");
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


    private static List<HitInfo> GetUnicodeHits(byte[] bytes)
    {
        var maxString = "";
        var mi2 = $"{"{"}{3}{","}{maxString}{"}"}";

        var uniRange = "[\u0020-\u007E]";
        var regUni = new Regex($"{uniRange}{mi2}", RegexOptions.Compiled);
        var uniString = Encoding.Unicode.GetString(bytes);

        var hits = new List<HitInfo>();

        foreach (Match match in regUni.Matches(uniString))
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

    public FixupData FixupData { get; }

    public bool IsBad { get; }
    public bool IsUninitialized { get; }

    public int Offset { get; }
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