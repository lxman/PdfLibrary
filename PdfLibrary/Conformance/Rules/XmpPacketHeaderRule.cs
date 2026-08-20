using System.Collections.Generic;
using System.Text;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// The XMP packet HEADER processing instruction must not carry the <c>bytes</c> or <c>encoding</c>
/// attributes (ISO 19005-2, 6.6.2.1 tests 2 and 3). Both exist in the XMP specification to let a
/// reader find and decode a packet embedded in an arbitrary file; inside a PDF the stream already
/// carries its own length and PDF/A already fixes the encoding as UTF-8, so a value here is at best
/// redundant and at worst contradicts the stream it sits in.
///
/// <para><b>Deliberately its own RuleId, not folded into <see cref="MetadataPresentRule"/>'s
/// <c>metadata</c>.</b> That id is owned by Pellucid's <c>MetadataDomain</c>, whose repair
/// SYNTHESIZES a replacement packet from the Info dictionary. That is the right fix for "the catalog
/// has no /Metadata at all" and a destructive one here — it would discard a document's real XMP to
/// correct one attribute in its header. A finding routed to a domain that would mis-repair it is
/// worse than a finding with no repair offered, so this reports and offers nothing.</para>
///
/// <para><b>Scope: the header only.</b> The trailer (<c>&lt;?xpacket end='w'?&gt;</c>) is a different
/// instruction with its own legal attribute, and the clause names the header.</para>
/// </summary>
internal sealed class XmpPacketHeaderRule : IConformanceRule
{
    public string RuleId => "xmp-packet-header";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        PdfStream? metadata = context.Document.GetCatalog()?.GetMetadata();
        if (metadata is null)
            yield break;                       // MetadataPresentRule owns the absent-stream case.

        if (PacketHeader(metadata.GetDecodedData(context.Document.Decryptor)) is not { } header)
            yield break;

        foreach (string attribute in ForbiddenAttributes(header))
        {
            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "6.6.2.1"),
                Message = $"The XMP packet header carries the '{attribute}' attribute, which PDF/A "
                          + "does not permit.",
                ObjectNumber = metadata.IsIndirect ? metadata.ObjectNumber : null,
            };
        }
    }

    /// <summary>The text between <c>&lt;?xpacket</c> and the first <c>?&gt;</c>, or null when the
    /// bytes carry no header instruction.
    ///
    /// <para>Decoded as Latin-1 so every byte maps to a char and nothing throws on a packet that is
    /// not valid UTF-8 — the structure being scanned is pure ASCII. A UTF-16 or UTF-32 packet
    /// therefore finds no header (its <c>&lt;?xpacket</c> is NUL-interleaved) and this rule stays
    /// silent. That is the deliberate direction to fail: silence is a coverage gap, whereas guessing
    /// at a wide encoding risks reporting an attribute on a conforming file, and this preflighter's
    /// standing property is that it never rejects a file veraPDF accepts.</para></summary>
    private static string? PacketHeader(byte[] xmpBytes)
    {
        string text = Encoding.Latin1.GetString(xmpBytes);

        int start = text.IndexOf("<?xpacket", System.StringComparison.Ordinal);
        if (start < 0) return null;

        int end = text.IndexOf("?>", start, System.StringComparison.Ordinal);
        return end < 0 ? null : text[(start + "<?xpacket".Length)..end];
    }

    /// <summary>The forbidden attribute names actually present, in header order.
    ///
    /// <para>Attribute VALUES are skipped wholesale rather than searched, so a value that happens to
    /// contain <c>bytes=</c> cannot be mistaken for the attribute — the real header's
    /// <c>begin</c> value is an arbitrary BOM string and its <c>id</c> is opaque, so scanning the
    /// raw text for a name would be reading data as structure.</para></summary>
    private static IEnumerable<string> ForbiddenAttributes(string header)
    {
        var i = 0;
        while (i < header.Length)
        {
            while (i < header.Length && char.IsWhiteSpace(header[i])) i++;

            int nameStart = i;
            while (i < header.Length && (char.IsLetterOrDigit(header[i]) || header[i] is '-' or '_' or ':'))
                i++;
            if (i == nameStart) { i++; continue; }        // not a name — resync a char at a time
            string name = header[nameStart..i];

            while (i < header.Length && char.IsWhiteSpace(header[i])) i++;
            if (i >= header.Length || header[i] != '=') continue;
            i++;
            while (i < header.Length && char.IsWhiteSpace(header[i])) i++;
            if (i >= header.Length) break;

            char quote = header[i];
            if (quote is not ('"' or '\'')) continue;     // unquoted value — not well-formed; skip
            int valueEnd = header.IndexOf(quote, i + 1);
            if (valueEnd < 0) break;                      // unterminated value — stop, do not guess
            i = valueEnd + 1;

            if (name is "bytes" or "encoding") yield return name;
        }
    }
}
