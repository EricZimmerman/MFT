using System;
using System.Text;

namespace MFT.Attributes
{
    public class Data : Attribute
    {
        public Data(byte[] rawBytes) : base(rawBytes)
        {
            if (IsResident)
            {
                var content = new byte[AttributeContentLength];

                Buffer.BlockCopy(rawBytes, ContentOffset, content, 0, AttributeContentLength);

                ResidentData = new ResidentData(content);
            }
            else
            {
                NonResidentData = new NonResidentData(rawBytes);
            }
        }

        public ResidentData ResidentData { get; }
        public NonResidentData NonResidentData { get; }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendLine("**** " + GetType().Name.ToUpperInvariant() + " ****");

            sb.AppendLine(base.ToString());

            sb.AppendLine();

            if (ResidentData == null)
            {
                sb.AppendLine("NonResidentData");
                sb.AppendLine(NonResidentData.ToString());
            }
            else
            {
                sb.AppendLine("ResidentData");
                sb.AppendLine(ResidentData.ToString());
            }

            return sb.ToString();
        }
    }
}

//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Text;
//using MFT.Other;
//
//namespace MFT.Attributes
//{
//    public class Data : Attribute
//    {
//        public ushort OffsetToDataRun { get; }
//        public ulong StartingVirtualCluster { get; }
//        public ulong EndingVirtualCluster { get; }
//        public ulong AllocatedSize { get; }
//        public ulong ActualSize { get; }
//        public ulong InitializedSize { get; }
//
//        public Data(byte[] rawBytes) : base(rawBytes)
//        {
//            DataRuns = new List<DataRun>();
//
//            if (NameSize > 0)
//            {
//               Name = Encoding.Unicode.GetString(rawBytes, NameOffset, NameSize * 2);
//            }
//
//            if (IsResident)
//            {
//                Content = new byte[AttributeContentLength];
//                Buffer.BlockCopy(rawBytes,ContentOffset,Content,0,AttributeContentLength);
//            }
//            else
//            {
//                StartingVirtualCluster = BitConverter.ToUInt64(rawBytes, 0x10);
//                EndingVirtualCluster = BitConverter.ToUInt64(rawBytes, 0x18);
//                OffsetToDataRun = BitConverter.ToUInt16(rawBytes, 0x20);
//                AllocatedSize = BitConverter.ToUInt64(rawBytes, 0x28);
//                ActualSize = BitConverter.ToUInt64(rawBytes, 0x30);
//                InitializedSize = BitConverter.ToUInt64(rawBytes, 0x38);
//            
//                var index = OffsetToDataRun;
//
//                var hasAnother = rawBytes[index] > 0;
//
//
//                //when data is split across several entries, must find them all and process in order of 
//                //StartingVCN: 0x0 EndingVCN: 0x57C
//                //StartingVCN: 0x57D EndingVCN: 0x138F
//                //so things go back in right order
//
//
//                while (hasAnother)
//                {
//                    var drStart = rawBytes[index];
//                    index += 1;
//
//                    var clustersToReadAtOffset= (byte)(drStart & 0x0F);
//                    var offsetToRun = (byte)((drStart & 0xF0) >> 4);
//
//                    var clusterCountRaw = new byte[8];
//                    var offsetToRunRaw = new byte[8];
//
//                    Buffer.BlockCopy(rawBytes,index,clusterCountRaw,0,clustersToReadAtOffset);
//
//                    index += clustersToReadAtOffset;
//                    Buffer.BlockCopy(rawBytes,index, offsetToRunRaw, 0, offsetToRun);
//                    index += offsetToRun;
//
//                    var clusterCount = BitConverter.ToInt64(clusterCountRaw, 0);
//                    var offset = BitConverter.ToInt64(offsetToRunRaw, 0);
//
//                    var dr = new DataRun(clusterCount,offset);
//                    DataRuns.Add(dr);
//
//                    hasAnother = rawBytes[index] >0;
//
//                   
//                }
//
//            }
//
//    }
//
//        public override string ToString()
//        {
//            var dr = string.Join("|", DataRuns);
//
//            return
//                $"{base.ToString()} Name: {Name} StartingVCN: 0x{StartingVirtualCluster:X} EndingVCN: 0x{EndingVirtualCluster:X} Allocated: 0x{AllocatedSize:X} Actual size: 0x{ActualSize:X} Init size: 0x{InitializedSize:X} Dataruns: {dr}";
//
//        }
//
//        public string Name { get; }
//
//        public List<DataRun> DataRuns { get; }
//        /// <summary>
//        /// When the file is resident, the bytes that make up the content of the file or ADS
//        /// </summary>
//        public byte[] Content { get; }
//    }
//}