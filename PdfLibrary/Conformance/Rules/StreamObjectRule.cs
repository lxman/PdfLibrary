using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A (ISO 19005-2/3, clause 6.1.7.1): a stream object's spacing and declared length.
/// <list type="number">
///   <item><b>Test 1</b> — the <c>/Length</c> value equals the real stream length: the byte count from the
///     data start (after the CRLF or single LF that follows <c>stream</c>) to the EOL that precedes
///     <c>endstream</c>.</item>
///   <item><b>Test 2</b> — the <c>stream</c> keyword is followed by a CR+LF or a single LF, and the
///     <c>endstream</c> keyword is immediately preceded by an EOL marker.</item>
/// </list>
/// (Test 3 — no F/FFilter/FDecodeParms keys — is covered by <see cref="StreamExternalFileRule"/>.)
///
/// A byte-level rule: the parser normalises this framing away, so it re-reads the source bytes at each
/// stream's cross-reference offset. It is a faithful port of veraPDF's CosStream parser — most importantly
/// the CRLF disambiguation of <c>realLength</c> (a CR before the final LF counts as data when
/// <c>size − Length ≤ 1</c>), which is what keeps a well-formed stream from being reported. Every object is
/// self-validated against its xref entry first, and anything it cannot resolve to a confident violation is
/// skipped, so the rule only under-reports (a strict subset of the reference validator). A stream whose
/// <c>/Length</c> is absent or non-numeric is left to other clauses. When the source bytes are unavailable
/// (an in-memory document) the rule reports nothing.
/// </summary>
internal sealed class StreamObjectRule : IConformanceRule
{
    private const byte Cr = 0x0D, Lf = 0x0A;
    private static readonly byte[] StreamKeyword = "stream"u8.ToArray();
    private static readonly byte[] EndStreamKeyword = "endstream"u8.ToArray();

    public string RuleId => "stream-object";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        byte[]? bytes = context.SourceBytes;
        if (bytes is null)
            yield break; // byte-level rule: an in-memory document has no source bytes to inspect

