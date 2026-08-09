using System.Globalization;
using PdfLibrary.Conformance;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Fonts.Remediation;

/// <summary>
/// Turns a document and its preflight findings into proposed font fixes. NEVER mutates the
/// document — that separation is what lets the app stage a proposal and let the user's ordinary
/// Save commit it.
///
/// <para>F-1 handles the two ToUnicode rules. F-2/F-3/F-4 extend the switch in
/// <see cref="Propose"/>; the shape does not change.</para>
/// </summary>
public sealed class FontRemediationPlanner
{
    private static readonly HashSet<string> HandledRules =
        new(StringComparer.Ordinal) { "pdfa2u-tounicode", "pdfa2u-tounicode-values" };

    // Mirrors FontUnicodeMapping's private ReplacementChar (U+FFFD) — the GlyphList ".notdef"
    // marker. A glyph name that only resolves to this sentinel carries no real Unicode value, the
    // same exclusion HasReliableUnicode applies before trusting a GlyphList hit.
    private const char ReplacementChar = '\uFFFD';

    public FontRemediationProposal Propose(PdfDocument document, PreflightResult findings)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(findings);

        IReadOnlyList<FontInventoryEntry> inventory = FontInventory.Read(document);
        var proposals = new List<FontProposal>();
        var seen = new HashSet<(int, string)>();

        foreach (Finding finding in findings.Findings)
        {
            if (!HandledRules.Contains(finding.RuleId)) continue;
            if (finding.ObjectNumber is not { } objectNumber) continue;
            if (FontInventory.Find(inventory, objectNumber) is not { } entry) continue;
            if (!seen.Add((entry.Id.ObjectNumber, finding.RuleId))) continue;

            proposals.Add(ProposeToUnicode(document, entry, finding.RuleId));
        }

        return new FontRemediationProposal(proposals);
    }

    private static FontProposal ProposeToUnicode(
        PdfDocument document, FontInventoryEntry entry, string ruleId)
    {
        if (!entry.IsAddressable)
        {
            return new DeclineProposal(entry.Id, ruleId,
                $"'{entry.FamilyName}' is written directly into a page's resources rather than as a "
                + "shared object, so Pellucid cannot address it to write a /ToUnicode entry.");
        }

        var provable = new Dictionary<int, string>();
        var needsInput = new List<int>();

        foreach (int code in entry.UsedCodes.Distinct().OrderBy(c => c))
        {
            if (ProvableUnicode(document, entry, code) is { } text)
                provable[code] = text;
            else
                needsInput.Add(code);
        }

        return new ToUnicodeProposal(entry.Id, ruleId, provable, needsInput);
    }

    /// <summary>
    /// A Unicode value DERIVED from the font's own declarations — the encoding's glyph name through
    /// the Adobe Glyph List, or the uniXXXX/uXXXXXX convention. Null when there is no honest answer.
    /// Uses <see cref="FontUnicodeMapping"/>'s own building blocks (<see cref="GlyphList"/> and
    /// <see cref="FontUnicodeMapping.IsForbiddenUnicodeValue"/>) — the SAME source of truth
    /// <c>Pdfa2uToUnicodeRule</c>/<c>Pdfa2uToUnicodeValuesRule</c> consult via
    /// <see cref="FontUnicodeMapping.HasReliableUnicode"/> — so the planner and the rules cannot
    /// disagree about what counts as provable.
    ///
    /// <para>The embedded program's cmap is deliberately NOT consulted as a fallback. Reversing a
    /// (3,1) table is usually right and occasionally confidently wrong — a subsetted or symbolic
    /// cmap can map into the private use area — and "usually right" is the property that makes a
    /// wrong mapping ship. A wrong /ToUnicode is worse than none: it corrupts extraction AND
    /// satisfies the rule, so preflight goes green over a document that got worse.</para>
    /// </summary>
    private static string? ProvableUnicode(PdfDocument document, FontInventoryEntry entry, int code)
    {
        if (document.GetObject(entry.Id.ObjectNumber) is not PdfDictionary dictionary) return null;
        if (PdfFont.Create(dictionary, document) is not { } font) return null;

        // Composite (Type0) fonts have no derivable code-to-Unicode mapping without their own
        // /ToUnicode entry — even a registered Adobe ordering's CID-to-Unicode table is bundled
        // machinery HasReliableUnicode merely gives the benefit of the doubt to, not a derivation
        // this planner can stand behind as a proposed value.
        if (font is Type0Font) return null;

        string? glyphName = font.Encoding?.GetGlyphName(code);
        if (string.IsNullOrEmpty(glyphName) || glyphName == ".notdef")
            return null; // no positive evidence to derive FROM; a proposal needs an actual value.

        if (GlyphList.GetUnicode(glyphName) is { } fromAgl && !fromAgl.Contains(ReplacementChar))
            return Provable(fromAgl);

        return UnicodeGlyphNameValue(glyphName) is { } fromConvention
            ? Provable(fromConvention)
            : null;
    }

    /// <summary>A derived value that PDF/A-2u or PDF/UA-1 itself forbids is not provable — proposing
    /// it would stage the very value a rule rejects. <see cref="FontUnicodeMapping.IsForbiddenUnicodeValue"/>
    /// is PDF/A-2u's set; it is the superset consulted here regardless of which of the two handled
    /// rules triggered the finding, since a value neither rule would accept is never worth proposing.</summary>
    private static string? Provable(string value) =>
        FontUnicodeMapping.IsForbiddenUnicodeValue(value) ? null : value;

    /// <summary>The "uXXXXXX" convention (a literal 'u' followed by 4–6 hex digits; "uniXXXX" is
    /// already resolved by <see cref="GlyphList.GetUnicode"/>). Mirrors the validation
    /// <c>FontUnicodeMapping</c>'s private <c>IsUnicodeGlyphName</c> performs — that predicate has no
    /// public value-producing counterpart, so the value is derived here from the same, narrow rule:
    /// the code point IS the hex digits.</summary>
    private static string? UnicodeGlyphNameValue(string name)
    {
        if (name.Length is < 5 or > 7 || name[0] != 'u') return null;
        for (var i = 1; i < name.Length; i++)
            if (!Uri.IsHexDigit(name[i])) return null;

        if (!int.TryParse(name.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int codePoint))
            return null;

        try
        {
            return char.ConvertFromUtf32(codePoint);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null; // a syntactically-valid hex run that is not a valid Unicode scalar value
        }
    }
}
