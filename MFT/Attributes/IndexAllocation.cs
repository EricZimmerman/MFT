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

            sb.AppendLine("**** INDEX ALLOCATION ****");

            sb.AppendLine(base.ToString());

            sb.AppendLine();

            sb.AppendLine(
                $"Non Resident Data: {NonResidentData}");

            return sb.ToString();
        }
    }
}