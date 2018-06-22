using System;
using System.Text;

namespace MFT.Attributes
{
    public class ExtendedAttributeInformation : Attribute
    {
        public ExtendedAttributeInformation(byte[] rawBytes) : base(rawBytes)
        {
            var content = new byte[AttributeContentLength];

            Buffer.BlockCopy(rawBytes, ContentOffset, content, 0, AttributeContentLength);

            EaSize = BitConverter.ToInt16(content, 0);
            NumberOfExtendedAttrWithNeedEaSet = BitConverter.ToInt16(content, 2);
            SizeOfEaData = BitConverter.ToInt32(content, 4);
        }

        public short EaSize { get; }
        public short NumberOfExtendedAttrWithNeedEaSet { get; }
        public int SizeOfEaData { get; }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendLine("**** EXTENDED ATTRIBUTE INFORMATION ****");

            sb.AppendLine(base.ToString());

            sb.AppendLine();
            sb.AppendLine("Extended Attribute Information");

            sb.AppendLine(
                $"Ea Size: 0x{EaSize:X}, Number Of Extended Attrributes With Need Ea Set: 0x{NumberOfExtendedAttrWithNeedEaSet:X} Size Of Ea Data: 0x{SizeOfEaData:X} ");

            return sb.ToString();
        }
    }
}