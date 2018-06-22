using System;
using System.Text;

namespace MFT.Attributes
{
    public class LoggedUtilityStream : Attribute
    {
        public LoggedUtilityStream(byte[] rawBytes) : base(rawBytes)
        {
            var content = new byte[AttributeContentLength];

            Buffer.BlockCopy(rawBytes, ContentOffset, content, 0, AttributeContentLength);

            //TODO decode content based on Name? $EFS, etc.
            ResidentData = new ResidentData(content);
        }

        public ResidentData ResidentData { get; }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendLine("**** LOGGED UTILITY STREAM ****");

            sb.AppendLine(base.ToString());

            sb.AppendLine();

            sb.AppendLine(
                $"Resident Data: {ResidentData}");

            return sb.ToString();
        }
    }
}