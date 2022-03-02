using System.IO;
using System.Linq;

namespace MFT.Other;

public class IndexEntryI30
{
        public enum OEntryFlag
        {
            HasSubNodes = 0x1,
            LastEntry = 0x2
        }

        public IndexEntryI30(byte[] rawBytes, long absoluteOffset, int pageNumber, bool fromSlack)
        {
            PageNumber = pageNumber;
            FromSlack = fromSlack;

            AbsoluteOffset = absoluteOffset;

            using var br = new BinaryReader(new MemoryStream(rawBytes));

            var skipOffset = 0;
            if (fromSlack == false)
            {
                MftReferenceSelf = new MftEntryInfo(br.ReadBytes(8));

                if (MftReferenceSelf.MftEntryNumber == 0)
                {
                    return;
                }

                var indexEntrySize = br.ReadInt16();
                var indexDataSize = br.ReadInt16();
                Flag = (OEntryFlag)br.ReadInt32();
                skipOffset = 8 + 2 + 2 + 4;
            }

            FileInfo = new Attributes.FileInfo(rawBytes.Skip(skipOffset).ToArray());
        }

        public OEntryFlag Flag { get; }

        public int PageNumber { get; }
        public bool FromSlack { get; }

        public long AbsoluteOffset { get; }

        public MftEntryInfo MftReferenceSelf { get; }

        public Attributes.FileInfo FileInfo { get; }
    
        public string Md5 { get; set; }


        public override string ToString()
        {
            return
                $"Absolute offset: 0x{AbsoluteOffset:X} FromSlack: {FromSlack} Self MFT: {MftReferenceSelf} FileInfo: {FileInfo}";
        }
    
}