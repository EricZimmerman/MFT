using System;
using System.Text;
using MFT.Attributes;
using Serilog;

namespace MFT.Other;

public class IndexEntry
{
    /// <param name="rawBytes">The entry from its length field on (8 bytes in, after the file reference)</param>
    public IndexEntry(byte[] rawBytes) : this(rawBytes, null)
    {
    }

    private IndexEntry(byte[] rawBytes, MftEntryInfo mftReference)
    {
        MftReference = mftReference;

        var index = 0;
        // var size = BitConverter.ToInt16(rawBytes, index);
        index += 2;

        var indexKeyDataSize = BitConverter.ToInt16(rawBytes, index);
        index += 2;

        var indexFlags = (IndexRoot.IndexFlag)BitConverter.ToInt32(rawBytes, index);
        index += 4;

        if ((indexFlags & IndexRoot.IndexFlag.IsLast) == IndexRoot.IndexFlag.IsLast)
        {
            return;
        }

        if (indexKeyDataSize == 0x10)
            //indicates no more index entries
        {
            return;
        }

        if (indexKeyDataSize <= 0x40)
            //too small to do anything with
        {
            return;
        }

        if (indexKeyDataSize > 0)
        {
            var mftInfoBytes = new byte[8];
            Buffer.BlockCopy(rawBytes, index, mftInfoBytes, 0, 8);
            index += 8;

            ParentMftRecord = new MftEntryInfo(mftInfoBytes);

            _createdOn = FileTimes.Check(BitConverter.ToInt64(rawBytes, index), "Invalid CreatedOn timestamp. Enable --debug for more details");

            index += 8;

            _contentModifiedOn = FileTimes.Check(BitConverter.ToInt64(rawBytes, index), "Invalid ContentModifiedOn timestamp. Enable --debug for more details");

            index += 8;

            _recordModifiedOn = FileTimes.Check(BitConverter.ToInt64(rawBytes, index), "Invalid RecordModifiedOn timestamp. Enable --debug for more details");

            index += 8;

            _lastAccessedOn = FileTimes.Check(BitConverter.ToInt64(rawBytes, index), "Invalid LastAccessedOn timestamp. Enable --debug for more details");

            index += 8;

            PhysicalSize = BitConverter.ToUInt64(rawBytes, index);
            index += 8;
            LogicalSize = BitConverter.ToUInt64(rawBytes, index);
            index += 8;

            Flags = (StandardInfo.Flag)BitConverter.ToInt32(rawBytes, index);
            index += 4;


            ReparseValue = BitConverter.ToInt32(rawBytes, index);
            index += 4;

            NameLength = rawBytes[index];
            index += 1;
            NameType = (NameTypes)rawBytes[index];
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
    private readonly long _createdOn;
    public DateTimeOffset? CreatedOn => FileTimes.ToDate(_createdOn);
    private readonly long _contentModifiedOn;
    public DateTimeOffset? ContentModifiedOn => FileTimes.ToDate(_contentModifiedOn);
    private readonly long _recordModifiedOn;
    public DateTimeOffset? RecordModifiedOn => FileTimes.ToDate(_recordModifiedOn);
    private readonly long _lastAccessedOn;
    public DateTimeOffset? LastAccessedOn => FileTimes.ToDate(_lastAccessedOn);
    public StandardInfo.Flag Flags { get; }

    public MftEntryInfo ParentMftRecord { get; }

    /// <summary>The file the entry is for (null when parsed without it, from the public constructor)</summary>
    public MftEntryInfo MftReference { get; }

    /// <summary>Parses a whole index entry: the 8 byte file reference, then the entry</summary>
    public static IndexEntry FromEntry(byte[] entry)
    {
        var reference = new byte[8];
        Buffer.BlockCopy(entry, 0, reference, 0, 8);

        var rest = new byte[entry.Length - 8];
        Buffer.BlockCopy(entry, 8, rest, 0, rest.Length);

        return new IndexEntry(rest, new MftEntryInfo(reference));
    }

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine();

        sb.AppendLine(
            $"File name: {FileName} (Len:0x{NameLength:X}) Flags: {Flags.ToString().Replace(", ", "|")}, Name Type: {NameType} " +
            $"Reparse Value: 0x{ReparseValue:X} Physical Size: 0x{PhysicalSize:X}, Logical Size: 0x{LogicalSize:X}" +
            $"\r\nMft Record: {MftReference} Parent Mft Record: {ParentMftRecord} " +
            $"\r\nCreated On:\t\t{CreatedOn?.ToString(MftFile.DateTimeFormat)}" +
            $"\r\nContent Modified On:\t{ContentModifiedOn?.ToString(MftFile.DateTimeFormat)}" +
            $"\r\nRecord Modified On:\t{RecordModifiedOn?.ToString(MftFile.DateTimeFormat)}" +
            $"\r\nLast Accessed On:\t{LastAccessedOn?.ToString(MftFile.DateTimeFormat)}");

        return sb.ToString();
    }
}