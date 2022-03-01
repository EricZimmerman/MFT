using System;
using System.Collections.Generic;
using System.Text;
using MFT.Other;

namespace MFT.Attributes;

public class IndexRoot : Attribute
{
    public enum CollationTypes
    {
        Binary = 0x000000,
        Filename = 0x000001,
        Unicode = 0x000002,
        NtOfsUlong = 0x000010,
        NtOfsSid = 0x000011,
        NtOfsSecurityHash = 0x000012,
        NtOfsUlongs = 0x000013
    }

    [Flags]
    public enum IndexFlag
    {
        HasSubNode = 0x001,
        IsLast = 0x002
    }

    public IndexRoot(byte[] rawBytes) : base(rawBytes)
    {
        var index = (int)ContentOffset;

        IndexedAttributeType = (AttributeType)BitConverter.ToInt32(rawBytes, index);
        index += 4;

        CollationType = (CollationTypes)BitConverter.ToInt32(rawBytes, index);
        index += 4;

        EntrySize = BitConverter.ToInt32(rawBytes, index);
        index += 4;

        NumberClusterBlocks = BitConverter.ToInt32(rawBytes, index);
        index += 4;

        OffsetToFirstIndexEntry = BitConverter.ToInt32(rawBytes, index);
        index += 4;
        TotalSizeOfIndexEntries = BitConverter.ToInt32(rawBytes, index);
        index += 4;
        AllocatedSizeOfEntries = BitConverter.ToInt32(rawBytes, index);
        index += 4;

        Flags = (IndexFlag)rawBytes[index];
        index += 1;


        index += 3; //padding

        //TODO verify this
        var mftInfoBytes = new byte[8];
        Buffer.BlockCopy(rawBytes, index, mftInfoBytes, 0, 8);
        index += 8;

        MftRecord = new MftEntryInfo(mftInfoBytes);
        //end verify

        IndexEntries = new List<IndexEntry>();

        while (index < rawBytes.Length)
        {
            var indexValSize = BitConverter.ToInt16(rawBytes, index);

            if (indexValSize == 0x10)
                //indicates no more index entries
            {
                break;
            }

            if (indexValSize > rawBytes.Length - index)
            {
                indexValSize = (short)(rawBytes.Length - index);
            }

            var buff = new byte[indexValSize];
            Buffer.BlockCopy(rawBytes, index, buff, 0, indexValSize);

            var ie = new IndexEntry(buff);

            IndexEntries.Add(ie);

            index += indexValSize;
        }
    }

    public IndexFlag Flags { get; }
    public int TotalSizeOfIndexEntries { get; }
    public int AllocatedSizeOfEntries { get; }
    public int OffsetToFirstIndexEntry { get; }
    public List<IndexEntry> IndexEntries { get; }
    public MftEntryInfo MftRecord { get; }
    public AttributeType IndexedAttributeType { get; }
    public int EntrySize { get; }
    public int NumberClusterBlocks { get; }


    public CollationTypes CollationType { get; }


    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine("**** INDEX ROOT ****");

        sb.AppendLine(base.ToString());

        sb.AppendLine();

        sb.AppendLine(
            $"Indexed Attribute Type: {IndexedAttributeType} Entry Size: 0x{EntrySize:X} Number Cluster Blocks: 0x{NumberClusterBlocks:X} Collation Type: {CollationType} Index entries count: 0x{IndexEntries.Count:X} Mft Record: {MftRecord}");

        sb.AppendLine();
        sb.AppendLine("FileInfo Records Entries");

        foreach (var ie in IndexEntries)
        {
            sb.AppendLine(ie.ToString());
        }

        return sb.ToString();
    }
}