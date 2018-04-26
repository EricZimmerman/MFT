using System;
using System.Text;

namespace MFT.Attributes
{
    public class SecurityDescriptor : Attribute
    {
        public SecurityDescriptor(byte[] rawBytes) : base(rawBytes)
        {
            if (IsResident)
            {
                var content = new byte[AttributeContentLength];

                Buffer.BlockCopy(rawBytes, ContentOffset, content, 0, AttributeContentLength);

                ResidentData = new ResidentData(content);
            }
            else
            {
                NonResidentData = new NonResidentData(rawBytes);
            }

            if (IsResident == false)
            {
                return;
            }

            SecurityInfo = new SKSecurityDescriptor(ResidentData.Data);
        }

        public SKSecurityDescriptor SecurityInfo { get; }

        public ResidentData ResidentData { get; }

        public NonResidentData NonResidentData { get; }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendLine("**** " + GetType().Name.ToUpperInvariant() + " ****");

            sb.AppendLine(base.ToString());

            sb.AppendLine();

            sb.AppendLine(
                $"SecurityInfo: {SecurityInfo}");

            if (IsResident)
            {
                sb.AppendLine($"ResidentData: {ResidentData}");
                sb.AppendLine($"SecurityInfo: {SecurityInfo}");
            }
            else
            {
                sb.AppendLine($"NonResidentData: {NonResidentData}");
            }

            return sb.ToString();
        }
    }
}