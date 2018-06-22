using System;
using System.Text;

namespace MFT.Attributes
{
    public class ExtendedAttribute : Attribute
    {
        public ExtendedAttribute(byte[] rawBytes) : base(rawBytes)
        {
            Content = new byte[AttributeContentLength];

            Buffer.BlockCopy(rawBytes, ContentOffset, Content, 0, AttributeContentLength);
        }

        public byte[] Content { get; }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendLine("**** EXTENDED ATTRIBUTE ****");

            sb.AppendLine(base.ToString());

            sb.AppendLine();
            sb.AppendLine($"Extended Attribute: {BitConverter.ToString(Content)}");

            return sb.ToString();
        }
    }
}