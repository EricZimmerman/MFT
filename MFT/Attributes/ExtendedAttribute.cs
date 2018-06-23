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

            var asAscii = Encoding.GetEncoding(1252).GetString(Content);
            var asUnicode = Encoding.Unicode.GetString(Content);
           

            sb.AppendLine();
            sb.AppendLine($"Extended Attribute:: {BitConverter.ToString(Content)}\r\n\r\nASCII: {asAscii}\r\nUnicode: {asUnicode}");

            return sb.ToString();
        }
    }
}