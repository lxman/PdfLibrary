using System.Globalization;
using System.Text.RegularExpressions;

namespace PdfLibrary.Metadata;

/// <summary>
/// Converts between PDF date strings (D:YYYYMMDDHHmmSS±HH'mm'), ISO-8601 strings, and
/// <see cref="DateTimeOffset"/>. All parsing and formatting uses <see cref="CultureInfo.InvariantCulture"/>.
/// </summary>
internal static class PdfDate
{
    // D:YYYYMMDDHHmmSS and optional offset ±HH'mm' or Z
    // Groups: year, month, day, hour, min, sec, sign, offH, offM
    private static readonly Regex PdfPattern = new(
        @"^D:(\d{4})(\d{2})?(\d{2})?(\d{2})?(\d{2})?(\d{2})?([+\-Z])?(\d{2})?'?(\d{2})?'?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Parses a PDF date string such as <c>D:20260620134500+00'00'</c>.
    /// Partial dates (e.g. <c>D:2026</c>, <c>D:202606</c>) are accepted with defaults
    /// (month=1, day=1, time=00:00:00, offset=UTC).
    /// Returns <c>false</c> for malformed input instead of throwing.
    /// </summary>
    public static bool TryParsePdf(string? s, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;

        Match m = PdfPattern.Match(s);
        if (!m.Success) return false;

        if (!int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int year))
            return false;

        int month  = ParseGroupOrDefault(m.Groups[2], 1);
        int day    = ParseGroupOrDefault(m.Groups[3], 1);
        int hour   = ParseGroupOrDefault(m.Groups[4], 0);
        int minute = ParseGroupOrDefault(m.Groups[5], 0);
        int second = ParseGroupOrDefault(m.Groups[6], 0);

        TimeSpan offset = TimeSpan.Zero;
        if (m.Groups[7].Success)
        {
            string sign = m.Groups[7].Value;
            if (sign == "Z")
            {
                offset = TimeSpan.Zero;
            }
            else if (sign is "+" or "-")
            {
                int offH = ParseGroupOrDefault(m.Groups[8], 0);
                int offM = ParseGroupOrDefault(m.Groups[9], 0);
                offset = new TimeSpan(offH, offM, 0);
                if (sign == "-") offset = offset.Negate();
            }
        }

        try
        {
            result = new DateTimeOffset(year, month, day, hour, minute, second, offset);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Formats a <see cref="DateTimeOffset"/> as a PDF date string, e.g. <c>D:20260620134500+00'00'</c>.
    /// Always uses <see cref="CultureInfo.InvariantCulture"/>.
    /// </summary>
    public static string FormatPdf(DateTimeOffset dto)
    {
        TimeSpan off = dto.Offset;
        char sign = off < TimeSpan.Zero ? '-' : '+';
        TimeSpan absOff = off < TimeSpan.Zero ? off.Negate() : off;

        return string.Create(CultureInfo.InvariantCulture,
            $"D:{dto.Year:D4}{dto.Month:D2}{dto.Day:D2}{dto.Hour:D2}{dto.Minute:D2}{dto.Second:D2}{sign}{absOff.Hours:D2}'{absOff.Minutes:D2}'");
    }

    /// <summary>
    /// Parses an ISO-8601 date-time string such as <c>2026-06-20T13:45:00+00:00</c>.
    /// Returns <c>false</c> for malformed input.
    /// </summary>
    public static bool TryParseIso(string? s, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;

        return DateTimeOffset.TryParseExact(
            s,
            new[] { "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ssZ", "yyyy-MM-dd'T'HH:mm:ss" },
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out result);
    }

    /// <summary>
    /// Formats a <see cref="DateTimeOffset"/> as an ISO-8601 string, e.g. <c>2026-06-20T13:45:00+00:00</c>.
    /// Always uses <see cref="CultureInfo.InvariantCulture"/>.
    /// </summary>
    public static string FormatIso(DateTimeOffset dto) =>
        dto.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    // ─── helpers ───────────────────────────────────────────────────────────────

    private static int ParseGroupOrDefault(Group g, int defaultValue)
    {
        if (!g.Success || string.IsNullOrEmpty(g.Value)) return defaultValue;
        return int.TryParse(g.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int v)
            ? v
            : defaultValue;
    }
}
