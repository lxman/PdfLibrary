using System.Collections.Generic;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A (ISO 19005-2/3, clause 6.1.4 test 2): the <c>xref</c> keyword and the cross-reference
/// subsection header that follows it shall be separated by a SINGLE EOL marker — one CR, one LF, or
/// one CRLF, and nothing else. Mirrors veraPDF's <c>CosXRef.xrefEOLMarkersComplyPDFA</c>.
///
/// <para>A byte-level rule, like <see cref="IndirectObjectSpacingRule"/>: the parser normalises this
/// whitespace away, so the source bytes are re-read. An in-memory document has none and the rule
/// reports nothing.</para>
///
/// <para><b>Cross-reference STREAMS are out of scope</b>, and correctly so: a stream carries no
/// <c>xref</c> keyword and no subsection header, so the clause has nothing to constrain. Only
/// classic tables are examined.</para>
///
/// <para><b>Why tables are found by scanning, and why the scan is tolerant.</b> The offset chain
/// (<c>startxref</c> plus each trailer's <c>/Prev</c>) is not retained by the parser, so candidates
/// are located in the raw bytes. The identification deliberately does NOT require well-formed
/// separation — requiring it would make every violation invisible, since the malformed separator is
/// the very thing being detected. Instead a candidate qualifies only when the SUBSECTION HEADER
/// grammar follows it (whitespace, then two integers, then an EOL), which is what distinguishes a
/// real table from the word "xref" occurring in ordinary text. Both corpus fixtures contain exactly
/// that trap: each embeds the sentence "…xref keyword and the following cross reference…" in its own
/// document information, and a keyword scan alone would flag it.</para>
/// </summary>
internal sealed class XrefTableSpacingRule : IConformanceRule
{
    private const byte Cr = 0x0D, Lf = 0x0A;
    private static readonly byte[] XrefKeyword = "xref"u8.ToArray();
    private static readonly byte[] StartPrefix = "start"u8.ToArray();

    public string RuleId => "xref-spacing";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        byte[]? bytes = context.SourceBytes;
        if (bytes is null)
            yield break; // byte-level rule: an in-memory document has no source bytes to inspect

        // EVERY table, not just the one startxref names: an incrementally-updated file carries one per
        // revision chained by /Prev, and corpus fixture 6-1-4-t01-fail-b.pdf has a malformed first
        // table and a well-formed second one. Stopping at the first would report it as conforming.
        for (var i = 0; i + XrefKeyword.Length <= bytes.Length; i++)
        {
            if (!MatchesAt(bytes, i, XrefKeyword)) continue;
            if (IsStartXref(bytes, i)) continue;

            if (SeparatorLength(bytes, i + XrefKeyword.Length) is not { } separator)
                continue; // not a cross-reference table — the subsection header grammar does not follow

            if (IsSingleEolMarker(bytes, i + XrefKeyword.Length, separator))
                continue;

            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "6.1.4"),
                Message = "The 'xref' keyword and the cross-reference subsection header must be "
                          + "separated by a single EOL marker, but they are separated by "
                          + Describe(bytes, i + XrefKeyword.Length, separator) + ".",
            };
        }
    }

    private static bool MatchesAt(byte[] b, int at, byte[] needle)
    {
        if (at + needle.Length > b.Length) return false;
        for (var k = 0; k < needle.Length; k++)
            if (b[at + k] != needle[k]) return false;
        return true;
    }

    /// <summary>True when this <c>xref</c> is the tail of <c>startxref</c>, which is a different
    /// keyword naming an offset rather than introducing a table.</summary>
    private static bool IsStartXref(byte[] b, int at) =>
        at >= StartPrefix.Length && MatchesAt(b, at - StartPrefix.Length, StartPrefix);

    /// <summary>The number of bytes between the keyword and the subsection header, or null when what
    /// follows is not a subsection header at all (so this is not a cross-reference table).
    ///
    /// <para>Accepts any run of spaces, tabs, CRs and LFs as the candidate separator — including a
    /// malformed one — then requires <c>digits SP digits</c> followed by whitespace, the subsection
    /// header of ISO 32000-1 7.5.4. An empty separator is rejected: <c>xref0 15</c> would be a
    /// different defect, and the keyword would not have been recognised as one.</para></summary>
    private static int? SeparatorLength(byte[] b, int afterKeyword)
    {
        int p = afterKeyword;
        while (p < b.Length && b[p] is 0x20 or 0x09 or Cr or Lf) p++;
        int separator = p - afterKeyword;
        if (separator == 0) return null;

        if (!ReadIntegerThen(b, ref p, 0x20)) return null;   // first object number, then a space
        if (!ReadIntegerThen(b, ref p, null)) return null;   // entry count, then any whitespace
        return separator;
    }

    /// <summary>Reads one run of ASCII digits at <paramref name="p"/>, then requires
    /// <paramref name="terminator"/> (or, when null, any whitespace). Advances past both on success.</summary>
    private static bool ReadIntegerThen(byte[] b, ref int p, byte? terminator)
    {
        int digitsStart = p;
        while (p < b.Length && b[p] is >= (byte)'0' and <= (byte)'9') p++;
        if (p == digitsStart || p >= b.Length) return false;

        bool ok = terminator is { } t ? b[p] == t : b[p] is 0x20 or 0x09 or Cr or Lf;
        if (ok) p++;
        return ok;
    }

    /// <summary>Whether the separator is exactly one EOL marker: a lone CR, a lone LF, or one CRLF
    /// pair. Anything else — a space, a tab, two EOLs, or an EOL with a space beside it — is the
    /// violation this clause names.</summary>
    private static bool IsSingleEolMarker(byte[] b, int at, int length) => length switch
    {
        1 => b[at] is Cr or Lf,
        2 => b[at] == Cr && b[at + 1] == Lf,
        _ => false,
    };

    private static string Describe(byte[] b, int at, int length)
    {
        var text = new System.Text.StringBuilder();
        for (int k = at; k < at + length; k++)
        {
            text.Append(b[k] switch
            {
                Cr => "CR",
                Lf => "LF",
                0x20 => "SPACE",
                0x09 => "TAB",
                _ => "0x" + b[k].ToString("X2"),
            });
            if (k + 1 < at + length) text.Append(' ');
        }
        return text.ToString();
    }
}
