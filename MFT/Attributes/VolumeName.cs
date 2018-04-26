using System.Text;

namespace MFT.Attributes
{
    public class VolumeName : Attribute
    {
        public VolumeName(byte[] rawBytes) : base(rawBytes)
        {
            var residentData = new ResidentData(rawBytes);

            VolName = string.Empty;

            if (residentData.Data.Length > 0)
            {
                VolName = Encoding.Unicode.GetString(residentData.Data,ContentOffset,residentData.Data.Length - ContentOffset).TrimEnd('\0');
            }
        }

        public string VolName { get; }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendLine("**** " + GetType().Name.ToUpperInvariant() + " ****");

            sb.AppendLine(base.ToString());

            sb.AppendLine();

            sb.AppendLine($"Volume Name: {VolName}");

            return sb.ToString();
        }
    }
}