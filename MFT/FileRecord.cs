using System;
using System.Collections.Generic;
using System.Text;
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

    public FileRecord(byte[] rawBytes, int offset)
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

                if (index != ActualRecordSize)
                {
                    Log.Warning("Slack space found in entry/seq: 0x{EntryNumber:X}/0x{SequenceNumber:X}", EntryNumber,
                        SequenceNumber);
                }

                //TODO process slack here?
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

        //rest is slack. handle here?
        Log.Verbose("Slack starts at 0x{Index:X} Absolute offset: 0x{Offset:X}", index, index + offset);
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