using PdfLibrary.Conformance;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Structure;

namespace PdfLibrary.Fonts.Remediation;

/// <summary>
/// Turns a document and its preflight findings into proposed font fixes. NEVER mutates the
/// document — that separation is what lets the app stage a proposal and let the user's ordinary
/// Save commit it.
///
/// <para>F-1 handles the two ToUnicode rules. F-2 (landed) adds <c>font-embedded</c>. F-3/F-4 extend
/// the switch in <see cref="Propose(PdfDocument, PreflightResult)"/> further; the shape does not
/// change.</para>
///
/// <para><paramref name="fonts"/> resolves a requested face to real system-font bytes for
/// <c>font-embedded</c> proposals. It is REQUIRED, not defaulted: a planner silently unable to
/// resolve fonts would decline every embed and look exactly like "no system fonts installed" — a
/// caller must choose a real provider explicitly.</para>
/// </summary>
public sealed class FontRemediationPlanner(ISystemFontProvider fonts)
{
    private static readonly HashSet<string> HandledRules =
        new(StringComparer.Ordinal) { "pdfa2u-tounicode", "pdfa2u-tounicode-values", "font-embedded" };

    public FontRemediationProposal Propose(PdfDocument document, PreflightResult findings)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(findings);

        // Findings without an ObjectNumber cannot be attributed to a font, and Propose(document,
        // IEnumerable<(string,int)>) below has no null-ObjectNumber case to represent — skipping them
        // here, before the projection, keeps that identically to what the old single-overload body did.
        IEnumerable<(string RuleId, int ObjectNumber)> projected = findings.Findings
            .Where(f => f.ObjectNumber is not null)
            .Select(f => (f.RuleId, f.ObjectNumber!.Value));

