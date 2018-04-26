using System.Text;

namespace MFT.Attributes
{
    public class VolumeName : Attribute
    {
        public VolumeName(byte[] rawBytes) : base(rawBytes)
        {
            var residentData = new ResidentData(rawBytes);

            VolName = string.Empty;

            if (NameSize > 0 && residentData.Data.Length > 0)
            {
                VolName = Encoding.Unicode.GetString(residentData.Data);
            }
        }

        public string VolName { get; }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendLine("**** " + GetType().Name.ToUpperInvariant() + " ****");

            sb.AppendLine(base.ToString());

            sb.AppendLine();

            sb.AppendLine($"VolName: {VolName}");

            return sb.ToString();
        }
    }
}