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

    public FontRemediationProposal Propose(PdfDocument document, PreflightResult findings)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(findings);

        IReadOnlyList<FontInventoryEntry> inventory = FontInventory.Read(document);
        var proposals = new List<FontProposal>();
        var seen = new HashSet<(FontId, string)>();

        foreach (Finding finding in findings.Findings)
        {
            if (!HandledRules.Contains(finding.RuleId)) continue;
            if (finding.ObjectNumber is not { } objectNumber) continue;
            if (FontInventory.Find(inventory, objectNumber) is not { } entry) continue;
            if (!seen.Add((entry.Id, finding.RuleId))) continue;

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

        // Constructed once, not once per code: Type1Font's constructor (and its siblings) eagerly
        // parses /Encoding, /ToUnicode and /Widths — work that is invariant across every code this
        // font draws, so re-running it per code is pure waste on a subset font with many used codes.
        var provable = new Dictionary<int, string>();
        var needsInput = new List<int>();

        if (document.GetObject(entry.Id.ObjectNumber) is PdfDictionary dictionary
            && PdfFont.Create(dictionary, document) is { } font)
        {
            foreach (int code in entry.UsedCodes.Distinct().OrderBy(c => c))
            {
                if (ProvableUnicode(font, code) is { } text)
                    provable[code] = text;
                else
                    needsInput.Add(code);
            }
        }
        else
        {
            // The font object could not be resolved/parsed — every used code is unprovable, same as
            // if each one individually failed derivation.
            needsInput.AddRange(entry.UsedCodes.Distinct().OrderBy(c => c));
        }

        return new ToUnicodeProposal(entry.Id, ruleId, provable, needsInput);
    }

    /// <summary>
    /// A Unicode value DERIVED from the font's own declarations — an EXISTING <c>/ToUnicode</c> entry
    /// for the code (the file already answering the question — not an inference, so admitting it does
    /// not weaken the no-invention rule), or failing that, the encoding's glyph name through the Adobe
    /// Glyph List or the uniXXXX/uXXXXXX convention. Null when there is no honest answer. Uses
    /// <see cref="FontUnicodeMapping"/>'s own building blocks (<see cref="GlyphList"/>,
    /// <see cref="FontUnicodeMapping.UnicodeGlyphNameValue"/>,
    /// <see cref="FontUnicodeMapping.IsForbiddenUnicodeValue"/>) — the SAME source of truth
    /// <c>Pdfa2uToUnicodeRule</c>/<c>Pdfa2uToUnicodeValuesRule</c> consult via
    /// <see cref="FontUnicodeMapping.HasReliableUnicode"/> — so the planner and the rules cannot
    /// disagree about what counts as provable.
    ///
    /// <para>Consulting the existing entry FIRST matters for a partial <c>/ToUnicode</c> CMap
    /// (routine in subset fonts): <c>Pdfa2uToUnicodeRule</c> only flags a font's UNCOVERED codes
    /// (<c>HasReliableUnicode</c> returns true for any code that already has a mapping), but a
    /// proposal spans every code the font draws. Without this, a covered code whose glyph name is
    /// non-AGL would be re-derived, fail, and land in <c>NeedsUserInput</c> despite the document
    /// already knowing the answer — and because <c>PdfDocumentEditor.SetToUnicode</c> REPLACES the
    /// whole CMap rather than merging into it, the eventual fix would destroy a correct existing
    /// mapping the finding never even objected to.</para>
    ///
    /// <para>The embedded program's cmap is deliberately NOT consulted as a fallback. Reversing a
    /// (3,1) table is usually right and occasionally confidently wrong — a subsetted or symbolic
    /// cmap can map into the private use area — and "usually right" is the property that makes a
    /// wrong mapping ship. A wrong /ToUnicode is worse than none: it corrupts extraction AND
    /// satisfies the rule, so preflight goes green over a document that got worse.</para>
    /// </summary>
    private static string? ProvableUnicode(PdfFont font, int code)
    {
        // An existing /ToUnicode entry is itself a derivation — the file already answering the
        // question. EXCEPT where the value is forbidden: that is exactly the pdfa2u-tounicode-values
        // case, and the rule rejecting it IS the proof it is wrong, so it must not be proposed back.
        // A forbidden existing value carries no evidentiary weight either way, so treat it as ABSENT
        // and fall through to the glyph-name derivation below — the same fresh re-derivation that
        // fixes the finding, rather than giving up on a code the encoding may still answer honestly.
        if (font.ToUnicode?.Lookup(code) is { } existing && Provable(existing) is { } provableExisting)
            return provableExisting;

        // Composite (Type0) fonts have no derivable code-to-Unicode mapping without their own
        // /ToUnicode entry — even a registered Adobe ordering's CID-to-Unicode table is bundled
        // machinery HasReliableUnicode merely gives the benefit of the doubt to, not a derivation
        // this planner can stand behind as a proposed value.
        if (font is Type0Font) return null;

        string? glyphName = font.Encoding?.GetGlyphName(code);
        if (string.IsNullOrEmpty(glyphName) || glyphName == ".notdef")
            return null; // no positive evidence to derive FROM; a proposal needs an actual value.

        if (GlyphList.GetUnicode(glyphName) is { } fromAgl && !fromAgl.Contains(FontUnicodeMapping.ReplacementChar))
            return Provable(fromAgl);

        return FontUnicodeMapping.UnicodeGlyphNameValue(glyphName) is { } fromConvention
            ? Provable(fromConvention)
            : null;
    }

    /// <summary>A derived value that PDF/A-2u or PDF/UA-1 itself forbids is not provable — proposing
    /// it would stage the very value a rule rejects. <see cref="FontUnicodeMapping.IsForbiddenUnicodeValue"/>
    /// is PDF/A-2u's set; it is the superset consulted here regardless of which of the two handled
    /// rules triggered the finding, or whether the value came from an existing entry or a fresh
    /// derivation, since a value neither rule would accept is never worth proposing.</summary>
    private static string? Provable(string value) =>
        FontUnicodeMapping.IsForbiddenUnicodeValue(value) ? null : value;
}
