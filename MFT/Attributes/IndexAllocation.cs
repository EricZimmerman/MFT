using System.Text;

namespace MFT.Attributes
{
    public class IndexAllocation : Attribute
    {
        public IndexAllocation(byte[] rawBytes) : base(rawBytes)
        {
            NonResidentData = new NonResidentData(rawBytes);
        }

        public NonResidentData NonResidentData { get; }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendLine("**** " + GetType().Name.ToUpperInvariant() + " ****");

            sb.AppendLine(base.ToString());

            sb.AppendLine();

            sb.AppendLine(
                $"NonResidentData: {NonResidentData}");

            return sb.ToString();
        }
    }
}