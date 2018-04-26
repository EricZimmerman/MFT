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

            sb.AppendLine("**** " + GetType().Name.ToUpperInvariant() + " ****");

            sb.AppendLine(base.ToString());

            sb.AppendLine();
            sb.AppendLine("ExtendedAttributeInformation");

            sb.AppendLine(
                $"EaSize: {EaSize} NumberOfExtendedAttrWithNeedEaSet: {NumberOfExtendedAttrWithNeedEaSet} SizeOfEaData: {SizeOfEaData} ");

            return sb.ToString();
        }
    }
}