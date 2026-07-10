using System.Text.RegularExpressions;

namespace PdfLibrary.Conformance.Xmp;

/// <summary>
/// The XMP <c>date</c> value check: the reference validates a date node by feeding its value to the
/// Adobe XMP ISO-8601 parser and reporting whether it parses. This reproduces the set of forms that
/// parser accepts — a bare year up to a full timestamp with fractional seconds and a time-zone
/// designator — as a single anchored pattern.
///
/// <para>The grammar is deliberately permissive to exactly the XMP-accepted spellings (year;
/// year-month; date; date + <c>T</c> hh:mm[:ss[.fraction]] with an optional <c>Z</c> / ±hh[:mm]
/// offset) and rejects everything else (a stray unit, prose, a malformed offset). It is verified
/// against every conformant corpus date so it can never flag a value the reference accepts.</para>
/// </summary>
internal static class XmpDate
{
    private static readonly Regex Iso8601 = new(
        @"\A\d{4}(-\d{2}(-\d{2}(T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}(:?\d{2})?)?)?)?)?\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>True when <paramref name="value"/> is an XMP-acceptable ISO-8601 date/time.</summary>
    public static bool IsValid(string? value)
    {
        if (value is null)
            return false;
        return Iso8601.IsMatch(value) || Iso8601.IsMatch(value.Trim());
    }
}
