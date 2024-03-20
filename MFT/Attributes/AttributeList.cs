using System;
using System.Collections.Generic;
using System.Text;
using MFT.Other;
using Serilog;

namespace MFT.Attributes;

public class AttributeList : Attribute
{
    public AttributeList(byte[] rawBytes) : base(rawBytes)
    {
        DataRuns = new List<DataRun>();
        AttributeInformations = new List<AttributeInfo>();

        //TODO Refactor using the NonResident and ResidentData classes?

        if (IsResident)
        {
            var index = ContentOffset;

            while (index < rawBytes.Length)
            {
                var size = BitConverter.ToInt16(rawBytes, index + 4);

                if (size < rawBytes.Length - index)
                {
                    Log.Debug("Not enough data to process attribute list. {Msg}","Skipping remaining bytes in attribute list");
                    break;
                }
                
                var buffer = new byte[size];
                Buffer.BlockCopy(rawBytes, index, buffer, 0, size);

                var er = new AttributeInfo(buffer);

                AttributeInformations.Add(er);

                index += size;
            }
        }
        else
        {
            StartingVirtualCluster = BitConverter.ToUInt64(rawBytes, 0x10);
            EndingVirtualCluster = BitConverter.ToUInt64(rawBytes, 0x18);
            OffsetToDataRun = BitConverter.ToUInt16(rawBytes, 0x20);
            AllocatedSize = BitConverter.ToUInt64(rawBytes, 0x28);
            ActualSize = BitConverter.ToUInt64(rawBytes, 0x30);
            InitializedSize = BitConverter.ToUInt64(rawBytes, 0x38);

            var index = OffsetToDataRun;

            var hasAnother = rawBytes[index] > 0;

            //when data is split across several entries, must find them all and process in order of 
            //StartingVCN: 0x0 EndingVCN: 0x57C
            //StartingVCN: 0x57D EndingVCN: 0x138F
            //so things go back in right order

            //TODO this should be a function vs here and in Data class.

            while (hasAnother)
            {
                var drStart = rawBytes[index];
                index += 1;

                var clustersToReadAtOffset = (byte)(drStart & 0x0F);
                var offsetToRun = (byte)((drStart & 0xF0) >> 4);

                var clusterCountRaw = new byte[8];
                var offsetToRunRaw = new byte[8];

                Buffer.BlockCopy(rawBytes, index, clusterCountRaw, 0, clustersToReadAtOffset);

                index += clustersToReadAtOffset;
                Buffer.BlockCopy(rawBytes, index, offsetToRunRaw, 0, offsetToRun);
                index += offsetToRun;

                var clusterCount = BitConverter.ToUInt64(clusterCountRaw, 0);
                var offset = BitConverter.ToInt64(offsetToRunRaw, 0);

                var dr = new DataRun(clusterCount, offset);
                DataRuns.Add(dr);

                hasAnother = rawBytes[index] > 0;
            }
        }
    }

    public ushort OffsetToDataRun { get; }
    public ulong StartingVirtualCluster { get; }
    public ulong EndingVirtualCluster { get; }
    public ulong AllocatedSize { get; }
    public ulong ActualSize { get; }
    public ulong InitializedSize { get; }

    /// <summary>
    ///     Contains cluster where the actual data lives when it is non-resident
    /// </summary>
    public List<DataRun> DataRuns { get; }

    public List<AttributeInfo> AttributeInformations { get; }

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine("**** ATTRIBUTE LIST ****");

        sb.AppendLine(base.ToString());

        sb.AppendLine();

        sb.AppendLine(
            $"DataRuns: {string.Join("\r\n", DataRuns)}\r\nAttribute Infos: {string.Join("\r\n", AttributeInformations)}");

        return sb.ToString();
    }
}