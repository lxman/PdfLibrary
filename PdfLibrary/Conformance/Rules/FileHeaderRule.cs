namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A file header (ISO 19005-2, 6.1.2). The header is a byte-level construct, so this rule reads
/// <see cref="ConformanceContext.SourceBytes"/> directly and skips gracefully when they are unavailable
/// (an in-memory document has no file bytes). Two lines are required, in order:
/// <list type="number">
///   <item>at byte offset 0, <c>%PDF-1.</c> followed by a single digit <c>0</c>–<c>7</c> and then a
///     single end-of-line marker (CR, LF, or CRLF);</item>
///   <item>a comment line: a <c>%</c> followed by at least four bytes whose first four are each greater
///     than 127 (the binary-content marker), then an end-of-line marker.</item>
/// </list>
/// The first violation is reported (one finding is enough for the clause). This is a strict subset of the
/// reference validator's check and never fires on a well-formed <c>%PDF-1.n</c> + binary-comment header,
/// so it introduces no false positive on the corpus pass files.
/// </summary>
internal sealed class FileHeaderRule : IConformanceRule
{
    public string RuleId => "file-header";

    // File-structure clause of ISO 19005 §6.1 — PDF/A only (PDF/UA rules are §7.x; PDF/X-4 has its own §6.1).
    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    // "%PDF-1." — the seven bytes that must precede the single version digit at offset 0.
    private static readonly byte[] VersionPrefix = "%PDF-1."u8.ToArray();

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        byte[]? bytes = context.SourceBytes;
        if (bytes is null)
            yield break; // byte-level rule: nothing to inspect when the source file bytes are unavailable.

        string? violation = FirstViolation(bytes);
        if (violation is not null)
        {
            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "6.1.2"),
                Message = violation,
            };
        }
    }

    /// <summary>The first header violation, or null when the header conforms.</summary>
    private static string? FirstViolation(byte[] b)
    {
        // Line 1: "%PDF-1." + one digit 0–7, at offset 0.
        if (b.Length < VersionPrefix.Length + 1 || !b.AsSpan(0, VersionPrefix.Length).SequenceEqual(VersionPrefix))
        {
            return "The file does not begin at offset 0 with a %PDF-1.n header (ISO 19005-2, 6.1.2).";
        }

        byte digit = b[VersionPrefix.Length];
        if (digit is < (byte)'0' or > (byte)'7')
        {
            return $"The PDF version '1.{(char)digit}' in the file header is not in the range 1.0–1.7.";
        }

        // A single end-of-line marker must immediately follow the version digit.
        int pos = VersionPrefix.Length + 1;
        int afterEol = SkipSingleEol(b, pos);
        if (afterEol == pos)
        {
            return "The %PDF-1.n header line is not terminated by a single end-of-line marker.";
        }
        pos = afterEol;

        // Line 2: a comment ('%') whose first four content bytes are each greater than 127.
        if (pos >= b.Length || b[pos] != (byte)'%')
        {
            return "The header line is not immediately followed by a binary-marker comment (a '%' comment "
                 + "whose first four bytes are each greater than 127).";
        }
        pos++; // step over '%'

        int lineEnd = IndexOfEol(b, pos);
        int contentLength = (lineEnd < 0 ? b.Length : lineEnd) - pos;
        if (contentLength < 4)
        {
            return "The binary-marker comment on the second line of the header has fewer than four bytes.";
        }

        for (int i = 0; i < 4; i++)
        {
            if (b[pos + i] <= 127)
            {
                return "The binary-marker comment on the second line of the header has a byte less than or "
                     + "equal to 127 among its first four bytes; all four must be greater than 127.";
            }
        }

        return null;
    }

    /// <summary>Consumes one end-of-line marker (CR, LF, or CRLF) at <paramref name="pos"/>; returns the
    /// index just past it, or <paramref name="pos"/> unchanged when no marker is present.</summary>
    private static int SkipSingleEol(byte[] b, int pos)
    {
        if (pos < b.Length && b[pos] == 0x0D) // CR, optionally followed by LF
        {
            pos++;
            if (pos < b.Length && b[pos] == 0x0A) pos++;
            return pos;
        }
        if (pos < b.Length && b[pos] == 0x0A) // lone LF
            return pos + 1;
        return pos;
    }

    /// <summary>Index of the next CR or LF at or after <paramref name="pos"/>, or -1 if none.</summary>
    private static int IndexOfEol(byte[] b, int pos)
    {
        for (int i = pos; i < b.Length; i++)
            if (b[i] is 0x0D or 0x0A) return i;
        return -1;
    }
}
