using System;
using System.Text;

namespace MFT.Attributes
{
    public class ObjectId : Attribute
    {
        public ObjectId(byte[] rawBytes) : base(rawBytes)
        {
            var content = new byte[AttributeContentLength];

            Buffer.BlockCopy(rawBytes, ContentOffset, content, 0, AttributeContentLength);

            var residentData = new ResidentData(content);

            var guidRaw0 = new byte[16];
            var guidRaw1 = new byte[16];
            var guidRaw2 = new byte[16];
            var guidRaw3 = new byte[16];

            Buffer.BlockCopy(residentData.Data, 0x00, guidRaw0, 0, 16);
            FileDroid = new Guid(guidRaw0);

            if (residentData.Data.Length == 16)
            {
                return;
            }

            Buffer.BlockCopy(residentData.Data, 0x0A, guidRaw1, 0, 16);
            Buffer.BlockCopy(residentData.Data, 0x20, guidRaw2, 0, 16);
            Buffer.BlockCopy(residentData.Data, 0x30, guidRaw3, 0, 16);

            VolumeDroidBirth = new Guid(guidRaw1);
            FileDroidBirth = new Guid(guidRaw2);
            DomainDroidBirth = new Guid(guidRaw3);
        }

        public Guid FileDroidBirth { get; }

        public Guid VolumeDroidBirth { get; }

        public Guid FileDroid { get; }

        public Guid DomainDroidBirth { get; }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendLine("**** " + GetType().Name.ToUpperInvariant() + " ****");

            sb.AppendLine(base.ToString());

            sb.AppendLine();

            sb.AppendLine(
                $"FileDroidBirth: {FileDroidBirth}\r\nVolumeDroidBirth: {VolumeDroidBirth}\r\nFileDroid: {FileDroid}\r\nDomainDroidBirth: {DomainDroidBirth}");

            return sb.ToString();
        }
    }
}