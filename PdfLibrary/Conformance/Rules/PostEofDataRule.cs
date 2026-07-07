namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A: no data may follow the last end-of-file marker except a single optional end-of-line marker
/// (ISO 19005-2, 6.1.3, test 3 — <c>postEOFDataSize == 0</c>; references ISO 32000-1, 7.5.5). This is a
/// byte-level rule and requires the source bytes; when they are unavailable it reports an informational
/// note rather than failing.
/// </summary>
internal sealed class PostEofDataRule : IConformanceRule
{
    private static readonly byte[] Eof = "%%EOF"u8.ToArray();

    public string RuleId => "post-eof";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        byte[]? bytes = context.SourceBytes;
        if (bytes is null)
        {
            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Info,
                Clause = ConformanceClauses.For(context.Target, "6.1.3"),
                Message = "Post-EOF data was not checked because the source bytes were unavailable "
                          + "(the document was inspected in memory).",
            };
            yield break;
        }

        int eofStart = LastIndexOf(bytes, Eof);
        if (eofStart < 0)
            yield break; // absence of an EOF marker is a different rule's concern

        int extra = ExtraBytesAfter(bytes, eofStart + Eof.Length);
        if (extra > 0)
        {
            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "6.1.3"),
                Message = $"{extra} byte(s) of data follow the last %%EOF marker; only a single optional "
                          + "end-of-line marker is permitted.",
            };
        }
    }

    /// <summary>Bytes remaining after position <paramref name="pos"/> once a single optional EOL is skipped.</summary>
    private static int ExtraBytesAfter(byte[] bytes, int pos)
    {
        if (pos < bytes.Length && bytes[pos] == 0x0D) // CR, optionally CRLF
        {
            pos++;
            if (pos < bytes.Length && bytes[pos] == 0x0A) pos++;
        }
        else if (pos < bytes.Length && bytes[pos] == 0x0A) // LF
        {
            pos++;
        }

        return bytes.Length - pos;
    }

    private static int LastIndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = haystack.Length - needle.Length; i >= 0; i--)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                return i;
        }
        return -1;
    }
}
