using System;
using MFT.Other;
using System.Text;
using Serilog;

namespace MFT.Attributes;

public class StandardInfo : Attribute
{
    [Flags]
    public enum Flag
    {
        None = 0x00,
        ReadOnly = 0x01,
        Hidden = 0x02,
        System = 0x04,
        VolumeLabel = 0x08,
        Directory = 0x010,
        Archive = 0x020,
        Device = 0x040,
        Normal = 0x080,
        Temporary = 0x0100,
        SparseFile = 0x0200,
        ReparsePoint = 0x0400,
        Compressed = 0x0800,
        Offline = 0x01000,
        NotContentIndexed = 0x02000,
        Encrypted = 0x04000,
        IntegrityStream = 0x08000,
        Virtual = 0x010000,
        NoScrubData = 0x020000,
        RecallOnOpen = 0x040000,
        RecallOnDataAccess = 0x400000,
        Pinned = 0x80000,
        UnPinned = 0x100000,
        IsDirectory = 0x10000000,
        IsIndexView = 0x20000000
    }

    [Flags]
    public enum Flag2
    {
        None = 0x00,
        IsCaseSensitive = 0x01
    }

    public StandardInfo(byte[] rawBytes) : base(rawBytes)
    {
        _createdOn = FileTimes.Check(BitConverter.ToInt64(rawBytes, 0x18), "Invalid CreatedOn timestamp! Enable --debug for record information");

        _contentModifiedOn = FileTimes.Check(BitConverter.ToInt64(rawBytes, 0x20), "Invalid ContentModifiedOn timestamp! Enable --debug for record information");

        _recordModifiedOn = FileTimes.Check(BitConverter.ToInt64(rawBytes, 0x28), "Invalid RecordModifiedOn timestamp! Enable --debug for record information");

        _lastAccessedOn = FileTimes.Check(BitConverter.ToInt64(rawBytes, 0x30), "Invalid LastAccessedOn timestamp! Enable --debug for record information");

        Flags = (Flag)BitConverter.ToInt32(rawBytes, 0x38);

        MaxVersion = BitConverter.ToInt32(rawBytes, 0x3C);
        Flags2 = (Flag2)BitConverter.ToInt32(rawBytes, 0x40);
        ClassId = BitConverter.ToInt32(rawBytes, 0x44);

        if (rawBytes.Length <= 0x48)
        {
            return;
        }

        OwnerId = BitConverter.ToInt32(rawBytes, 0x48);
        SecurityId = BitConverter.ToInt32(rawBytes, 0x4C);
        QuotaCharged = BitConverter.ToInt32(rawBytes, 0x50);
        UpdateSequenceNumber = BitConverter.ToInt64(rawBytes, 0x58);
    }

    public int MaxVersion { get; }
    public Flag2 Flags2 { get; }
    public int ClassId { get; }
    public int OwnerId { get; }
    public int SecurityId { get; }
    public int QuotaCharged { get; }
    public long UpdateSequenceNumber { get; }

    private readonly long _createdOn;
    public DateTimeOffset? CreatedOn => FileTimes.ToDate(_createdOn);
    private readonly long _contentModifiedOn;
    public DateTimeOffset? ContentModifiedOn => FileTimes.ToDate(_contentModifiedOn);
    private readonly long _recordModifiedOn;
    public DateTimeOffset? RecordModifiedOn => FileTimes.ToDate(_recordModifiedOn);
    private readonly long _lastAccessedOn;
    public DateTimeOffset? LastAccessedOn => FileTimes.ToDate(_lastAccessedOn);

    public Flag Flags { get; }

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine("**** STANDARD INFO ****");

        sb.AppendLine(base.ToString());

        sb.AppendLine();

        sb.AppendLine(
            $"Flags: {Flags.ToString().Replace(", ", "|")}, Max Version: 0x{MaxVersion:X}, Flags 2: {Flags2.ToString().Replace(", ", "|")}, Class Id: 0x{ClassId:X}, " +
            $"Owner Id: 0x{OwnerId:X}, Security Id: 0x{SecurityId:X}, Quota Charged: 0x{QuotaCharged:X} " +
            $"\r\nUpdate Sequence #: 0x{UpdateSequenceNumber:X}" +
            $"\r\n\r\nCreated On:\t\t{CreatedOn?.ToString(MftFile.DateTimeFormat)}" +
            $"\r\nContent Modified On:\t{ContentModifiedOn?.ToString(MftFile.DateTimeFormat)}" +
            $"\r\nRecord Modified On:\t{RecordModifiedOn?.ToString(MftFile.DateTimeFormat)}" +
            $"\r\nLast Accessed On:\t{LastAccessedOn?.ToString(MftFile.DateTimeFormat)}");

        return sb.ToString();
    }
}
