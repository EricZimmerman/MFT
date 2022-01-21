using System;
using System.Text;

// namespaces...

namespace MFT.Attributes;

// public classes...
public class SkSecurityDescriptor
{
    // public enums...
    [Flags]
    public enum ControlEnum
    {
        SeDaclAutoInherited = 0x0400,
        SeDaclAutoInheritReq = 0x0100,
        SeDaclDefaulted = 0x0008,
        SeDaclPresent = 0x0004,
        SeDaclProtected = 0x1000,
        SeGroupDefaulted = 0x0002,
        SeOwnerDefaulted = 0x0001,
        SeServerSecurity = 0x0080,
        SeDaclUntrusted = 0x0040,
        SeRmControlValid = 0x4000,
        SeSaclAutoInherited = 0x0800,
        SeSaclAutoInheritReq = 0x0200,
        SeSaclDefaulted = 0x0020,
        SeSaclPresent = 0x0010,
        SeSaclProtected = 0x2000,
        SeSelfRelative = 0x8000
    }

    private readonly uint _sizeDacl;
    private readonly uint _sizeGroupSid;
    private readonly uint _sizeOwnerSid;

    private readonly uint _sizeSacl;

    // public constructors...
    /// <summary>
    ///     Initializes a new instance of the <see cref="SkSecurityDescriptor" /> class.
    /// </summary>
    public SkSecurityDescriptor(byte[] rawBytes)
    {
        RawBytes = rawBytes;

        _sizeSacl = DaclOffset - SaclOffset;
        _sizeDacl = OwnerOffset - DaclOffset;
        _sizeOwnerSid = GroupOffset - OwnerOffset;
        _sizeGroupSid = (uint) (rawBytes.Length - GroupOffset);


        Padding = string.Empty; //TODO VERIFY ITS ALWAYS ZEROs
    }

    // public properties...
    public ControlEnum Control => (ControlEnum) BitConverter.ToUInt16(RawBytes, 0x02);

    public XAclRecord Dacl
    {
        get
        {
            if ((Control & ControlEnum.SeDaclPresent) == ControlEnum.SeDaclPresent)
            {
                //var rawDacla = RawBytes.Skip((int) DaclOffset).Take((int) sizeDacl).ToArray();

                var rawDacl = new byte[_sizeDacl];
                Buffer.BlockCopy(RawBytes, (int) DaclOffset, rawDacl, 0, (int) _sizeDacl);


                return new XAclRecord(rawDacl, XAclRecord.AclTypeEnum.Discretionary);
            }

            return null; //ncrunch: no coverage
        }
    }

    public uint DaclOffset => BitConverter.ToUInt32(RawBytes, 0x10);

    public uint GroupOffset => BitConverter.ToUInt32(RawBytes, 0x08);

    public string GroupSid
    {
        get
        {
            // var rawGroup = RawBytes.Skip((int) GroupOffset).Take((int) sizeGroupSid).ToArray();

            var rawGroup = new byte[_sizeGroupSid];
            Buffer.BlockCopy(RawBytes, (int) GroupOffset, rawGroup, 0, (int) _sizeGroupSid);

            return Helpers.ConvertHexStringToSidString(rawGroup);
        }
    }

    public Helpers.SidTypeEnum GroupSidType => Helpers.GetSidTypeFromSidString(GroupSid);

    public uint OwnerOffset => BitConverter.ToUInt32(RawBytes, 0x04);

    public string OwnerSid
    {
        get
        {
            // var rawOwner = RawBytes.Skip((int) OwnerOffset).Take((int) sizeOwnerSid).ToArray();

            var rawOwner = new byte[_sizeOwnerSid];
            Buffer.BlockCopy(RawBytes, (int) OwnerOffset, rawOwner, 0, (int) _sizeOwnerSid);

            return Helpers.ConvertHexStringToSidString(rawOwner);
        }
    }

    public Helpers.SidTypeEnum OwnerSidType => Helpers.GetSidTypeFromSidString(OwnerSid);

    public string Padding { get; }
    public byte[] RawBytes { get; }

    public byte Revision => RawBytes[0];

    public XAclRecord Sacl
    {
        get
        {
            if ((Control & ControlEnum.SeSaclPresent) != ControlEnum.SeSaclPresent) return null;

            if (_sizeSacl > 1000) return null;

            var rawSacl = new byte[_sizeSacl];
            Buffer.BlockCopy(RawBytes, (int) SaclOffset, rawSacl, 0, (int) _sizeSacl);

            if (rawSacl.Length == 0) return null;

            return new XAclRecord(rawSacl, XAclRecord.AclTypeEnum.Security);
        }
    }

    public uint SaclOffset => BitConverter.ToUInt32(RawBytes, 0x0c);

    // public methods...
    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Revision: 0x{Revision:X}");
        sb.AppendLine($"Control: {Control}");

        sb.AppendLine();
        sb.AppendLine($"Owner offset: 0x{OwnerOffset:X}");
        sb.AppendLine($"Owner SID: {OwnerSid}");
        sb.AppendLine($"Owner SID Type: {OwnerSidType}");

        sb.AppendLine();
        sb.AppendLine($"Group offset: 0x{GroupOffset:X}");
        sb.AppendLine($"Group SID: {GroupSid}");
        sb.AppendLine($"Group SID Type: {GroupSidType}");

        if (Dacl != null)
        {
            sb.AppendLine();
            sb.AppendLine($"Dacl Offset: 0x{DaclOffset:X}");
            sb.AppendLine($"DACL: {Dacl}");
        }

        if (Sacl != null)
        {
            sb.AppendLine();
            sb.AppendLine($"Sacl Offset: 0x{SaclOffset:X}");
            sb.AppendLine($"SACL: {Sacl}");
        }

        return sb.ToString();
    }
}