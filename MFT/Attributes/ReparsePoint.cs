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
            ReservedTwo,
            DriverExtender,
            HierarchicalStorageManager2,
            SisFilterDriver,
            DistributedFileSystem,
            FilterManagerTestHarness,
            DistributedFileSystemR,
            MountPoint,
            SymbolicLink,
            Wim,
            Csv,
            HierarchicalStorageManager,
            DeDupe,
            Nfs,
            FilePlaceHolder,
            Wof,
            Wci,
            GlobalReparse,
            AppExeCLink,
            Hfs,
            Unhandled,
            OneDrive,
            Cloud,
            CloudRoot,
            CloudOnDemand,
            CloudRootOnDemand,
            Gvfs,
            IisCache,
            LxSymLink,
            WciTombstone,
            GvfsTombstone,
            AppXStrim
        }

        public ReparsePoint(byte[] rawBytes) : base(rawBytes)
        {
            if (AttributeContentLength == 0 || AttributeContentLength == 8)
            {
                SubstituteName = string.Empty;
                PrintName = string.Empty;
             
                return;
            }

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
                case 0x00000002:
                    Tag = ReparsePointTag.ReservedTwo;
                    break;
                case 0x80000005:
                    Tag = ReparsePointTag.DriverExtender;
                    break;
                case 0x80000006:
                    Tag = ReparsePointTag.HierarchicalStorageManager2;
                    break;
                case 0x80000007:
                    Tag = ReparsePointTag.SisFilterDriver;
                    break;
                case 0x80000008:
                    Tag = ReparsePointTag.Wim;
                    break;
                case 0x80000009:
                    Tag = ReparsePointTag.Csv;
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
                case 0x80000013:
                    Tag = ReparsePointTag.DeDupe;
                    break;
                case 0x80000014:
                    Tag = ReparsePointTag.Nfs;
                    break;
                case 0x80000015:
                    Tag = ReparsePointTag.FilePlaceHolder;
                    break;

                case 0x80000017:
                    Tag = ReparsePointTag.Wof;
                    break;
                case 0x80000018:
                    Tag = ReparsePointTag.Wci;
                    break;
                case 0x80000019:
                    Tag = ReparsePointTag.GlobalReparse;
                    break;
                case 0x8000001B:
                    Tag = ReparsePointTag.AppExeCLink;
                    break;
                case 0x8000001E:
                    Tag = ReparsePointTag.Hfs;
                    break;
                case 0x80000020:
                    Tag = ReparsePointTag.Unhandled;
                    break;
                case 0x80000021:
                    Tag = ReparsePointTag.OneDrive;
                    break;
                case 0x9000001A:
                    Tag = ReparsePointTag.Cloud;
                    break;
                case 0x9000101A:
                    Tag = ReparsePointTag.CloudRoot;
                    break;
                case 0x9000201A:
                    Tag = ReparsePointTag.CloudOnDemand;
                    break;

                case 0x9000301A:
                    Tag = ReparsePointTag.CloudRootOnDemand;
                    break;
                case 0x9000001C:
                    Tag = ReparsePointTag.Gvfs;
                    break;
                case 0xA0000010:
                    Tag = ReparsePointTag.IisCache;
                    break;
                case 0xA0000019:
                    Tag = ReparsePointTag.GlobalReparse;
                    break;
                case 0xA000001D:
                    Tag = ReparsePointTag.LxSymLink;
                    break;
                case 0xA000001F:
                    Tag = ReparsePointTag.WciTombstone;
                    break;

                case 0xA0000022:
                    Tag = ReparsePointTag.GvfsTombstone;
                    break;

                case 0xC0000014:
                    Tag = ReparsePointTag.AppXStrim;
                    break;
            }

            var subNameOffset = BitConverter.ToInt16(content, 8);
            var subNameSize = BitConverter.ToInt16(content, 10);
            var printNameOffset = BitConverter.ToInt16(content, 12);
            var printNameSize = BitConverter.ToInt16(content, 14);

            SubstituteName = string.Empty;
            PrintName = string.Empty;

            if (Tag != ReparsePointTag.SymbolicLink && Tag != ReparsePointTag.MountPoint)
            {
                return;
            }

            if (subNameSize > 0)
            {
                if (subNameOffset == 0)
                {
                    subNameOffset = 0x10;
                }
                else
                {
                    subNameOffset = 0x14;
                }

                SubstituteName = Encoding.Unicode.GetString(content, subNameOffset, subNameSize);
            }

            if (printNameSize > 0)
            {
                if (printNameOffset == 0)
                {
                    printNameOffset = (short) (subNameOffset + subNameSize);
                }
                else
                {
                    printNameOffset = (short) (subNameOffset + printNameOffset);
                }


                PrintName = Encoding.Unicode.GetString(content, printNameOffset, printNameSize);
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