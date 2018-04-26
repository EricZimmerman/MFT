using System;
using System.Text;

namespace MFT.Attributes
{
    public class ReparsePoint : Attribute
    {
        public enum ReparsePointTag
        {
            ReservedZero_0x00000000,
            ReservedOne_0x00000001,
            DriverExtender_0x80000005,
            HierarchicalStorageManager2_0x80000006,
            SISFilterDriver_0x80000007,
            DistributedFileSystem_0x8000000a,
            FilterManagerTestHarness_0x8000000b,
            DistributedFileSystemR_0x80000012,
            MountPoint_0xa0000003,
            SymbolicLink_0xa000000c,
            HierarchicalStorageManager_0xc0000004
        }

        public ReparsePoint(byte[] rawBytes) : base(rawBytes)
        {
            var content = new byte[AttributeContentLength];

            Buffer.BlockCopy(rawBytes, ContentOffset, content, 0, AttributeContentLength);

            var tag = BitConverter.ToUInt32(content, 0);

            switch (tag)
            {
                case 0x00000000:
                    Tag = ReparsePointTag.ReservedZero_0x00000000;
                    break;
                case 0x00000001:
                    Tag = ReparsePointTag.ReservedOne_0x00000001;
                    break;
                case 0x80000005:
                    Tag = ReparsePointTag.DriverExtender_0x80000005;
                    break;
                case 0x80000006:
                    Tag = ReparsePointTag.HierarchicalStorageManager2_0x80000006;
                    break;
                case 0x80000007:
                    Tag = ReparsePointTag.SISFilterDriver_0x80000007;
                    break;
                case 0x8000000a:
                    Tag = ReparsePointTag.DistributedFileSystem_0x8000000a;
                    break;
                case 0x8000000b:
                    Tag = ReparsePointTag.FilterManagerTestHarness_0x8000000b;
                    break;
                case 0x80000012:
                    Tag = ReparsePointTag.DistributedFileSystemR_0x80000012;
                    break;
                case 0xa0000003:
                    Tag = ReparsePointTag.MountPoint_0xa0000003;
                    break;
                case 0xa000000c:
                    Tag = ReparsePointTag.SymbolicLink_0xa000000c;
                    break;
                case 0xc0000004:
                    Tag = ReparsePointTag.HierarchicalStorageManager_0xc0000004;
                    break;
            }

            var subNameOffset = BitConverter.ToInt16(content, 8);
            var subNameSize = BitConverter.ToInt16(content, 10);
            var printNameOffset = BitConverter.ToInt16(content, 12);
            var printNameSize = BitConverter.ToInt16(content, 14);

            SubstituteName = string.Empty;
            PrintName = string.Empty;

            var baseOffset = 0x10;

            if (Tag != ReparsePointTag.SymbolicLink_0xa000000c && Tag != ReparsePointTag.MountPoint_0xa0000003)
            {
                return;
            }

            if (Tag == ReparsePointTag.SymbolicLink_0xa000000c)
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

            sb.AppendLine("**** " + GetType().Name.ToUpperInvariant() + " ****");

            sb.AppendLine(base.ToString());

            sb.AppendLine();

            sb.AppendLine($"SubstituteName: {SubstituteName} PrintName: {PrintName} Tag: {Tag}");

            return sb.ToString();
        }
    }
}