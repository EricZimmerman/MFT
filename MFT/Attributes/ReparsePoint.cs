using System;
using System.Text;

namespace MFT.Attributes
{
    public class ReparsePoint : Attribute
    {
        public enum ReparsePointTag
        {
            ReservedZero,
            ReservedOne,
            DriverExtender,
            HierarchicalStorageManager2,
            SISFilterDriver,
            DistributedFileSystem,
            FilterManagerTestHarness,
            DistributedFileSystemR,
            MountPoint,
            SymbolicLink,
            HierarchicalStorageManager
        }

        public ReparsePoint(byte[] rawBytes) : base(rawBytes)
        {
            var content = new byte[AttributeContentLength];

            Buffer.BlockCopy(rawBytes, ContentOffset, content, 0, AttributeContentLength);

            var tag = BitConverter.ToUInt32(content, 0);

            switch (tag)
            {
                case 0x00000000:
                    Tag = ReparsePointTag.ReservedZero;
                    break;
                case 0x00000001:
                    Tag = ReparsePointTag.ReservedOne;
                    break;
                case 0x80000005:
                    Tag = ReparsePointTag.DriverExtender;
                    break;
                case 0x80000006:
                    Tag = ReparsePointTag.HierarchicalStorageManager2;
                    break;
                case 0x80000007:
                    Tag = ReparsePointTag.SISFilterDriver;
                    break;
                case 0x8000000a:
                    Tag = ReparsePointTag.DistributedFileSystem;
                    break;
                case 0x8000000b:
                    Tag = ReparsePointTag.FilterManagerTestHarness;
                    break;
                case 0x80000012:
                    Tag = ReparsePointTag.DistributedFileSystemR;
                    break;
                case 0xa0000003:
                    Tag = ReparsePointTag.MountPoint;
                    break;
                case 0xa000000c:
                    Tag = ReparsePointTag.SymbolicLink;
                    break;
                case 0xc0000004:
                    Tag = ReparsePointTag.HierarchicalStorageManager;
                    break;
            }

            var subNameOffset = BitConverter.ToInt16(content, 8);
            var subNameSize = BitConverter.ToInt16(content, 10);
            var printNameOffset = BitConverter.ToInt16(content, 12);
            var printNameSize = BitConverter.ToInt16(content, 14);

            SubstituteName = string.Empty;
            PrintName = string.Empty;

            var baseOffset = 0x10;

            if (Tag != ReparsePointTag.SymbolicLink && Tag != ReparsePointTag.MountPoint)
            {
                return;
            }

            if (Tag == ReparsePointTag.SymbolicLink)
            {
                baseOffset += 2;
            }

            if (subNameSize > 0)
            {
                SubstituteName = Encoding.Unicode.GetString(content, baseOffset + subNameOffset, subNameSize);
            }

            if (printNameSize > 0)
            {
                PrintName = Encoding.Unicode.GetString(content, baseOffset + printNameOffset, printNameSize);
            }
        }

        public string SubstituteName { get; }
        public string PrintName { get; }
        public ReparsePointTag Tag { get; }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendLine("**** REPARSE POINT ****");

            sb.AppendLine(base.ToString());

            sb.AppendLine();

            sb.AppendLine($"Substitute Name: {SubstituteName} Print Name: {PrintName} Tag: {Tag}");

            return sb.ToString();
        }
    }
}