        foreach (PdfStream stream in context.Streams)
        {
            if (!stream.IsIndirect)
                continue;
            if (context.Document.XrefTable.GetEntry(stream.ObjectNumber)
                is not { IsInUse: true, EntryType: PdfXrefEntryType.Uncompressed, ByteOffset: var offset })
            {
                continue;
            }
            if (offset < 1 || offset >= bytes.Length)
                continue;

            string? violation = Evaluate(bytes, (int)offset, stream, context);
            if (violation is null)
                continue;

            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "6.1.7.1"),
                Message = $"Stream object {stream.ObjectNumber}: {violation}.",
                ObjectNumber = stream.ObjectNumber,
            };
        }
    }

    private static string? Evaluate(byte[] b, int offset, PdfStream stream, ConformanceContext context)
    {
        // Self-validate: the bytes at the xref offset must begin with this object's number, else the offset
        // is stale / header-relative on a leading-junk file — skip rather than mis-read.
        int p = offset;
        int numStart = p;
        while (p < b.Length && IsDigit(b[p])) p++;
        if (p == numStart || !DigitsEqual(b, numStart, p, stream.ObjectNumber))
            return null;

        // Parse the stream dictionary respecting PDF lexical structure — skipping strings, comments, and
        // nested dictionaries — so a "stream" byte-run or a nested/in-string "/Length" cannot be mistaken
        // for the real keyword or the real length. Yields the position just past the top-level '>>' and the
        // dictionary's own top-level /Length (read from the source, since the loader rewrites the parsed
        // value to the corrected length).
        int dictStart = IndexOf(b, "<<"u8, offset);
        if (dictStart < 0)
            return null;
        if (ScanDictionary(b, dictStart, context) is not { } dict)
            return null; // unterminated / unparseable dictionary — skip
        (int dictEnd, long? length) = dict;

        int streamKw = SkipSpacesAndComments(b, dictEnd);
        if (!Matches(b, streamKw, StreamKeyword))
            return null; // the 'stream' keyword must follow the dictionary
        int afterStream = streamKw + StreamKeyword.Length;

        // Test 2a + data start: the byte(s) right after 'stream' must be CRLF or a single LF.
        bool streamKeywordCompliant;
        int dataStart;
        if (afterStream < b.Length && b[afterStream] == Cr)
        {
            if (afterStream + 1 < b.Length && b[afterStream + 1] == Lf)
            {
                streamKeywordCompliant = true;
                dataStart = afterStream + 2; // CRLF
            }
            else
            {
                streamKeywordCompliant = false;
                dataStart = afterStream + 1; // lone CR — consumed, but non-compliant
            }
        }
        else if (afterStream < b.Length && b[afterStream] == Lf)
        {
            streamKeywordCompliant = true;
            dataStart = afterStream + 1; // single LF
        }
        else
        {
            streamKeywordCompliant = false;
            dataStart = afterStream; // no EOL — nothing skipped
        }

        // Locate the 'endstream' keyword: trust /Length first (seek to dataStart+Length and confirm), else
        // scan forward from the data start for the first 'endstream' — mirroring the reference validator so
        // an 'endstream' byte-sequence inside binary data resolves the same way for both.
        int endstream = -1;
        if (length is { } declared && declared >= 0 && dataStart + declared <= b.Length)
        {
            int candidate = SkipSpaces(b, (int)(dataStart + declared));
            if (Matches(b, candidate, EndStreamKeyword))
                endstream = candidate;
        }
        if (endstream < 0)
            endstream = ScanForEndstream(b, dataStart);
        if (endstream < 0 || endstream == 0)
            return null; // no locatable endstream — skip

        // Test 2b + realLength: the byte immediately before 'endstream' must be an EOL marker; realLength is
        // the data span minus that marker, with the CRLF/CR-as-data disambiguation resolved via /Length.
        int size = endstream - dataStart;
        long len = length ?? 0;
        bool endstreamKeywordCompliant;
        int eolBytes;
        if (b[endstream - 1] == Lf)
        {
            endstreamKeywordCompliant = true;
            eolBytes = endstream - 2 >= 0 && b[endstream - 2] == Cr ? (size - len > 1 ? 2 : 1) : 1;
        }
        else if (b[endstream - 1] == Cr)
        {
            endstreamKeywordCompliant = true;
            eolBytes = 1;
        }
        else
        {
            endstreamKeywordCompliant = false;
            eolBytes = 0;
        }
        long realLength = size - eolBytes;

        // Test 1 — only when /Length is a resolvable number (a missing/non-numeric one is another clause's
        // concern; flagging it would risk a false positive from a Length we simply failed to resolve).
        if (length is { } lengthValue && lengthValue != realLength)
            return $"the /Length value {lengthValue} does not match the actual stream length "
                 + $"of {realLength} byte(s)";
        if (!streamKeywordCompliant)
            return "the 'stream' keyword is not followed by a CARRIAGE RETURN and LINE FEED or a single LINE FEED";
        if (!endstreamKeywordCompliant)
            return "the 'endstream' keyword is not immediately preceded by an EOL marker";

        return null;
    }

    /// <summary>
    /// Walks the dictionary starting at the <c>&lt;&lt;</c> at <paramref name="dictStart"/>, honouring string
    /// literals, hex strings, comments, and dictionary nesting, and returns the index just past the matching
    /// top-level <c>&gt;&gt;</c> together with the dictionary's own top-level <c>/Length</c> value (a direct
    /// integer, or an indirect reference resolved to its integer). Length is null when absent, non-numeric,
    /// or unresolvable — leaving test 1 unchecked rather than risking a false positive. Returns null when the
    /// dictionary does not start at <paramref name="dictStart"/> or never closes.
    /// </summary>
    private static (int DictEnd, long? Length)? ScanDictionary(byte[] b, int dictStart, ConformanceContext context)
    {
        if (!Matches(b, dictStart, "<<"u8))
            return null;

        int depth = 1;
        int i = dictStart + 2;
        long? length = null;

        while (i < b.Length)
        {
            byte c = b[i];
            if (IsWhitespace(c)) { i++; continue; }

            if (c == (byte)'%') // comment to end of line
            {
                i++;
                while (i < b.Length && b[i] != Cr && b[i] != Lf) i++;
            }
            else if (c == (byte)'(')
            {
                i = SkipLiteralString(b, i);
            }
            else if (c == (byte)'<' && i + 1 < b.Length && b[i + 1] == (byte)'<')
            {
                depth++;
                i += 2;
            }
            else if (c == (byte)'<')
            {
                i = SkipHexString(b, i);
            }
            else if (c == (byte)'>' && i + 1 < b.Length && b[i + 1] == (byte)'>')
            {
                depth--;
                i += 2;
                if (depth == 0)
                    return (i, length);
            }
            else if (c == (byte)'/')
            {
                int nameStart = i + 1;
                int j = nameStart;
                while (j < b.Length && IsRegular(b[j])) j++;
                bool isTopLevelLength = depth == 1 && j - nameStart == 6 && Matches(b, nameStart, "Length"u8);
                i = j;
                if (isTopLevelLength)
                    length = ReadLengthValue(b, ref i, context);
            }
            else
            {
                i++; // a number, keyword, or array bracket — advance past this byte
            }
        }
        return null; // unterminated dictionary
    }

    /// <summary>Reads the value following a top-level <c>/Length</c> at <paramref name="i"/>: a direct integer,
    /// or an indirect reference <c>N G R</c> resolved to its integer. Advances <paramref name="i"/> past a
    /// value it consumes; returns null (and leaves <paramref name="i"/> put) for anything else.</summary>
    private static long? ReadLengthValue(byte[] b, ref int i, ConformanceContext context)
    {
        int p = SkipSpaces(b, i);
        int numStart = p;
        while (p < b.Length && IsDigit(b[p])) p++;
        if (p == numStart)
            return null; // not a number (an array, name, dict, …) — leave for the main walk
        long first = ParseLong(b, numStart, p);

        int q = SkipSpaces(b, p);
        int genStart = q;
        while (q < b.Length && IsDigit(b[q])) q++;
        if (q > genStart)
        {
            int r = SkipSpaces(b, q);
            if (r < b.Length && b[r] == (byte)'R') // indirect reference N G R
            {
                i = r + 1;
                return first is >= 0 and <= int.MaxValue
                    && context.Document.GetObject((int)first) is PdfInteger li
                    ? li.LongValue
                    : null;
            }
        }
        i = p;
        return first; // direct integer
    }

    /// <summary>Advances past a literal string beginning at the <c>(</c> at <paramref name="i"/>, honouring
    /// balanced parentheses and backslash escapes; returns the index just past the closing <c>)</c>.</summary>
    private static int SkipLiteralString(byte[] b, int i)
    {
        int depth = 0;
        while (i < b.Length)
        {
            byte c = b[i];
            if (c == (byte)'\\') { i += 2; continue; } // escape — skip the next byte too
            if (c == (byte)'(') depth++;
            else if (c == (byte)')') { depth--; if (depth == 0) return i + 1; }
            i++;
        }
        return i;
    }

    /// <summary>Advances past a hex string beginning at the single <c>&lt;</c> at <paramref name="i"/>;
    /// returns the index just past the closing <c>&gt;</c>.</summary>
    private static int SkipHexString(byte[] b, int i)
    {
        i++;
        while (i < b.Length && b[i] != (byte)'>') i++;
        return i < b.Length ? i + 1 : i;
    }

    private static int SkipSpacesAndComments(byte[] b, int p)
    {
        while (p < b.Length)
        {
            if (IsWhitespace(b[p])) { p++; }
            else if (b[p] == (byte)'%') { while (p < b.Length && b[p] != Cr && b[p] != Lf) p++; }
            else break;
        }
        return p;
    }

    private static int IndexOf(byte[] b, ReadOnlySpan<byte> needle, int from)
    {
        for (int i = from; i + needle.Length <= b.Length; i++)
            if (b.AsSpan(i, needle.Length).SequenceEqual(needle))
                return i;
        return -1;
    }

    private static long ParseLong(byte[] b, int from, int to)
    {
        long value = 0;
        for (int i = from; i < to; i++)
        {
            value = value * 10 + (b[i] - '0');
            if (value < 0) return long.MaxValue; // overflow guard
        }
        return value;
    }

    // A regular character: any byte that is neither white-space nor a PDF delimiter — the bytes a name is made of.
    private static bool IsRegular(byte x) =>
        !IsWhitespace(x) && x is not ((byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'['
            or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%');

    /// <summary>The first <c>endstream</c> keyword at/after <paramref name="from"/>, or -1.</summary>
    private static int ScanForEndstream(byte[] b, int from)
    {
        int limit = b.Length - EndStreamKeyword.Length;
        for (int i = Math.Max(from, 0); i <= limit; i++)
            if (b[i] == 'e' && Matches(b, i, EndStreamKeyword))
                return i;
        return -1;
    }

    private static int SkipSpaces(byte[] b, int p)
    {
        while (p < b.Length && IsWhitespace(b[p])) p++;
        return p;
    }

    private static bool Matches(byte[] b, int pos, ReadOnlySpan<byte> needle) =>
        pos >= 0 && pos + needle.Length <= b.Length && b.AsSpan(pos, needle.Length).SequenceEqual(needle);

    private static bool DigitsEqual(byte[] b, int from, int to, int expected)
    {
        long value = 0;
        for (int i = from; i < to; i++)
        {
            value = value * 10 + (b[i] - '0');
            if (value > int.MaxValue) return false;
        }
        return value == expected;
    }

    private static bool IsDigit(byte x) => x is >= (byte)'0' and <= (byte)'9';

    // ISO 32000-1 Table 1 white-space characters (veraPDF's CharTable.isSpace set).
    private static bool IsWhitespace(byte x) => x is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20;
}
