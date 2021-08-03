using System;
using System.Collections.Generic;
using System.Text;
using MFT.Other;

namespace MFT.Attributes
{
    public class NonResidentData
    {
        public NonResidentData(byte[] rawBytes)
        {
            StartingVirtualClusterNumber = BitConverter.ToUInt64(rawBytes, 0x10);
            EndingVirtualClusterNumber = BitConverter.ToUInt64(rawBytes, 0x18);

            var offsetToDataRuns = BitConverter.ToUInt16(rawBytes, 0x20);

            AllocatedSize = BitConverter.ToUInt64(rawBytes, 0x28);
            ActualSize = BitConverter.ToUInt64(rawBytes, 0x30);
            InitializedSize = BitConverter.ToUInt64(rawBytes, 0x38);

            var index = (int) offsetToDataRuns; //set index into bytes to start reading offsets

            DataRuns = new List<DataRun>();

            var drStart = rawBytes[index];

            while (drStart != 0)
            {
                var offsetLength = (byte) ((drStart & 0xF0) >> 4); //left nibble
                var clusterLenByteCount = (byte) (drStart & 0x0F); //right nibble
                index += 1;

                var runLenBytes = new byte[8]; //length should never exceed 8, so start with 8
                Buffer.BlockCopy(rawBytes, index, runLenBytes, 0, clusterLenByteCount);

                index += clusterLenByteCount;

                var clusterRunLength = BitConverter.ToUInt64(runLenBytes, 0);

                var clusterBytes = new byte[8]; //length should never exceed 8, so start with 8

                //copy in what we have
                Buffer.BlockCopy(rawBytes, index, clusterBytes, 0, offsetLength);
                
                //negative offsets
                if (offsetLength > 0)
                {
                    if (clusterBytes[offsetLength - 1] >= 0x80)
                    {
                        for (int i = offsetLength; i < clusterBytes.Length; i++)
                            clusterBytes[i] = 0xFF;
                    }
                }
   
                //we can safely get our cluster #
                var clusterNumber = BitConverter.ToInt64(clusterBytes, 0);

                index += offsetLength;

                var dr = new DataRun(clusterRunLength, clusterNumber);
                DataRuns.Add(dr);

                drStart = rawBytes[index];
            }
        }

        public ulong StartingVirtualClusterNumber { get; }
        public ulong EndingVirtualClusterNumber { get; }
        public ulong AllocatedSize { get; }
        public ulong ActualSize { get; }
        public ulong InitializedSize { get; }
        public List<DataRun> DataRuns { get; }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendLine();
            sb.AppendLine(
                $"Starting Virtual Cluster #: 0x{StartingVirtualClusterNumber:X}, Ending Virtual Cluster #: 0x{EndingVirtualClusterNumber:X}, Allocated Size: 0x{AllocatedSize:X}, Actual Size: 0x{ActualSize:X}, Initialized Size: 0x{InitializedSize:X} ");

            sb.AppendLine();
            sb.AppendLine("DataRuns Entries");

            foreach (var dataRun in DataRuns)
            {
                sb.AppendLine(dataRun.ToString());
            }

            return sb.ToString();
        }
    }
}
