using System;
using System.Text;

// namespaces...

namespace MFT.Attributes;

// public classes...
public class AceRecord
{
    // public enums...
    [Flags]
    public enum AceFlagsEnum
    {
        ContainerInheritAce = 0x02,
        FailedAccessAceFlag = 0x80,
        InheritedAce = 0x10,
        InheritOnlyAce = 0x08,
        None = 0x0,
        NoPropagateInheritAce = 0x04,
        ObjectInheritAce = 0x01,
        SuccessfulAccessAceFlag = 0x40
    }

    public enum AceTypeEnum
    {
        AccessAllowed = 0x0,
        AccessAllowedCompound = 0x4,
        AccessAllowedObject = 0x5,
        AccessDenied = 0x1,
        AccessDeniedObject = 0x6,
        SystemAlarm = 0x3,
        SystemAlarmObject = 0x8,
        SystemAudit = 0x2,
        SystemAuditObject = 0x7,
        AccessAllowedCallback = 0x9,
        AccessDeniedCallback = 0xa,
        AccessAllowedCallbackObject = 0xb,
        AccessDeniedCallbackObject = 0xc,
        SystemAuditCallback = 0xd,
        SystemAlarmCallback = 0xe,
        SystemAuditCallbackObject = 0xf,
        SystemAlarmCallbackObject = 0x10,
        SystemMandatoryLabel = 0x11,
        SystemResourceAttribute = 0x12,
        SystemScopedPolicyId = 0x13,
        SystemProcessTrustLabel = 0x14,
        Unknown = 0x99
    }

    [Flags]
    public enum MasksEnum
    {
        FilEExecute = 0x00000020,
        CreateSubDir = 0x00000004,
        ReadAttrs = 0x00000080,
        WriteAttrs = 0x00000100,
        WriteOwnProp = 0x00000200,
        DeleteOwnProp = 0x00000400,
        ViewOwnProp = 0x00000800,
        Delete = 0x00010000,
        ReadEa = 0x00000008,
        FullControl = 0x000F003F,
        WriteEa = 0x00000010,
        FileReadDirList = 0x00000001,
        ReadControl = 0x00020000,
        FileWriteFileAdd = 0x00000002,
        WriteDac = 0x00040000,
        WriteOwner = 0x00080000,
        Synchronize = 0x000100000,
        TrusteeOwn = 0x00004000,
        UserAsContact = 0x00008000
    }

    // public constructors...
    /// <summary>
    ///     Initializes a new instance of the <see cref="ACERecord" /> class.
    /// </summary>
    public AceRecord(byte[] rawBytes)
    {
        RawBytes = rawBytes;
    }

    // public properties...
    public AceFlagsEnum AceFlags => (AceFlagsEnum) RawBytes[1];

    public ushort AceSize => BitConverter.ToUInt16(RawBytes, 2);

    public AceTypeEnum AceType
    {
        get
        {
            switch (RawBytes[0])
            {
                case 0x0:
                    return AceTypeEnum.AccessAllowed;
                //ncrunch: no coverage start
                case 0x1:
                    return AceTypeEnum.AccessDenied;

                case 0x2:
                    return AceTypeEnum.SystemAudit;

                case 0x3:
                    return AceTypeEnum.SystemAlarm;

                case 0x4:
                    return AceTypeEnum.AccessAllowedCompound;

                case 0x5:
                    return AceTypeEnum.AccessAllowedObject;

                case 0x6:
                    return AceTypeEnum.AccessDeniedObject;

                case 0x7:
                    return AceTypeEnum.SystemAuditObject;

                case 0x8:
                    return AceTypeEnum.SystemAlarmObject;
                case 0x9:
                    return AceTypeEnum.AccessAllowedCallback;
                case 0xa:
                    return AceTypeEnum.AccessDeniedCallback;
                case 0xb:
                    return AceTypeEnum.AccessAllowedCallbackObject;
                case 0xc:
                    return AceTypeEnum.AccessDeniedCallbackObject;
                case 0xd:
                    return AceTypeEnum.SystemAuditCallback;
                case 0xe:
                    return AceTypeEnum.SystemAlarmCallback;
                case 0xf:
                    return AceTypeEnum.SystemAuditCallbackObject;
                case 0x10:
                    return AceTypeEnum.SystemAlarmCallbackObject;
                case 0x11:
                    return AceTypeEnum.SystemMandatoryLabel;
                case 0x12:
                    return AceTypeEnum.SystemResourceAttribute;
                case 0x13:
                    return AceTypeEnum.SystemScopedPolicyId;
                case 0x14:
                    return AceTypeEnum.SystemProcessTrustLabel;
                default:
                    return AceTypeEnum.Unknown;
                //ncrunch: no coverage end
            }
        }
    }

    public MasksEnum Mask => (MasksEnum) BitConverter.ToUInt32(RawBytes, 4);

    public byte[] RawBytes { get; }

    public string Sid
    {
        get
        {
            var rawSidBytes = new byte[AceSize - 0x8];
            Buffer.BlockCopy(RawBytes, 0x8, rawSidBytes, 0, rawSidBytes.Length);

            return Helpers.ConvertHexStringToSidString(rawSidBytes);
        }
    }

    public Helpers.SidTypeEnum SidType => Helpers.GetSidTypeFromSidString(Sid);

    // public methods...
    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"ACE Size: 0x{AceSize:X}");

        sb.AppendLine($"ACE Type: {AceType}");

        sb.AppendLine($"ACE Flags: {AceFlags.ToString().Replace(", ", "|")}");

        sb.AppendLine($"Mask: {Mask}");

        sb.AppendLine($"SID: {Sid}");
        sb.AppendLine($"SID Type: {SidType}");

        sb.AppendLine($"SID Type Description: {Helpers.GetDescriptionFromEnumValue(SidType)}");

        return sb.ToString();
    }
}