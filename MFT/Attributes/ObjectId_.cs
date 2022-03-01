using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MFT.Attributes;

public class ObjectId_ : Attribute
{
    public ObjectId_(byte[] rawBytes) : base(rawBytes)
    {
        var content = new byte[AttributeContentLength];

        Buffer.BlockCopy(rawBytes, ContentOffset, content, 0, AttributeContentLength);

        var residentData = new ResidentData(content);

        var guidRaw0 = new byte[16];
        var guidRaw1 = new byte[16];
        var guidRaw2 = new byte[16];
        var guidRaw3 = new byte[16];

        Buffer.BlockCopy(residentData.Data, 0x00, guidRaw0, 0, 16);
        ObjectId = new Guid(guidRaw0);

        if (residentData.Data.Length == 16)
        {
            return;
        }

        Buffer.BlockCopy(residentData.Data, 0x0A, guidRaw1, 0, 16);
        Buffer.BlockCopy(residentData.Data, 0x20, guidRaw2, 0, 16);
        Buffer.BlockCopy(residentData.Data, 0x30, guidRaw3, 0, 16);

        BirthVolumeId = new Guid(guidRaw1);
        BirthObjectId = new Guid(guidRaw2);
        DomainId = new Guid(guidRaw3);
    }

    public Guid BirthObjectId { get; }

    public Guid BirthVolumeId { get; }

    public Guid ObjectId { get; }

    public Guid DomainId { get; }

    private DateTimeOffset GetDateTimeOffsetFromGuid(Guid guid)
    {
        // offset to move from 1/1/0001, which is 0-time for .NET, to gregorian 0-time of 10/15/1582
        var gregorianCalendarStart = new DateTimeOffset(1582, 10, 15, 0, 0, 0, TimeSpan.Zero);
        const int versionByte = 7;
        const int versionByteMask = 0x0f;
        const int versionByteShift = 4;
        const byte timestampByte = 0;

        var bytes = guid.ToByteArray();

        // reverse the version
        bytes[versionByte] &= versionByteMask;
        bytes[versionByte] |= 0x01 >> versionByteShift;

        var timestampBytes = new byte[8];
        Array.Copy(bytes, timestampByte, timestampBytes, 0, 8);

        var timestamp = BitConverter.ToInt64(timestampBytes, 0);
        var ticks = timestamp + gregorianCalendarStart.Ticks;

        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }


//        public Guid ObjectId { get; }
//        public Guid BirthVolumeId { get; }

//        public Guid BirthObjectId { get; }
//        public Guid DomainId { get; }
//        ObjectId = new Guid(br.ReadBytes(16));
//        BirthVolumeId = new Guid(br.ReadBytes(16));
//        BirthObjectId = new Guid(br.ReadBytes(16));
//        DomainId = new Guid(br.ReadBytes(16));

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine("**** OBJECT ID ****");

        sb.AppendLine(base.ToString());

        sb.AppendLine();


        var tempMac = ObjectId.ToString().Split('-').Last();
        var objectIdMacAddress = Regex.Replace(tempMac, ".{2}", "$0:");
        var objectIdCreatedOn = GetDateTimeOffsetFromGuid(ObjectId);


        tempMac = BirthObjectId.ToString().Split('-').Last();
        var birthVolumeIdMacAddress = Regex.Replace(tempMac, ".{2}", "$0:");
        var birthVolumeIdCreatedOn = GetDateTimeOffsetFromGuid(BirthObjectId);

        var extra =
            $"\tBirth Volume Id MAC: {birthVolumeIdMacAddress}\r\n\tBirth Volume Id Created On: {birthVolumeIdCreatedOn.ToUniversalTime():yyyy-MM-dd HH:mm:ss.fffffff}\r\n";
        if (BirthObjectId.ToString() == "00000000-0000-0000-0000-000000000000")
        {
            extra = string.Empty;
        }

        sb.AppendLine(
            $"Object Id: {ObjectId}\r\n\tObject Id MAC: {objectIdMacAddress}\r\n\tObject Id Created On: {objectIdCreatedOn.ToUniversalTime():yyyy-MM-dd HH:mm:ss.fffffff}\r\n" +
            $"Birth Volume Id: {BirthVolumeId}\r\n" +
            extra +
            $"Birth Object Id: {BirthObjectId}\r\n" +
            $"Domain Id: {DomainId}");

        return sb.ToString();
    }
}