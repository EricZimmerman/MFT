using System;
using System.Text;
using MFT.Other;
using Serilog;

namespace MFT.Attributes;

[Flags]
public enum NameTypes
{
    Posix = 0x0,
    Windows = 0x1,
    Dos = 0x2,
    DosWindows = 0x3
}

public class FileInfo
{
    public FileInfo(byte[] rawBytes)
    {
        var entryBytes = new byte[8];

        Buffer.BlockCopy(rawBytes, 0, entryBytes, 0, 8);

        ParentMftRecord = new MftEntryInfo(entryBytes);

        _createdOn = FileTimes.Check(BitConverter.ToInt64(rawBytes, 0x8), "Invalid CreatedOn timestamp! Enable --debug for record information");

        _contentModifiedOn = FileTimes.Check(BitConverter.ToInt64(rawBytes, 0x10), "Invalid ContentModifiedOn timestamp! Enable --debug for record information");

        _recordModifiedOn = FileTimes.Check(BitConverter.ToInt64(rawBytes, 0x18), "Invalid RecordModifiedOn timestamp! Enable --debug for record information");

        _lastAccessedOn = FileTimes.Check(BitConverter.ToInt64(rawBytes, 0x20), "Invalid LastAccessedOn timestamp! Enable --debug for record information");

        PhysicalSize = BitConverter.ToUInt64(rawBytes, 0x28);
        LogicalSize = BitConverter.ToUInt64(rawBytes, 0x30);

        Flags = (StandardInfo.Flag)BitConverter.ToInt32(rawBytes, 0x38);

        ReparseValue = BitConverter.ToInt32(rawBytes, 0x3c);

        NameLength = rawBytes[0x40];
        NameType = (NameTypes)rawBytes[0x41];

        FileName = Encoding.Unicode.GetString(rawBytes, 0x42, NameLength * 2);
    }

    public int ReparseValue { get; }
    public byte NameLength { get; }
    public NameTypes NameType { get; }
    public string FileName { get; }
    public ulong PhysicalSize { get; }
    public ulong LogicalSize { get; }
    private readonly long _createdOn;
    public DateTimeOffset? CreatedOn => FileTimes.ToDate(_createdOn);
    private readonly long _contentModifiedOn;
    public DateTimeOffset? ContentModifiedOn => FileTimes.ToDate(_contentModifiedOn);
    private readonly long _recordModifiedOn;
    public DateTimeOffset? RecordModifiedOn => FileTimes.ToDate(_recordModifiedOn);
    private readonly long _lastAccessedOn;
    public DateTimeOffset? LastAccessedOn => FileTimes.ToDate(_lastAccessedOn);
    public StandardInfo.Flag Flags { get; }
    public MftEntryInfo ParentMftRecord { get; }

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine();

        sb.AppendLine(
            $"File name: {FileName} (Length: 0x{NameLength:X})\r\nFlags: {Flags.ToString().Replace(", ", "|")}, Name Type: {NameType}, " +
            $"Reparse Value: 0x{ReparseValue:X}, Physical Size: 0x{PhysicalSize:X}, Logical Size: 0x{LogicalSize:X}" +
            $"\r\nParent Mft Record: {ParentMftRecord}" +
            $"\r\n\r\nCreated On:\t\t{CreatedOn?.ToString(MftFile.DateTimeFormat)}" +
            $"\r\nContent Modified On:\t{ContentModifiedOn?.ToString(MftFile.DateTimeFormat)}" +
            $"\r\nRecord Modified On:\t{RecordModifiedOn?.ToString(MftFile.DateTimeFormat)}" +
            $"\r\nLast Accessed On:\t{LastAccessedOn?.ToString(MftFile.DateTimeFormat)}");

        return sb.ToString();
    }
}