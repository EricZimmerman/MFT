using System;
using System.Text;

namespace MFT.Attributes
{
    public class FileName : Attribute
    {
        public FileName(byte[] rawBytes) : base(rawBytes)
        {
            var content = new byte[rawBytes.Length - ContentOffset];

            Buffer.BlockCopy(rawBytes, ContentOffset, content, 0, rawBytes.Length - ContentOffset);

            FileInfo = new FileInfo(content);
        }

        public FileInfo FileInfo { get; }


        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendLine("**** " + GetType().Name.ToUpperInvariant() + " ****");

            sb.AppendLine(base.ToString());

            sb.AppendLine();

            sb.AppendLine(FileInfo.ToString());

            return sb.ToString();
        }
    }
}