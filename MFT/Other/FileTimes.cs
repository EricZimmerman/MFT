using System;
using Serilog;

namespace MFT.Other;

/// <summary>
///     FILETIMEs are kept raw (8 bytes instead of a 24 byte DateTimeOffset?) and converted when read; a loaded $MFT has
///     millions of them
/// </summary>
internal static class FileTimes
{
    private static readonly long Max = DateTime.MaxValue.ToFileTimeUtc();

    /// <summary>The raw value, or 0 (no time) when it is not a valid FILETIME</summary>
    public static long Check(long raw, string warning)
    {
        if (raw <= 0)
        {
            return 0;
        }

        if (raw > Max)
        {
            Log.Warning(warning);
            return 0;
        }

        return raw;
    }

    /// <summary>UTC, the same value DateTimeOffset.FromFileTime(raw).ToUniversalTime() gave</summary>
    public static DateTimeOffset? ToDate(long raw) => raw > 0 ? new DateTimeOffset(DateTime.FromFileTimeUtc(raw)) : null;
}
