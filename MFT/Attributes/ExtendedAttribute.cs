using System;
using System.Text;

namespace MFT.Attributes
{
    public class ExtendedAttribute : Attribute
    {
        public ExtendedAttribute(byte[] rawBytes) : base(rawBytes)
        {
            var content = new byte[AttributeContentLength];

            Buffer.BlockCopy(rawBytes, ContentOffset, content, 0, AttributeContentLength);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendLine("**** " + GetType().Name.ToUpperInvariant() + " ****");

            sb.AppendLine(base.ToString());

            sb.AppendLine();
            sb.AppendLine("ExtendedAttribute (do you need raw bytes?)");

            return sb.ToString();
        }
    }
}