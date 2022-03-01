using System;
using System.Text;

namespace MFT.Attributes;

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

        SecurityInfo = new SkSecurityDescriptor(ResidentData.Data);
    }

    public SkSecurityDescriptor SecurityInfo { get; }

    public ResidentData ResidentData { get; }

    public NonResidentData NonResidentData { get; }

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine("**** SECURITY DESCRIPTOR ****");

        sb.AppendLine(base.ToString());

        sb.AppendLine();

        sb.AppendLine(
            $"Security Info: {SecurityInfo}");

        if (IsResident)
        {
            sb.AppendLine($"Resident Data: {ResidentData}");
            sb.AppendLine($"Security Info: {SecurityInfo}");
        }
        else
        {
            sb.AppendLine($"Non Resident Data: {NonResidentData}");
        }

        return sb.ToString();
    }
}