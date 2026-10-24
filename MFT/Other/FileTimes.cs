using System;
using System.Text;
using Serilog;

namespace MFT.Other;

/// <summary>
///     FILETIMEs are kept raw (8 bytes instead of a 24 byte DateTimeOffset?) and converted when read; a loaded $MFT has
///     millions of them
/// </summary>
internal static class FileTimes
{
    private static readonly long Max = DateTime.MaxValue.ToFileTimeUtc();

    /// <summary>
    ///     The raw value, or 0 (no time) when it is not a valid FILETIME. An invalid one is logged with its field (such as
    ///     CreatedOn) and source (where it was read)
    /// </summary>
    public static long Check(long raw, string field, in TimestampSource source)
    {
        if (raw <= 0)
        {
            return 0;
        }

        if (raw > Max)
        {
            Log.Warning("Invalid {Field:l} timestamp 0x{Raw:X16} in {Source:l}", field, raw, source.ToString());
            return 0;
        }

        return raw;
    }

    /// <summary>UTC, the same value DateTimeOffset.FromFileTime(raw).ToUniversalTime() gave</summary>
    public static DateTimeOffset? ToDate(long raw) => raw > 0 ? new DateTimeOffset(DateTime.FromFileTimeUtc(raw)) : null;
}

/// <summary>
///     Where timestamps were read (attribute, FILE record, offset, $LogFile LSN), for the warning about an invalid one.
///     A struct, formatted only for a warning: one is made for every attribute of millions of records
/// </summary>
internal readonly struct TimestampSource
{
    private readonly string _attribute;
    private readonly uint? _entry;
    private readonly ushort? _sequence;
    private readonly long? _offset;
    private readonly ulong? _lsn;

    private TimestampSource(string attribute, uint? entry, ushort? sequence, long? offset, ulong? lsn)
    {
        _attribute = attribute;
        _entry = entry;
        _sequence = sequence;
        _offset = offset;
        _lsn = lsn;
    }

    /// <summary>A FILE record in an $MFT, or one logged in $LogFile (lsn > 0; its offset is not a position in an $MFT)</summary>
    public static TimestampSource FileRecord(uint entry, ushort sequence, long offset, ulong lsn) =>
        lsn > 0
            ? new TimestampSource(null, entry, sequence, null, lsn)
            : new TimestampSource(null, entry, sequence, offset, null);

    /// <summary>An index entry: in an $I30 file (absolute offset), or in the slack of a FILE record (entry > 0, offset in the record)</summary>
    public static TimestampSource IndexEntry(long offset, uint entry, bool fromSlack) =>
        new(fromSlack ? "slack index entry" : "index entry", entry > 0 ? entry : null, null, offset, null);

    /// <summary>Something decoded from a $LogFile record</summary>
    public static TimestampSource LogRecord(ulong lsn) => new(null, null, null, null, lsn);

    public TimestampSource WithAttribute(string attribute) => new(attribute, _entry, _sequence, _offset, _lsn);

    public override string ToString()
    {
        var sb = new StringBuilder(_attribute ?? "unknown location");

        if (_entry.HasValue)
        {
            //decimal entry-sequence, as in MFTECmd's EntryNumber and SequenceNumber columns
            sb.Append(" of FILE record ").Append(_entry.Value);
            if (_sequence.HasValue)
            {
                sb.Append('-').Append(_sequence.Value);
            }
        }

        if (_offset.HasValue)
        {
            sb.Append(_entry.HasValue && _sequence.HasValue == false ? " at record offset 0x" : " at offset 0x")
                .Append(_offset.Value.ToString("X"));
        }

        if (_lsn.HasValue)
        {
            sb.Append(" in $LogFile record LSN 0x").Append(_lsn.Value.ToString("X"));
        }

        return sb.ToString();
    }
}
