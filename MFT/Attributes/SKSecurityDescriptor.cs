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

        //the sizes used to be the gaps between the parts, assuming the order SACL, DACL, owner, group; other
        //writers (and damaged data) put them elsewhere, which underflowed. Take them from the data instead
        _sizeSacl = AclSize(SaclOffset);
        _sizeDacl = AclSize(DaclOffset);
        _sizeOwnerSid = SidSize(OwnerOffset);
        _sizeGroupSid = SidSize(GroupOffset);


        Padding = string.Empty; //TODO VERIFY ITS ALWAYS ZEROs
    }

    /// <summary>The ACL's size (at +2 in its header), cut to the end of the descriptor; 0 when there is none</summary>
    private uint AclSize(uint offset)
    {
        if (offset == 0 || RawBytes.Length < 0x14 || offset > RawBytes.Length - 4)
        {
            return 0;
        }

        return Math.Min(BitConverter.ToUInt16(RawBytes, (int)offset + 2), (uint)RawBytes.Length - offset);
    }

    /// <summary>8 + 4 bytes per subauthority, cut to the end of the descriptor; 0 when there is none</summary>
    private uint SidSize(uint offset)
    {
        if (offset == 0 || RawBytes.Length < 0x14 || offset > RawBytes.Length - 8)
        {
            return 0;
        }

        return Math.Min(8u + 4u * RawBytes[offset + 1], (uint)RawBytes.Length - offset);
    }

    // public properties...
    public ControlEnum Control => (ControlEnum)BitConverter.ToUInt16(RawBytes, 0x02);

    public XAclRecord Dacl
    {
        get
        {
            if ((Control & ControlEnum.SeDaclPresent) == ControlEnum.SeDaclPresent && _sizeDacl > 0)
            {
                //var rawDacla = RawBytes.Skip((int) DaclOffset).Take((int) sizeDacl).ToArray();

                var rawDacl = new byte[_sizeDacl];
                Buffer.BlockCopy(RawBytes, (int)DaclOffset, rawDacl, 0, (int)_sizeDacl);


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

            if (_sizeGroupSid == 0)
            {
                return string.Empty;
            }

            var rawGroup = new byte[_sizeGroupSid];
            Buffer.BlockCopy(RawBytes, (int)GroupOffset, rawGroup, 0, (int)_sizeGroupSid);

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

            if (_sizeOwnerSid == 0)
            {
                return string.Empty;
            }

            var rawOwner = new byte[_sizeOwnerSid];
            Buffer.BlockCopy(RawBytes, (int)OwnerOffset, rawOwner, 0, (int)_sizeOwnerSid);

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
            if ((Control & ControlEnum.SeSaclPresent) != ControlEnum.SeSaclPresent)
            {
                return null;
            }

            if (_sizeSacl == 0)
            {
                return null;
            }

            var rawSacl = new byte[_sizeSacl];
            Buffer.BlockCopy(RawBytes, (int)SaclOffset, rawSacl, 0, (int)_sizeSacl);

            if (rawSacl.Length == 0)
            {
                return null;
            }

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