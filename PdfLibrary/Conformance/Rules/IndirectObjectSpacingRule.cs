using PdfLibrary.Structure;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A (ISO 19005-2/3, clause 6.1.9): the whitespace framing every indirect object definition is
/// constrained. For each <c>N G obj … endobj</c> the object number and generation number are separated by a
/// single white-space character; the generation number and the <c>obj</c> keyword are separated by a single
/// white-space character; the object number and the <c>endobj</c> keyword are each immediately preceded by
/// an EOL marker; and the <c>obj</c> and <c>endobj</c> keywords are each immediately followed by an EOL
/// marker. Mirrors veraPDF's <c>CosIndirect.spacingCompliesPDFA</c>.
///
/// A byte-level rule: the parser normalises whitespace away, so it re-reads the source bytes at each
/// object's cross-reference offset. It runs only on in-use, uncompressed objects (compressed objects live
/// inside an object stream and have no such framing). Every object is self-validated first — the bytes at
/// the recorded offset must parse as this object's own <c>N G obj</c> header — so a stale offset, a rebuilt
/// xref, or a leading-junk header (whose offsets are relative to the PDF header, not the file) makes the
/// object skipped, never mis-flagged. When the source bytes are unavailable (an in-memory document) the
/// rule cannot run and reports nothing.
/// </summary>
internal sealed class IndirectObjectSpacingRule : IConformanceRule
{
    private const byte Cr = 0x0D, Lf = 0x0A;
    private static readonly byte[] ObjKeyword = "obj"u8.ToArray();
    private static readonly byte[] EndObjKeyword = "endobj"u8.ToArray();

    public string RuleId => "indirect-object-spacing";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        byte[]? bytes = context.SourceBytes;
        if (bytes is null)
            yield break; // byte-level rule: an in-memory document has no source bytes to inspect

        // In-use, uncompressed entries only, ordered by offset so the byte range of each object (this
        // header up to the next object's offset) can be delimited for the endobj search below.
        var entries = context.Document.XrefTable.Entries
            .Where(e => e is { IsInUse: true, EntryType: PdfXrefEntryType.Uncompressed })
            .OrderBy(e => e.ByteOffset)
            .ToList();

        for (int i = 0; i < entries.Count; i++)
        {
            PdfXrefEntry entry = entries[i];
            long start = entry.ByteOffset; // points at the object number's first digit
            if (start < 1 || start >= bytes.Length)
                continue; // out of range, or no preceding byte to bear the object number's EOL

            long regionEnd = i + 1 < entries.Count ? entries[i + 1].ByteOffset : bytes.Length;
            if (regionEnd > bytes.Length) regionEnd = bytes.Length;

            string? violation = Evaluate(bytes, (int)start, (int)regionEnd, entry.ObjectNumber, entry.GenerationNumber);
            if (violation is null)
                continue;

            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "6.1.9"),
                Message = $"Indirect object {entry.ObjectNumber} {entry.GenerationNumber}: {violation}.",
                ObjectNumber = entry.ObjectNumber,
            };
        }
    }

    /// <summary>
    /// Returns a description of the object's first spacing violation, or null when it conforms — or when the
    /// bytes at <paramref name="start"/> do not parse as this object's <c>N G obj</c> header, in which case
    /// the object is skipped (never flagged), keeping the rule a strict subset of the reference validator.
    /// </summary>
    private static string? Evaluate(byte[] b, int start, int regionEnd, int expectNum, int expectGen)
    {
        int p = start;

        int numStart = p;
        while (p < b.Length && IsDigit(b[p])) p++;
        if (p == numStart || !DigitsEqual(b, numStart, p, expectNum))
            return null; // offset does not point at this object's number — do not trust it

        int ws1 = SkipWhitespace(b, ref p);
        int genStart = p;
        while (p < b.Length && IsDigit(b[p])) p++;
        if (ws1 == 0 || p == genStart || !DigitsEqual(b, genStart, p, expectGen))
            return null;

        int ws2 = SkipWhitespace(b, ref p);
        if (ws2 == 0 || !Matches(b, p, ObjKeyword))
            return null; // not a 'N G obj' header at this offset
        int afterObj = p + ObjKeyword.Length;

        // Header validated against the xref — every check below is a genuine clause-6.1.9 violation.
        if (!IsEol(b[start - 1]))
            return "the object number is not immediately preceded by an EOL marker";
        if (ws1 != 1)
            return "the object number and generation number are not separated by a single white-space character";
        if (ws2 != 1)
            return "the generation number and 'obj' keyword are not separated by a single white-space character";
        if (afterObj >= b.Length || !IsEol(b[afterObj]))
            return "the 'obj' keyword is not immediately followed by an EOL marker";

        // The endobj framing can only be judged when the object's region holds exactly one 'endobj' token.
        // The region (this header up to the next object's offset) legally contains other bytes — an
        // inter-object comment, a superseded object copy left by an incremental update, or stream data — any
        // of which may embed the bytes 'endobj'. If more than one occurs, which is structural is ambiguous,
        // so the endobj checks are skipped (a safe under-report) rather than risk flagging a conformant
        // object. The single-object corpus fixtures (last object before the xref) have exactly one.
        int endObj = SingleOccurrence(b, EndObjKeyword, start, regionEnd);
        if (endObj >= 0)
        {
            if (!IsEol(b[endObj - 1]))
                return "the 'endobj' keyword is not immediately preceded by an EOL marker";
            int afterEnd = endObj + EndObjKeyword.Length;
            if (afterEnd < b.Length && !IsEol(b[afterEnd]))
                return "the 'endobj' keyword is not immediately followed by an EOL marker";
        }

        return null;
    }

    private static int SkipWhitespace(byte[] b, ref int p)
    {
        int count = 0;
        while (p < b.Length && IsWhitespace(b[p])) { p++; count++; }
        return count;
    }

    /// <summary>Parses the decimal digits in <c>b[from, to)</c> and reports whether they equal <paramref name="expected"/>.</summary>
    private static bool DigitsEqual(byte[] b, int from, int to, int expected)
    {
        long value = 0;
        for (int i = from; i < to; i++)
        {
            value = value * 10 + (b[i] - '0');
            if (value > int.MaxValue) return false; // absurd run of digits — not a real object number
        }
        return value == expected;
    }

    private static bool Matches(byte[] b, int pos, byte[] needle) =>
        pos + needle.Length <= b.Length && b.AsSpan(pos, needle.Length).SequenceEqual(needle);

    /// <summary>
    /// The start index of <paramref name="needle"/> when it occurs exactly once fully within
    /// <c>[from, end)</c>; -1 when it is absent or occurs more than once (ambiguous).
    /// </summary>
    private static int SingleOccurrence(byte[] b, byte[] needle, int from, int end)
    {
        int found = -1;
        int limit = Math.Min(end, b.Length) - needle.Length;
        for (int i = from; i <= limit; i++)
        {
            if (!b.AsSpan(i, needle.Length).SequenceEqual(needle))
                continue;
            if (found >= 0)
                return -1; // more than one occurrence — cannot tell which is structural
            found = i;
        }
        return found;
    }

    private static bool IsDigit(byte x) => x is >= (byte)'0' and <= (byte)'9';

    // ISO 32000-1 Table 1 white-space characters.
    private static bool IsWhitespace(byte x) => x is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20;

    private static bool IsEol(byte x) => x is Cr or Lf;
}
