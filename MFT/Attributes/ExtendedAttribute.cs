using System;
using System.Diagnostics;
using System.Text;

namespace MFT.Attributes
{
    public class ExtendedAttribute : Attribute
    {
        public ExtendedAttribute(byte[] rawBytes) : base(rawBytes)
        {
            Content = new byte[AttributeContentLength];

            Buffer.BlockCopy(rawBytes, ContentOffset, Content, 0, AttributeContentLength);

            ProcessContent();
        }

        private void ProcessContent()
        {
            if (Content.Length == 0)
            {
                return;
            }

            var index = 0;

            while (index < Content.Length)
            {
                var nextOffset = BitConverter.ToUInt32(Content, index);
                if (nextOffset == 0)
                {
                    break;
                }
                index += 4;
                var flags = Content[index];
                index += 1;
                var nameLen = Content[index];
                index += 1;
                var eaLen = BitConverter.ToUInt16(Content, index);
                index += 2;

                var name = Encoding.GetEncoding(1252).GetString(Content, index, nameLen);

                Debug.WriteLine($"name: {name}");

                index += nameLen;
                index += 1; //null char

                while (index % 8 != 0)
                {
                    index += 1; //get to next 8 byte b oundary
                }

                if (name.Equals("LXATTRB"))
                {
                    index += 56;
                }

                if (name.Equals("LXXATTR"))
                {
                    index += 56;
                }

            }

//            ULONG  NextEntryOffset;
//            UCHAR  Flags;
//            UCHAR  EaNameLength;
//            USHORT EaValueLength;
//            CHAR   EaName[1];

            Debug.WriteLine(1);
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
            sb.AppendLine(
                $"Extended Attribute:: {BitConverter.ToString(Content)}\r\n\r\nASCII: {asAscii}\r\nUnicode: {asUnicode}");

            return sb.ToString();
        }
    }
}