        return Propose(document, projected);
    }

    /// <summary>
    /// Same proposal logic as <see cref="Propose(PdfDocument, PreflightResult)"/>, but takes only the
    /// two fields the planner actually reads off a finding rather than a whole engine
    /// <see cref="PreflightResult"/>. A caller that has already mirrored preflight findings into its
    /// own type (Pellucid.Core's <c>PreflightReport</c>/<c>PreflightFinding</c>, for instance) should
    /// not have to reconstruct an engine <see cref="PreflightResult"/> — or re-run preflight — just to
    /// call this planner. Re-running would also risk planning against a different finding set than the
    /// one the caller is actually showing the user.
    /// </summary>
    public FontRemediationProposal Propose(
        PdfDocument document, IEnumerable<(string RuleId, int ObjectNumber)> findings)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(findings);

        IReadOnlyList<FontInventoryEntry> inventory = FontInventory.Read(document);
        var proposals = new List<FontProposal>();
        // Keyed on (Id.ObjectNumber, ProgramHolderId.ObjectNumber, RuleId) rather than just Id: FontId
        // is a single-field record struct (FontId(int ObjectNumber)), so keying on Id ALONE is really
        // still keying on the raw object number — and FontInventory.cs assigns FontId(0) to every
        // DIRECT (non-indirect) font dictionary as a sentinel, so two distinct direct dictionaries
        // that each have an indirect program holder would collide on Id==0 and silently drop the
        // second one's proposal. Including ProgramHolderId's object number distinguishes them because
        // it's real per FontInventoryEntry.BuildEntry (indirect dictionaries never share a program
        // holder). This is a workaround, not a fix: FontId(0) is an overloaded sentinel for "direct
        // dictionary" in the public read model, and disambiguating it properly (a nullable or
        // discriminated id) ripples through FontInventory's public surface — deferred, out of scope
        // for this task.
        var seen = new HashSet<(int, int?, string)>();

        foreach ((string ruleId, int objectNumber) in findings)
        {
            if (!HandledRules.Contains(ruleId)) continue;
            if (FontInventory.Find(inventory, objectNumber) is not { } entry) continue;
            if (!seen.Add((entry.Id.ObjectNumber, entry.ProgramHolderId?.ObjectNumber, ruleId))) continue;

            proposals.Add(ruleId == "font-embedded"
                ? ProposeEmbed(document, entry, ruleId)
                : ProposeToUnicode(document, entry, ruleId));
        }

        return new FontRemediationProposal(proposals);
    }

    /// <summary>
    /// Proposes embedding a font program for a <c>font-embedded</c> finding, or explains why it
    /// cannot. Every decline here is a FACT about this font on this machine (design §6.1) — never a
    /// policy position — so each reason names the specific cause, and the fsType (embedding
    /// restricted) check runs BEFORE the proposal is built, never after: a font whose vendor forbids
    /// embedding must not have its bytes carried in view state at all.
    ///
    /// <para>Internal rather than private: F-2 declines every composite font, so
    /// <c>entry.ProgramHolderId ?? entry.Id</c> can never observably diverge from
    /// <c>entry.Id</c> through <see cref="Propose(PdfDocument, IEnumerable{ValueTuple{string, int}})"/>
    /// in THIS increment — <see cref="FontInventory"/> only ever produces a <c>ProgramHolderId</c>
    /// different from <c>Id</c> for a Type0 font, and that kind declines one branch earlier. Exposing
    /// this lets a test hand-build a <see cref="FontInventoryEntry"/> whose <c>ProgramHolderId</c>
    /// differs from <c>Id</c> under a non-composite <c>Kind</c> and prove the targeting expression
    /// itself is correct, ahead of the composite-font increment that will make it reachable
    /// end-to-end through <c>Propose</c>.</para>
    /// </summary>
    internal FontProposal ProposeEmbed(PdfDocument document, FontInventoryEntry entry, string ruleId)
    {
        if (!entry.IsAddressable)
        {
            return Decline(entry, ruleId,
                "This font is written directly into the page's resources rather than as its own "
                + "object, so there is nothing to attach a font program to.");
        }

        if (entry.Kind is FontKind.Type0CidType0 or FontKind.Type0CidType2)
        {
            // Under Identity-H with /CIDToGIDMap /Identity the document's CIDs ARE the original
            // program's glyph indices, so a substitute with a different glyph order renders real
            // glyphs in the wrong places — plausible-looking garbage that errors nowhere. A later
            // increment's problem (design §3.2's own note); not attempted here.
            return Decline(entry, ruleId,
                "Substituting a program for a composite font would need its character-to-glyph "
                + "mapping rewritten in step, which Pellucid does not yet do.");
        }

        if (entry.Kind is FontKind.Type3)
        {
            return Decline(entry, ruleId, "Type 3 font glyphs are drawing instructions, not a font program.");
        }

        // Guaranteed non-null: IsAddressable required dict.IsIndirect && programHolder.IsIndirect,
        // and FontInventory only assigns a null ProgramHolderId when the program holder is NOT
        // indirect (FontInventory.BuildEntry).
        FontId programHolder = entry.ProgramHolderId ?? entry.Id;

        FontRequest request = BuildRequest(document, entry, programHolder);

        FontMatch? match = fonts.Resolve(request);
        if (match is null)
        {
            return Decline(entry, ruleId,
                $"No font matching '{entry.FamilyName}' is installed on this computer. Installing "
                + "it would let Pellucid embed it.");
        }

        // Runs the SAME byte-facing gates AssessCandidate uses on a caller-supplied candidate
        // (classify, fsType, Type 1 PFB segments, ISO 32000-2 Table 124 reconciliation) — extracted
        // so the automatic and manual paths cannot drift apart. ACCEPTED LIMITATION carried over from
        // before the extraction: the Table 124 decline for a CID-keyed CidFontType0C program in a
        // simple font is proven only compositionally (SimpleFontProgramSubtype's own unit tests drive
        // the throw directly) — there is no committed CID-keyed CFF fixture in this repo to exercise
        // the planner end-to-end through a real font program. See the CFF-fixture note in
        // PreflightSlice19Tests.cs for why one was not hand-authored.
        ByteGateOutcome gates = RunByteGates(match.Data, match.FaceIndex, entry.FamilyName);
        if (gates.HardBlockReason is not null)
        {
            return Decline(entry, ruleId, gates.HardBlockReason);
        }

        EmbeddedFontMetrics metrics = gates.Metrics!;
        string resolvedFamily = gates.ResolvedFamily!;
        ClassifiedProgram classified = gates.Classified!;

        // Checked AFTER fsType (a vendor's embedding restriction is a harder no than a coverage gap)
        // and BEFORE the proposal is built, for the same reason as every decline above: a glyph
        // missing from a RENDER is transient — reopening on a better-equipped machine fixes it — but
        // a glyph missing from an EMBEDDED program is .notdef baked into the file permanently
        // (GlyphCoverage's own doc comment). Reads entry.Id, not the program holder: for a composite
        // font those diverge, but composites decline two branches up, so entry.Id and programHolder
        // agree on everything reaching here.
        if (document.GetObject(entry.Id.ObjectNumber) is PdfDictionary fontDictionary
            && PdfFont.Create(fontDictionary, document) is { } pdfFont)
        {
            IReadOnlyList<int> uncovered =
                GlyphCoverage.UncoveredCodes(pdfFont, metrics, pdfFont.FirstChar, pdfFont.LastChar);
            if (uncovered.Count > 0)
            {
                string? glyphName = pdfFont.Encoding?.GetGlyphName(uncovered[0]);
                string? uni = string.IsNullOrEmpty(glyphName) ? null : GlyphList.GetUnicode(glyphName);
                int codePoint = string.IsNullOrEmpty(uni) ? 0 : char.ConvertToUtf32(uni, 0);

                return Decline(entry, ruleId,
                    $"'{resolvedFamily}' has no glyph for {uncovered.Count} character(s) this font "
                    + $"draws (first: U+{codePoint:X4}); embedding it would bake them in as .notdef.");
            }
        }

        string style = (metrics.IsBold, metrics.IsItalic) switch
        {
            (true, true) => "Bold Italic",
            (true, false) => "Bold",
            (false, true) => "Italic",
            (false, false) => "Regular",
        };

        return new EmbedProposal(
            programHolder, ruleId,
            $"{resolvedFamily} ({style}) — from your system fonts",
            classified.Program, classified.Format);
    }

    /// <summary>
    /// Runs a caller-supplied substitute face's bytes through the SAME gate chain
    /// <see cref="ProposeEmbed"/> uses, for the manual font picker: a user has already chosen this
    /// candidate on purpose, so a coverage shortfall or a Symbol/Latin mismatch is reported as a
    /// <see cref="CandidateAssessment.Warnings"/> entry rather than an outright decline — but fsType,
    /// an undeclared Type 1 PFB segment table, and an ISO 32000-2 Table 124 refusal remain hard blocks
    /// (<see cref="CandidateAssessment.HardBlockReason"/>): those are facts about what Pellucid can
    /// legally and mechanically write, not something the user's judgement can override.
    /// </summary>
    public CandidateAssessment AssessCandidate(
        PdfDocument document, FontInventoryEntry entry, string ruleId,
        byte[] candidateBytes, int faceIndex, string sourceDescription)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(candidateBytes);
        ArgumentNullException.ThrowIfNull(sourceDescription);

        // Entry-shape hard blocks mirror ProposeEmbed exactly, including reason strings: these are
        // facts about the DOCUMENT's font, independent of which candidate bytes were offered.
        if (!entry.IsAddressable)
        {
            return new CandidateAssessment(null,
                "This font is written directly into the page's resources rather than as its own "
                + "object, so there is nothing to attach a font program to.",
                [], null);
        }

        if (entry.Kind is FontKind.Type0CidType0 or FontKind.Type0CidType2)
        {
            return new CandidateAssessment(null,
                "Substituting a program for a composite font would need its character-to-glyph "
                + "mapping rewritten in step, which Pellucid does not yet do.",
                [], null);
        }

        if (entry.Kind is FontKind.Type3)
        {
            return new CandidateAssessment(null,
                "Type 3 font glyphs are drawing instructions, not a font program.", [], null);
        }

        FontId programHolder = entry.ProgramHolderId ?? entry.Id;

        ByteGateOutcome gates = RunByteGates(candidateBytes, faceIndex, entry.FamilyName);
        if (gates.HardBlockReason is not null)
        {
            return new CandidateAssessment(gates.Classified?.Format, gates.HardBlockReason, [], null);
        }

        EmbeddedFontMetrics metrics = gates.Metrics!;
        string resolvedFamily = gates.ResolvedFamily!;
        ClassifiedProgram classified = gates.Classified!;

        var warnings = new List<string>();

        // Coverage shortfall: the same GlyphCoverage check ProposeEmbed declines on, reported as a
        // consequence instead — the manual path lets the user accept .notdef glyphs on purpose.
        if (document.GetObject(entry.Id.ObjectNumber) is PdfDictionary fontDictionary
            && PdfFont.Create(fontDictionary, document) is { } pdfFont)
        {
            IReadOnlyList<int> uncovered =
                GlyphCoverage.UncoveredCodes(pdfFont, metrics, pdfFont.FirstChar, pdfFont.LastChar);
            if (uncovered.Count > 0)
            {
                string? glyphName = pdfFont.Encoding?.GetGlyphName(uncovered[0]);
                string? uni = string.IsNullOrEmpty(glyphName) ? null : GlyphList.GetUnicode(glyphName);
                int codePoint = string.IsNullOrEmpty(uni) ? 0 : char.ConvertToUtf32(uni, 0);

                warnings.Add(
                    $"'{resolvedFamily}' has no glyph for {uncovered.Count} character(s) this font "
                    + $"draws (first: U+{codePoint:X4}); they will embed as .notdef.");
            }
        }

        // Symbol/Dingbats mismatch: the entry's /BaseFont aliases to Symbol or ZapfDingbats (via the
        // SAME Base35Aliases.Split ProposeEmbed's system-font search uses to name a face), but the
        // candidate carries no symbol-encoded cmap (Windows platform, Symbol encoding) — so its codes
        // would render through whatever Latin glyphs those raw byte values happen to hit.
        (string aliasFamily, _, _) = Base35Aliases.Split(entry.BaseFont);
        if ((aliasFamily.Equals("symbol", StringComparison.OrdinalIgnoreCase)
                || aliasFamily.Equals("zapfdingbats", StringComparison.OrdinalIgnoreCase))
            && !metrics.HasSymbolCmapEncoding())
        {
            warnings.Add(
                "this font is symbol-encoded; a Latin text face will render its content as garbage.");
        }

        var proposal = new EmbedProposal(
            programHolder, ruleId, sourceDescription, classified.Program, classified.Format);

        return new CandidateAssessment(classified.Format, null, warnings, proposal);
    }

    /// <summary>
    /// The byte-facing gates shared by <see cref="ProposeEmbed"/> and <see cref="AssessCandidate"/>:
    /// classify the program, read its metrics, and check fsType embedding restriction, Type 1 PFB
    /// segment declaration, and ISO 32000-2 Table 124 reconciliation, in that order — extracted from
    /// <see cref="ProposeEmbed"/> so the automatic and manual paths cannot silently drift apart. Every
    /// reason string here is verbatim what <see cref="ProposeEmbed"/> produced before the extraction:
    /// its own tests pin them, and this refactor must not move them.
    /// </summary>
    private static ByteGateOutcome RunByteGates(byte[] bytes, int faceIndex, string familyName)
    {
        ClassifiedProgram? classified = FontProgramClassifier.Classify(bytes, faceIndex);
        if (classified is null)
        {
            return new ByteGateOutcome(null, null, null,
                $"The font file found for '{familyName}' is not in a format Pellucid can embed.");
        }

        // Reads Os2/Head via the SAME internal metrics reader FontDescriptorMetrics.Compute and
        // PdfDocumentEditor.WriteDerivedWidths use, so this cannot disagree with what those consult
        // later. Never on the request's bytes — always on classified.Program, what will actually be
        // written (design §7; §6.2 rejected re-resolving at apply time for exactly this reason).
        EmbeddedFontMetrics metrics = classified.Format == FontProgramFormat.Type1
            ? new EmbeddedFontMetrics(classified.Program, length1: 0, length2: 0, length3: 0)
            : new EmbeddedFontMetrics(classified.Program);

        string resolvedFamily = metrics.FamilyName ?? familyName;

        // Checked BEFORE the proposal is built (design requirement, restated in the class doc): a
        // font whose vendor forbids embedding must not have its bytes carried in view state at all.
        if (metrics.Os2?.EmbeddingRestricted == true)
        {
            return new ByteGateOutcome(classified, metrics, resolvedFamily,
                $"'{resolvedFamily}' is licensed by its vendor with embedding restricted, so Pellucid "
                + "will not embed it.");
        }

        // Calls the SAME validation PdfDocumentEditor.EmbedProgram runs (Type1PfbSegments.Split,
        // shared rather than mirrored) so the two cannot diverge: a bare PFA with no PFB segment
        // markers, a corrupt segment table, or a PFB with no binary segment all throw
        // NotSupportedException there — declined here instead, so a proposal never reaches Save only
        // to throw there. The split result itself is discarded; only whether it succeeds matters, since
        // EmbedProgram (Task 5) re-splits the SAME bytes when the proposal is actually applied.
        if (classified.Format == FontProgramFormat.Type1)
        {
            try
            {
                Type1PfbSegments.Split(classified.Program);
            }
            catch (NotSupportedException)
            {
                return new ByteGateOutcome(classified, metrics, resolvedFamily,
                    $"The Type 1 program found for '{familyName}' does not declare its segment "
                    + "lengths, which are required to embed it.");
            }
        }

        // Calls the SAME reconciliation PdfDocumentEditor.EmbedProgram runs
        // (SimpleFontProgramSubtype.Resolve, shared rather than mirrored) for the SAME reason the
        // Type1PfbSegments check above exists: the editor refuses a program ISO 32000-2 Table 124
        // permits in no simple font dictionary (a CID-keyed CFF, an OpenType program whose shape
        // cannot be read), and a proposal that survived to throw at Save time would be a crash where
        // an honest decline belongs. Everything reaching here is a SIMPLE font — composites declined
        // by the caller before RunByteGates is ever invoked — so the simple-font question is the right
        // one to ask. The current subtype is passed as null deliberately: it only chooses between
        // /Type1 and /MMType1 for a program this ACCEPTS, and never affects whether it throws, so the
        // planner does not need to have resolved the dictionary to predict a refusal. The answer
        // itself is discarded; only whether it succeeds matters, since EmbedProgram re-resolves it
        // when the proposal is applied.
        try
        {
            SimpleFontProgramSubtype.Resolve(classified.Format, classified.Program, currentSubtype: null);
        }
        catch (NotSupportedException ex)
        {
            return new ByteGateOutcome(classified, metrics, resolvedFamily,
                $"The font program found for '{familyName}' cannot be embedded in this font: "
                + ex.Message);
        }

        return new ByteGateOutcome(classified, metrics, resolvedFamily, null);
    }

    /// <summary>Result of <see cref="RunByteGates"/>: the classified program and its metrics whenever
    /// classification succeeded (even if a later gate then hard-blocked it — a caller may still want
    /// <see cref="Classified"/>'s <see cref="ClassifiedProgram.Format"/> to report), and the first
    /// hard-block reason, if any.</summary>
    private readonly record struct ByteGateOutcome(
        ClassifiedProgram? Classified,
        EmbeddedFontMetrics? Metrics,
        string? ResolvedFamily,
        string? HardBlockReason);

    /// <summary>
    /// The face to search for. <paramref name="entry"/>'s <c>FamilyName</c> (<c>/BaseFont</c> with any
    /// subset tag stripped) is the search term; Bold/Italic are derived preferentially from that
    /// name's own suffix convention (<c>-Bold</c>, <c>,Italic</c>, <c>BoldItalic</c> — a stronger
    /// signal than a flags bit), falling back to the program holder's <c>/FontDescriptor</c> Italic
    /// flag (bit 6, 0x40) and a non-zero <c>/ItalicAngle</c> when the name says nothing.
    /// </summary>
    private static FontRequest BuildRequest(PdfDocument document, FontInventoryEntry entry, FontId programHolder)
    {
        string name = entry.FamilyName;
        string lower = name.ToLowerInvariant();
        bool bold = lower.Contains("bold");
        bool italic = lower.Contains("italic") || lower.Contains("oblique");

        if (!italic
            && document.GetObject(programHolder.ObjectNumber) is PdfDictionary holderDict
            && Resolve(document, holderDict.Get("FontDescriptor")) is PdfDictionary descriptor)
        {
            if (Resolve(document, descriptor.Get("Flags")) is PdfInteger flags && (flags.Value & 0x40) != 0)
                italic = true;

            PdfObject? italicAngle = Resolve(document, descriptor.Get("ItalicAngle"));
            if (italicAngle is PdfReal { Value: not 0 } or PdfInteger { Value: not 0 })
                italic = true;
        }

        return new FontRequest(name, bold, italic);
    }

    private static DeclineProposal Decline(FontInventoryEntry entry, string ruleId, string reason) =>
        new(entry.Id, ruleId, reason);

    private static PdfObject? Resolve(PdfDocument document, PdfObject? obj) =>
        obj is PdfIndirectReference reference ? document.GetObject(reference.ObjectNumber) : obj;

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
