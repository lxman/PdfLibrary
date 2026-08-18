using System.Text.RegularExpressions;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
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
        new(StringComparer.Ordinal)
        {
            "pdfa2u-tounicode", "pdfa2u-tounicode-values", "font-embedded", "font-subset-coverage",
            "font-program",
        };

    /// <summary>Single source for the "a simple font's finding is a missing glyph" decline — used by
    /// <see cref="ProposeWidthPatch"/>'s dispatch (a simple font's notdef-only finding), and again as a
    /// defensive gate inside <see cref="ProposeProgramReplace"/> and <see cref="AssessReplacementCandidate"/>
    /// for a non-composite entry reached directly rather than through the dispatch (v1 scope: replacing
    /// a simple font's program is not something Pellucid does yet).</summary>
    private const string SimpleFontMissingGlyphReason =
        "This font's finding is a missing glyph, and replacing a simple font's program is not "
        + "something Pellucid does yet.";

    /// <summary>Issue 40 honesty: the decline for a font that draws CID 0 — see
    /// <see cref="ProposeProgramReplace"/>'s cid0 gate. Verbatim string is load-bearing: a later
    /// sweep taxonomy keys on it. The string still reads accurately even though (controller ruling,
    /// 2026-08-17 review) the gate no longer requires CID 0 to be the font's ONLY dead code — see
    /// the gate's own doc comment for why.</summary>
    private const string Cid0OnlyDeclineReason =
        "This font draws character code 0, which ISO 32000 defines as .notdef regardless of what "
        + "glyph the font maps it to — no font-side fix can make that draw conformant.";

    /// <summary>The dead-code predicate <see cref="BuildReplacement"/>'s <c>RestoredCodeCount</c>
    /// needs — reading the OLD program's CID→GID resolution the same way
    /// <see cref="Conformance.Rules.FontProgramRule.CheckType0"/> does (CID-keyed CFF via the
    /// charset, CIDFontType2 via /CIDToGIDMap). Deliberately NOT the rule's own <c>code == 0</c>
    /// addition (issue 40): <c>RestoredCodeCount</c> counts codes the SUBSTITUTE's map can actually
    /// change the rule's verdict on, and a used CID 0 can never leave .notdef regardless of what the
    /// substitute's map assigns it — counting it as "restored" would overstate what the replacement
    /// achieves. (Not used by the cid0 honesty gate below, which short-circuits on CID 0's mere
    /// presence rather than asking this predicate anything.)</summary>
    private static bool MapsToNotdefGlyph(int code, bool cidKeyed, CidFont cid, EmbeddedFontMetrics metrics) =>
        (cidKeyed ? metrics.GetGlyphIdByCid((ushort)code) : cid.MapCidToGid(code)) == 0;

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

        // font-program findings carry three sub-clauses under ONE rule id, and the projected tuples
        // have no clause — so the planner re-derives the sub-clause map by running the rule itself
        // (the rule IS the oracle; a second hand-rolled predicate would drift). Lazy: only documents
        // with a font-program finding pay for it. Always evaluated under PdfA2b — the A-2/UA-1
        // sub-numbers are identical (FontProgramRule's own class doc), and FontInventory.Read already
        // reads inventory under a PdfA2b context for the same reason.
        var fontProgramFindings = new Lazy<ILookup<int, Finding>>(() =>
            new FontProgramRule()
                .Check(new ConformanceContext(document, ConformanceProfile.PdfA2b))
                .Where(f => f.ObjectNumber is not null)
                .ToLookup(f => f.ObjectNumber!.Value));

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

        // Pass 1: resolve every distinct (ruleId, entry) pair this call was given, in input order,
        // AND — for font-program findings that are notdef/composite-eligible (the same condition
        // ProposeWidthPatch itself gates on below) — group them by HolderGroupKey (issue 38). Grouping
        // is scoped to what THIS CALL was given: a caller that passes only ONE of two sharing wrappers'
        // findings gets the plain singleton path for it, unaware of the sibling — see
        // ReplaceProgramProposalTests' promoted guard-era tests for that shape.
        var resolved = new List<(string RuleId, FontInventoryEntry Entry)>();
        var memberOfGroup = new Dictionary<FontId, long>();
        var groupMembers = new Dictionary<long, List<FontInventoryEntry>>();

        foreach ((string ruleId, int objectNumber) in findings)
        {
            if (!HandledRules.Contains(ruleId)) continue;
            if (FontInventory.Find(inventory, objectNumber) is not { } entry) continue;
            if (!seen.Add((entry.Id.ObjectNumber, entry.ProgramHolderId?.ObjectNumber, ruleId))) continue;

            resolved.Add((ruleId, entry));

            if (ruleId == "font-program"
                && entry.ProgramHolderId is not null
                && entry.Kind is FontKind.Type0CidType0 or FontKind.Type0CidType2
                && HasNotdefFinding(entry, fontProgramFindings.Value))
            {
                long key = HolderGroupKey(document, entry);
                memberOfGroup[entry.Id] = key;
                if (!groupMembers.TryGetValue(key, out List<FontInventoryEntry>? members))
                    groupMembers[key] = members = [];
                members.Add(entry);
            }
        }

        // Pass 2: dispatch. A multi-entry group is built ONCE (at the position of its FIRST member)
        // by the merged builder; every other entry — including a notdef-eligible SINGLETON, whose own
        // group has exactly one member — takes the EXISTING per-entry switch unchanged, so the
        // degenerate (non-sharing) case is byte-identical to before this task.
        var processedGroups = new HashSet<long>();
        foreach ((string ruleId, FontInventoryEntry entry) in resolved)
        {
            if (ruleId == "font-program" && memberOfGroup.TryGetValue(entry.Id, out long key))
            {
                List<FontInventoryEntry> members = groupMembers[key];
                if (members.Count > 1)
                {
                    if (!processedGroups.Add(key)) continue; // this group's proposals already emitted
                    proposals.AddRange(ProposeMergedReplace(document, members, ruleId, fontProgramFindings.Value));
                    continue;
                }
            }

            // Null means "this font has nothing to propose AND nothing to report" — only
            // ProposeRegenerate produces it, for a font carrying no subset declaration at all (see its
            // own doc comment). A DeclineProposal is a REPORT, surfaced to the user; emitting one for a
            // font the rule is silent about would be noise about a document with nothing wrong with it.
            FontProposal? proposal = ruleId switch
            {
                "font-embedded" => ProposeEmbed(document, entry, ruleId),
                "font-subset-coverage" => ProposeRegenerate(document, entry, ruleId),
                "font-program" => ProposeWidthPatch(document, entry, ruleId, fontProgramFindings.Value),
                _ => ProposeToUnicode(document, entry, ruleId),
            };

            if (proposal is not null)
                proposals.Add(proposal);
        }

        return new FontRemediationProposal(proposals);
    }

    /// <summary>
    /// Groups a multi-entry notdef-family font-program share for <see cref="Propose(PdfDocument, IEnumerable{ValueTuple{string, int}})"/>
    /// (spec §4, tracker issue 38): the resolved indirect <c>/FontDescriptor</c> object number when
    /// <paramref name="entry"/>'s program holder has one, else the holder's own object number. Only
    /// meaningful for an entry with a non-null <see cref="FontInventoryEntry.ProgramHolderId"/> — the
    /// caller gates on that before calling this, so every entry reaching here is addressable (a
    /// program holder is only non-null when both the font dictionary AND the holder are indirect).
    /// </summary>
    private static long HolderGroupKey(PdfDocument document, FontInventoryEntry entry)
    {
        if (entry.ProgramHolderId is not { } holder) return entry.Id.ObjectNumber;
        return DescriptorObjectNumber(document, holder) ?? holder.ObjectNumber;
    }

    /// <summary>The <c>font-program</c> findings attributable to <paramref name="entry"/> — its own
    /// object, plus its program holder's when that differs (a Type0 wrapper's 6.2.11.8/6.2.11.5
    /// findings are reported against the DESCENDANT CIDFont FontProgramRule actually parsed, not the
    /// wrapper). Shared by <see cref="ProposeWidthPatch"/>'s own dispatch and <see cref="Propose(PdfDocument, IEnumerable{ValueTuple{string, int}})"/>'s
    /// group-eligibility check (<see cref="HasNotdefFinding"/>) so the two cannot disagree about what
    /// "this entry's findings" means.</summary>
    private static List<Finding> FontProgramFindingsFor(FontInventoryEntry entry, ILookup<int, Finding> ruleFindings) =>
        ruleFindings[entry.Id.ObjectNumber]
            .Concat(entry.ProgramHolderId is { } ph && ph.ObjectNumber != entry.Id.ObjectNumber
                ? ruleFindings[ph.ObjectNumber]
                : Enumerable.Empty<Finding>())
            .ToList();

    /// <summary>Whether <paramref name="entry"/> carries a 6.2.11.8 (.notdef) font-program finding —
    /// the SAME test <see cref="ProposeWidthPatch"/> uses to decide whether a composite font's finding
    /// routes to <see cref="ProposeProgramReplace"/>, reused by <see cref="Propose(PdfDocument, IEnumerable{ValueTuple{string, int}})"/>
    /// to decide whether an entry is eligible to join a merged group at all.</summary>
    private static bool HasNotdefFinding(FontInventoryEntry entry, ILookup<int, Finding> ruleFindings) =>
        FontProgramFindingsFor(entry, ruleFindings).Any(f => ClauseKey(f.Clause) == "6.2.11.8");

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
    /// Proposes patching the embedded program's hmtx advances to the declared widths for a
    /// font-program 6.2.11.5 finding, or explains why not (spec §2). Internal for the same
    /// hand-built-entry testability reason as <see cref="ProposeEmbed"/>. Direction is always
    /// program := declared: /Widths and /W position text in conforming viewers, so they are never
    /// touched (§5.1 pin).
    /// </summary>
    internal FontProposal ProposeWidthPatch(
        PdfDocument document, FontInventoryEntry entry, string ruleId, ILookup<int, Finding> ruleFindings)
    {
        if (!entry.IsAddressable)
        {
            return Decline(entry, ruleId,
                "This font is written directly into the page's resources rather than as its own "
                + "object, so Pellucid cannot address its font program to correct it.");
        }

        FontId holder = entry.ProgramHolderId ?? entry.Id;

        List<Finding> mine = FontProgramFindingsFor(entry, ruleFindings);
        bool hasWidth = mine.Any(f => ClauseKey(f.Clause) == "6.2.11.5");
        bool hasOther = mine.Any(f => ClauseKey(f.Clause) != "6.2.11.5");

        // Controller ruling: a composite font's .notdef finding routes to the whole-program-replace
        // arm (the only remedy that can fix a missing glyph), but a SIMPLE font carrying BOTH a
        // 6.2.11.8 (.notdef) and a 6.2.11.5 (width) finding must not lose its width patch to that
        // routing — replacement is not something Pellucid does for a simple font's program at all
        // (SimpleFontMissingGlyphReason below), so gating on hasNotdef alone would silently swallow a
        // fix this planner CAN make. Composite is checked here, not inside ProposeProgramReplace,
        // so this stays the single place that decides which arm a notdef finding reaches.
        bool hasNotdef = mine.Any(f => ClauseKey(f.Clause) == "6.2.11.8");
        bool composite = entry.Kind is FontKind.Type0CidType0 or FontKind.Type0CidType2;
        if (hasNotdef && composite)
            return ProposeProgramReplace(document, entry, ruleId, mine);
        if (!hasWidth)
        {
            return Decline(entry, ruleId, mine.Count == 0
                ? "The font-program finding could not be reproduced against this document's current "
                  + "state, so there is nothing Pellucid can safely correct."
                : hasNotdef
                    ? SimpleFontMissingGlyphReason
                    : "This font renders a glyph absent from its embedded program — replacing a simple "
                      + "font's program is not something Pellucid does yet.");
        }

        switch (entry.Kind)
        {
            case FontKind.Type3:
                return Decline(entry, ruleId,
                    "Type 3 font widths come from each glyph's own drawing procedure, which Pellucid "
                    + "does not rewrite.");
            case FontKind.Type0CidType0 or FontKind.Type1:
                return Decline(entry, ruleId,
                    "This font's program stores its advances in CFF charstrings, which Pellucid "
                    + "cannot yet rewrite.");
        }

        if (document.GetObject(holder.ObjectNumber) is not PdfDictionary holderDict
            || Resolve(document, holderDict.Get("FontDescriptor")) is not PdfDictionary descriptor)
        {
            return Decline(entry, ruleId,
                "The font has no /FontDescriptor, so there is no embedded program to correct.");
        }
        if (Resolve(document, descriptor.Get("FontFile2")) is not PdfStream fontFile2)
        {
            return Decline(entry, ruleId,
                "The font's program is not carried as a /FontFile2 sfnt, so its advances cannot be "
                + "patched in place.");
        }

        if (document.GetObject(entry.Id.ObjectNumber) is not PdfDictionary fontDict
            || PdfFont.Create(fontDict, document) is not { } pdfFont)
        {
            return Decline(entry, ruleId,
                "This font's dictionary could not be read, so Pellucid cannot compare its widths.");
        }
        EmbeddedFontMetrics? metrics = pdfFont.GetEmbeddedMetrics();
        if (metrics is null || !metrics.IsValid)
        {
            return Decline(entry, ruleId,
                "The embedded font program could not be parsed, so correcting its advances would be "
                + "a guess.");
        }

        IEnumerable<WidthComparison> tuples;
        if (entry.Kind == FontKind.Type0CidType2)
        {
            if (pdfFont is not Type0Font type0 || type0.DescendantFont is not CidFont cid
                || type0.EncodingName is not ("Identity-H" or "Identity-V"))
            {
                return Decline(entry, ruleId,
                    "This composite font's encoding is not an Identity CMap, so Pellucid cannot "
                    + "prove which glyph each character selects.");
            }
            // cidKeyedCff is false BY GATING, not by nature: Type0CidType0 declined above, so only a
            // CIDFontType2 descendant reaches this enumeration. If the kind gate ever admits CID0 here,
            // this flag must become entry.Kind-derived (mirror FontProgramRule.CheckType0's discriminator).
            tuples = ProgramWidthResolver.Composite(
                cid, metrics, cidKeyedCff: false, entry.UsedCodes.Distinct());
        }
        else
        {
            if (Resolve(document, fontDict.Get("Widths")) is not PdfArray widths)
            {
                return Decline(entry, ruleId,
                    "The font declares no /Widths array, so there is nothing to reconcile the "
                    + "program against.");
            }
            tuples = ProgramWidthResolver.Simple(
                pdfFont, metrics, widths, entry.UsedCodes.Distinct(), isTrueType: true);
        }

        int upm = metrics.UnitsPerEm <= 0 ? 1000 : metrics.UnitsPerEm;
        var targetByGid = new Dictionary<ushort, double>();
        double worst = 0;
        foreach (WidthComparison w in tuples)
        {
            worst = Math.Max(worst, Math.Abs(w.Declared - w.Program));

            if (w.Declared == 0 && w.Program > 0)
            {
                return Decline(entry, ruleId,
                    "The document declares a zero width where the program has a real advance; "
                    + "patching the program to zero would visibly change layout in renderers that "
                    + "fall back to program advances, so Pellucid leaves it alone.");
            }
            if (targetByGid.TryGetValue(w.Gid, out double existing))
            {
                if (Math.Abs(existing - w.Declared) > FontProgramRule.WidthTolerance)
                {
                    return Decline(entry, ruleId,
                        "Two character codes share one glyph but declare different widths, so no "
                        + "single program advance can satisfy both.");
                }
                continue;
            }
            targetByGid[w.Gid] = w.Declared;
        }

        if (worst <= FontProgramRule.WidthTolerance)
        {
            return Decline(entry, ruleId,
                "The width mismatch could not be reproduced over the character codes this document "
                + "uses, so there is nothing Pellucid can safely correct.");
        }

        var advanceByGid = new Dictionary<ushort, ushort>();
        foreach ((ushort gid, double declared) in targetByGid)
        {
            var fontUnits = (ushort)Math.Clamp(Math.Round(declared * upm / 1000.0), 0, ushort.MaxValue);
            if (fontUnits != metrics.GetAdvanceWidth(gid))
                advanceByGid[gid] = fontUnits;
        }
        if (advanceByGid.Count == 0)
        {
            return Decline(entry, ruleId,
                "Every used glyph's program advance already matches its declared width after "
                + "rounding, so there is nothing to patch.");
        }

        byte[] program = fontFile2.GetDecodedData(document.Decryptor);
        byte[]? patched = SfntAdvancePatcher.Patch(program, advanceByGid, out string? failReason);
        if (patched is null)
        {
            return Decline(entry, ruleId, $"The font program cannot be patched: {failReason}");
        }

        return new PatchWidthsProposal(
            holder, ruleId, patched, advanceByGid.Count, worst, hasOther, CoveredFonts: [entry.Id]);
    }

    /// <summary>
    /// Proposes replacing a Type0 composite font's whole embedded program for a font-program 6.2.11.8
    /// (.notdef) finding — the ONLY arm that can fix a missing glyph, because the substitute's
    /// /CIDToGIDMap has to be rewritten in step with the program rather than reused (spec §3). Gates
    /// run in the order below; each decline is a fact about THIS font/document/machine, never policy
    /// (§6.1, mirrors <see cref="ProposeEmbed"/> / <see cref="ProposeWidthPatch"/>). Internal for the
    /// same hand-built-entry testability reason as those two.
    ///
    /// <para><paramref name="mine"/> is unused beyond the dispatch that already ran in
    /// <see cref="ProposeWidthPatch"/> (which only calls here when <c>mine</c> contains a 6.2.11.8
    /// finding, so it is never empty on that path) — kept in the signature, and typed to match
    /// <see cref="ProposeWidthPatch"/>'s own local <c>mine</c> exactly, so a caller invoking this
    /// directly still passes the same shape the dispatcher does.</para>
    /// </summary>
    internal FontProposal ProposeProgramReplace(
        PdfDocument document, FontInventoryEntry entry, string ruleId, List<Finding> mine)
    {
        if (!entry.IsAddressable)
        {
            return Decline(entry, ruleId,
                "This font is written directly into the page's resources rather than as its own "
                + "object, so Pellucid cannot address its font program to correct it.");
        }

        // Unreachable through Propose() as of this task's dispatch fix (composite is checked there
        // before this method is ever called), but kept as a defensive gate — and the single-source
        // constant — for a direct call (as the width-patch tests make against ProposeWidthPatch) or a
        // future caller.
        if (entry.Kind is not (FontKind.Type0CidType0 or FontKind.Type0CidType2))
            return Decline(entry, ruleId, SimpleFontMissingGlyphReason);

        // F-4b Task 4 (tracker issue 38): the guard that used to sit here — decline whenever another
        // entry shares this entry's program holder or descriptor — is DELETED. Propose() now groups
        // notdef-eligible font-program findings by holder BEFORE ever calling this method, and routes
        // a multi-entry group to ProposeMergedReplace instead; a call that reaches HERE for a
        // composite, notdef-finding entry is therefore already known to be a SINGLETON within the
        // findings this call was given (see Propose's own doc comment on that scoping). The guard
        // itself (SharedHolderReason) still exists as a TEMPORARY private helper for
        // AssessReplacementCandidate's manual path, which Task 4 does not extend to groups — see that
        // method's own call site, and Task 7 for its retirement.
        if (document.GetObject(entry.Id.ObjectNumber) is not PdfDictionary fontDict
            || PdfFont.Create(fontDict, document) is not Type0Font type0
            || type0.DescendantFont is not CidFont cid)
        {
            return Decline(entry, ruleId,
                "This font's dictionary could not be read as a composite font, so Pellucid cannot "
                + "correct its program.");
        }

        if (type0.EncodingName is not ("Identity-H" or "Identity-V"))
        {
            return Decline(entry, ruleId,
                "This composite font's encoding is not an Identity CMap, so Pellucid cannot prove "
                + "which glyph each character selects.");
        }

        if (type0.ToUnicode is null)
        {
            return Decline(entry, ruleId,
                "This font declares no /ToUnicode mapping, which is the only honest source for what "
                + "its characters mean — a replacement face cannot be chosen without it.");
        }

        // Issue 40 honesty (controller ruling, 2026-08-17 review — supersedes this gate's original
        // "only if CID 0 is the SOLE dead code" shape): a draw of CID 0 is .notdef no matter what a
        // replacement maps it to (ISO 32000 §9.7.4.2 — FontProgramRule.CheckType0 now keys 6.2.11.8
        // on the CID itself, not just the resolved GID). CheckType0 emits at most ONE 6.2.11.8
        // finding PER FONT (a single OR'd notdefHit bool across every drawn code, not one finding
        // per dead code) — so for ANY font that draws CID 0, that one finding survives every
        // replacement unconditionally: the content stream is untouched by a program swap, CID 0 is
        // still drawn afterward, and CID 0 is still .notdef afterward regardless of what the
        // substitute's map assigns it. A replacement proposal for such a font therefore closes ZERO
        // rule-visible findings on THIS font — the false-fix shape the resave-harness convention
        // exists to catch — even when the font also draws other, independently-dead codes: those
        // other codes' contribution to the SAME single aggregate finding is moot, because the
        // finding does not go away either way. Decline unconditionally on CID 0's presence.
        //
        // Group-level nuance (a font-program finding that spans MULTIPLE fonts/objects, where a
        // proposal closes ITS OWN font's finding but not a sibling's) is a different question,
        // out of scope here — that belongs to Task 4's per-target ClosesFinding tracking, not this
        // per-font gate.
        if (entry.UsedCodes.Contains(0))
            return Decline(entry, ruleId, Cid0OnlyDeclineReason);

        FontId holder = entry.ProgramHolderId ?? entry.Id;

        // Controller ruling (tracker issue 43): the DECLARED style can lie. 0000_0000024.pdf points
        // three descriptors (Regular / ",Bold" / ",Italic", italic flag + /ItalicAngle -11 and all)
        // at ONE upright embedded program, and every reference renderer (poppler/MuPDF/Ghostscript,
        // oracle-verified 2026-08-17) draws that program and ignores the declarations — so a
        // replacement styled from the declaration visibly restyles the page in every viewer.
        // When the program being replaced parses and carries a head table, its macStyle is the
        // ground truth for what the reader was actually drawing, and the replacement's style follows
        // it. A program with no head table (bare CFF descendant) states nothing, so the
        // name/descriptor derivation in BuildRequest stays the only signal there.
        (bool Bold, bool Italic)? programStyle =
            type0.GetEmbeddedMetrics() is { IsValid: true, HasHeadTable: true } original
                ? (original.IsBold, original.IsItalic)
                : null;

        FontRequest request = BuildRequest(document, entry, holder, programStyle);
        FontMatch? match = fonts.Resolve(request);

        ReplacementResult primary = match is null
            ? new ReplacementResult(
                Decline(entry, ruleId,
                    $"No font matching '{entry.FamilyName}' is installed on this computer. Installing "
                    + "it would let Pellucid replace the deficient program."),
                null)
            : BuildReplacement(
                document, entry, ruleId, holder, type0, cid, match.Data, match.FaceIndex,
                sourceDescription: null);

        if (primary.Proposal is ReplaceProgramProposal)
            return primary.Proposal;

        // Controller ruling (tracker issue 39): SystemFontLocator's OWN ladder can resolve a
        // non-base-35 family (e.g. 'AlArabiya', 'HelveticaNeue-Medium') through its internal
        // synthetic Standard-14 fallback (step 3 of SystemFontLocator.Resolve) into a
        // CFF-flavoured system face (Nimbus Sans/Roman on this machine, ranked ahead of Liberation
        // by Base35Aliases) that BundledStandard14Provider never gets a chance to intercept —
        // that provider only recognises a REQUEST whose ORIGINAL family is itself a base-35 alias,
        // never a name synthesised deep inside the locator's own ladder after the provider has
        // already been asked (and declined) once. Spec §3 step 1 mandates Liberation precedence,
        // which this silently violated for every non-alias-named font. Retrying explicitly, with
        // the SAME synthetic name SubstituteFontResolver.Load derives (Classify + SyntheticStd14Name
        // — the identical two calls the locator's own fallback makes), gives the bundled provider
        // the one chance it needs.
        //
        // Scoped to exactly one retry, and only when the primary attempt found NO face at all, or a
        // face this operation cannot use (non-TrueType — Decision 2 above) — a genuine glyph
        // coverage gap or any other decline is a fact about the SUBSTITUTE FOUND, not about which
        // name was requested, so retrying there could not change the outcome and stays untouched.
        // Strictly the REPLACEMENT path: F-2 embed resolution and render substitution
        // (SubstituteFontResolver itself) are unaffected — see tracker issue 39 for those.
        if (match is null || primary.Format is not FontProgramFormat.TrueType)
        {
            (bool serif, bool mono, bool bold, bool italic) =
                SubstituteFontResolver.Classify(request.BaseFont, type0.DescendantDescriptor);
            // Issue 43 again: Classify reads style tokens off the NAME (",Italic") and the
            // descriptor — the same declarations the program may contradict. Serif/mono stay with
            // Classify (a program states nothing about family class), but bold/italic follow the
            // program whenever it has spoken, or the synthetic name re-imports the lie the primary
            // request just scrubbed.
            if (programStyle is { } style)
                (bold, italic) = style;
            string synthetic = SubstituteFontResolver.SyntheticStd14Name(serif, mono, bold, italic);

            if (!string.Equals(synthetic, request.BaseFont, StringComparison.OrdinalIgnoreCase))
            {
                FontMatch? retryMatch = fonts.Resolve(request with { BaseFont = synthetic });
                if (retryMatch is not null)
                {
                    ReplacementResult retry = BuildReplacement(
                        document, entry, ruleId, holder, type0, cid, retryMatch.Data, retryMatch.FaceIndex,
                        sourceDescription: null);
                    if (retry.Proposal is ReplaceProgramProposal)
                        return retry.Proposal;
                }
            }
        }

        // Neither attempt produced a usable replacement — keep the PRIMARY decline: it names the
        // font the document actually requested (or the fact that nothing matched it at all), which
        // is more informative than a decline about the synthetic retry name the caller never asked
        // for by that name.
        return primary.Proposal;
    }

    /// <summary>
    /// F-4b Task 4 (spec §4, tracker issue 38): the merged builder for a multi-entry NOTDEF-family
    /// group — N logical (Type0) fonts sharing one embedded program, either DIRECTLY (one descendant
    /// CIDFont) or at DESCRIPTOR level (distinct descendants, one shared <c>/FontDescriptor</c>).
    /// Replaces the deleted <c>SharedHolderReason</c> decline for the automatic path: instead of
    /// refusing to touch any of them, this builds ONE <see cref="ReplaceProgramProposal"/> naming a
    /// target per member.
    ///
    /// <para>Every decline this method can produce is reported to EVERY member (one
    /// <see cref="DeclineProposal"/> each, identical reason text — <see cref="DeclineAll"/>): a
    /// per-sibling SHAPE failure (unaddressable, non-composite, unreadable, non-Identity encoding, no
    /// <c>/ToUnicode</c>) is wrapped in the <c>merge-blocked-sibling</c> template naming that sibling's
    /// own would-be singleton reason (<see cref="MergeBlockedSibling"/>); every other failure (no
    /// substitute installed, non-TrueType, a coverage gap, a CID or width conflict between siblings, or
    /// the group-wide cid0-only case) is a fact about the GROUP as a whole and uses its own direct
    /// reason text.</para>
    ///
    /// <para>Task 3 amendment (spec §6, controller ruling, commit 905bae1): a member drawing CID 0 no
    /// longer blocks construction — only that MEMBER's own <see cref="ReplaceTarget.ClosesFinding"/> is
    /// false. Checked right after the per-sibling shape gate, before ever resolving a substitute
    /// (mirroring <see cref="ProposeProgramReplace"/>'s own cid0 gate running before its own
    /// <c>fonts.Resolve</c> call): if EVERY member draws CID 0, none would close, and the whole group
    /// declines <see cref="Cid0OnlyDeclineReason"/> unconditionally — regardless of whether a substitute
    /// is even available — exactly like the singleton gate.</para>
    /// </summary>
    private IReadOnlyList<FontProposal> ProposeMergedReplace(
        PdfDocument document, IReadOnlyList<FontInventoryEntry> group, string ruleId,
        ILookup<int, Finding> ruleFindings)
    {
        // Step 1: gate every sibling individually — the SAME shape gates ProposeProgramReplace runs
        // 1–4 for a singleton, just fanned out to the whole group on the first failure.
        var siblings = new List<(FontInventoryEntry Entry, Type0Font Type0, CidFont Cid)>();
        foreach (FontInventoryEntry entry in group)
        {
            if (!entry.IsAddressable)
            {
                return DeclineAll(group, ruleId, MergeBlockedSibling(
                    "This font is written directly into the page's resources rather than as its own "
                    + "object, so Pellucid cannot address its font program to correct it."));
            }

            if (entry.Kind is not (FontKind.Type0CidType0 or FontKind.Type0CidType2))
                return DeclineAll(group, ruleId, MergeBlockedSibling(SimpleFontMissingGlyphReason));

            if (document.GetObject(entry.Id.ObjectNumber) is not PdfDictionary fontDict
                || PdfFont.Create(fontDict, document) is not Type0Font type0
                || type0.DescendantFont is not CidFont cid)
            {
                return DeclineAll(group, ruleId, MergeBlockedSibling(
                    "This font's dictionary could not be read as a composite font, so Pellucid cannot "
                    + "correct its program."));
            }

            if (type0.EncodingName is not ("Identity-H" or "Identity-V"))
            {
                return DeclineAll(group, ruleId, MergeBlockedSibling(
                    "This composite font's encoding is not an Identity CMap, so Pellucid cannot prove "
                    + "which glyph each character selects."));
            }

            if (type0.ToUnicode is null)
            {
                return DeclineAll(group, ruleId, MergeBlockedSibling(
                    "This font declares no /ToUnicode mapping, which is the only honest source for "
                    + "what its characters mean — a replacement face cannot be chosen without it."));
            }

            siblings.Add((entry, type0, cid));
        }

        // Task 3 amendment: group-wide cid0-only gate, BEFORE substitute resolution — a member drawing
        // CID 0 never closes its own finding (ISO 32000 §9.7.4.2, issue 40), so if NONE of them would
        // close, decline unconditionally, exactly like the singleton gate.
        if (!siblings.Any(s => !s.Entry.UsedCodes.Contains(0)))
            return DeclineAll(group, ruleId, Cid0OnlyDeclineReason);

        // Step 2: resolve the substitute ONCE — program style from the shared program (issue 43's
        // own code path, read off the FIRST sibling since the program is shared), request from the
        // FIRST sibling's FamilyName (their names may differ; the search term does not need to agree
        // across siblings). The issue-39 synthetic retry runs exactly as ProposeProgramReplace's own.
        FontInventoryEntry first = siblings[0].Entry;
        FontId holder0 = first.ProgramHolderId!.Value; // non-null: IsAddressable gated above
        (bool Bold, bool Italic)? programStyle =
            siblings[0].Type0.GetEmbeddedMetrics() is { IsValid: true, HasHeadTable: true } original
                ? (original.IsBold, original.IsItalic)
                : null;

        FontRequest request = BuildRequest(document, first, holder0, programStyle);
        FontMatch? match = fonts.Resolve(request);

        MergedResult primary = match is null
            ? new MergedResult(DeclineAll(group, ruleId,
                $"No font matching '{first.FamilyName}' is installed on this computer. Installing it "
                + "would let Pellucid replace the deficient program."), null)
            : BuildMergedReplacement(siblings, ruleId, match.Data, match.FaceIndex);

        if (primary.Proposals is [ReplaceProgramProposal])
            return primary.Proposals;

        if (match is null || primary.Format is not FontProgramFormat.TrueType)
        {
            (bool serif, bool mono, bool bold, bool italic) =
                SubstituteFontResolver.Classify(request.BaseFont, siblings[0].Type0.DescendantDescriptor);
            if (programStyle is { } style)
                (bold, italic) = style;
            string synthetic = SubstituteFontResolver.SyntheticStd14Name(serif, mono, bold, italic);

            if (!string.Equals(synthetic, request.BaseFont, StringComparison.OrdinalIgnoreCase))
            {
                FontMatch? retryMatch = fonts.Resolve(request with { BaseFont = synthetic });
                if (retryMatch is not null)
                {
                    MergedResult retry = BuildMergedReplacement(siblings, ruleId, retryMatch.Data, retryMatch.FaceIndex);
                    if (retry.Proposals is [ReplaceProgramProposal])
                        return retry.Proposals;
                }
            }
        }

        return primary.Proposals;
    }

    /// <summary>The merged construction core (spec §4 steps 2–5) once a substitute's bytes have been
    /// resolved: byte gates + TrueType check, per-sibling coverage (union, spec §3 step 2), per-target
    /// CID maps (direct-sharing union with cid-conflict detection, or per-descriptor for descriptor
    /// sharing), the advance patch (union of declared widths with width-conflict detection), restored-
    /// code count, and the descriptor. Mirrors <see cref="BuildReplacement"/>'s shape for N siblings
    /// instead of one. <see cref="MergedResult.Format"/> is set whenever classification succeeded, even
    /// on a decline, so <see cref="ProposeMergedReplace"/>'s retry can decide without reclassifying.</summary>
    private static MergedResult BuildMergedReplacement(
        List<(FontInventoryEntry Entry, Type0Font Type0, CidFont Cid)> siblings, string ruleId,
        byte[] bytes, int faceIndex)
    {
        IReadOnlyList<FontInventoryEntry> allEntries = siblings.Select(s => s.Entry).ToList();
        FontInventoryEntry first = siblings[0].Entry;

        ByteGateOutcome gates = RunByteGates(bytes, faceIndex, first.FamilyName, simpleFont: false);
        if (gates.HardBlockReason is not null)
            return new MergedResult(DeclineAll(allEntries, ruleId, gates.HardBlockReason), gates.Classified?.Format);

        EmbeddedFontMetrics metrics = gates.Metrics!;
        string resolvedFamily = gates.ResolvedFamily!;
        ClassifiedProgram classified = gates.Classified!;

        if (classified.Format != FontProgramFormat.TrueType)
        {
            return new MergedResult(DeclineAll(allEntries, ruleId,
                $"The face found for '{first.FamilyName}' is not a TrueType program, and only a "
                + "TrueType program can replace this font's without rewriting CFF charstrings."),
                classified.Format);
        }

        // Coverage gate (spec §3 step 2): every sibling's used CIDs resolved through ITS OWN
        // /ToUnicode into the substitute — the union of every sibling's coverage, all-or-nothing.
        var perSibling = new List<(FontInventoryEntry Entry, CidFont Cid, CidReplacementMapResult MapResult)>();
        foreach ((FontInventoryEntry entry, Type0Font type0, CidFont cid) in siblings)
        {
            CidReplacementMapResult mapResult = CidReplacementMap.Build(type0.ToUnicode!, entry.UsedCodes, metrics);
            if (mapResult.Unresolvable.Count > 0)
            {
                int firstUnresolvable = mapResult.Unresolvable[0];
                return new MergedResult(DeclineAll(allEntries, ruleId,
                    $"'{resolvedFamily}' cannot honestly render {mapResult.Unresolvable.Count} of this "
                    + $"font's characters (first: CID {firstUnresolvable}), so replacing the program "
                    + "would still leave missing glyphs — Pellucid makes no partial replacements."),
                    classified.Format);
            }
            perSibling.Add((entry, cid, mapResult));
        }

        // Direct vs descriptor sharing is decided PER PROGRAM HOLDER, not once for the whole group:
        // grouping by ProgramHolderId.ObjectNumber handles the two-fixture cases this task tests
        // (SharedDescendantDoc: one holder-group of 2 → direct/union; SharedDescriptorDoc: two
        // holder-groups of 1 → descriptor/per-target) AND generalises correctly to a 3+-member group
        // that mixes both (e.g. two siblings sharing a descendant directly, a third sharing only the
        // descriptor) — a shape neither fixture exercises, but one a single group-wide boolean would
        // get wrong for the direct pair inside it. GroupBy preserves first-occurrence order, so
        // Targets keeps the group's input order.
        var targets = new List<ReplaceTarget>();
        foreach (IGrouping<int, (FontInventoryEntry Entry, CidFont Cid, CidReplacementMapResult MapResult)> holderGroup
                 in perSibling.GroupBy(p => p.Entry.ProgramHolderId!.Value.ObjectNumber))
        {
            List<(FontInventoryEntry Entry, CidFont Cid, CidReplacementMapResult MapResult)> members = holderGroup.ToList();

            if (members.Count == 1)
            {
                (FontInventoryEntry entry, _, CidReplacementMapResult mapResult) = members[0];
                targets.Add(new ReplaceTarget(
                    entry.ProgramHolderId!.Value, entry.Id, mapResult.CidToGid, mapResult.MaxCid,
                    ClosesFinding: !entry.UsedCodes.Contains(0)));
                continue;
            }

            // Direct sharing within THIS holder: union the members' maps into ONE; a CID present in
            // two maps with a DIFFERENT gid is a genuine disagreement about what that character code
            // means (spec §4 step 3).
            var unionMap = new Dictionary<int, ushort>();
            var maxCid = 0;
            foreach ((FontInventoryEntry _, CidFont _, CidReplacementMapResult mapResult) in members)
            {
                maxCid = Math.Max(maxCid, mapResult.MaxCid);
                foreach ((int cidCode, ushort gid) in mapResult.CidToGid)
                {
                    if (unionMap.TryGetValue(cidCode, out ushort existingGid))
                    {
                        if (existingGid != gid)
                        {
                            return new MergedResult(DeclineAll(allEntries, ruleId,
                                $"Two fonts sharing this embedded program map character code {cidCode} "
                                + "to different characters, so one replacement program cannot serve "
                                + "both."),
                                classified.Format);
                        }
                        continue;
                    }
                    unionMap[cidCode] = gid;
                }
            }

            FontId sharedHolder = members[0].Entry.ProgramHolderId!.Value;
            foreach ((FontInventoryEntry entry, CidFont _, CidReplacementMapResult _) in members)
            {
                targets.Add(new ReplaceTarget(
                    sharedHolder, entry.Id, unionMap, maxCid, ClosesFinding: !entry.UsedCodes.Contains(0)));
            }
        }

        // Advance patch (spec §4 step 4): union of every sibling's declared widths for the codes IT
        // resolved; the same substitute GID reached by two siblings with different declared widths is
        // a genuine conflict — one program advance cannot satisfy both.
        var targetByGid = new Dictionary<ushort, double>();
        foreach ((FontInventoryEntry _, CidFont cid, CidReplacementMapResult mapResult) in perSibling)
        {
            foreach ((int cidCode, ushort gid) in mapResult.CidToGid)
            {
                double declared = cid.GetCharacterWidth(cidCode);
                if (targetByGid.TryGetValue(gid, out double existing))
                {
                    if (Math.Abs(existing - declared) > FontProgramRule.WidthTolerance)
                    {
                        return new MergedResult(DeclineAll(allEntries, ruleId,
                            "Two fonts sharing this embedded program declare different widths for the "
                            + "same glyph, so one patched program cannot serve both."),
                            classified.Format);
                    }
                    continue;
                }
                targetByGid[gid] = declared;
            }
        }

        int upm = metrics.UnitsPerEm <= 0 ? 1000 : metrics.UnitsPerEm;
        var advanceByGid = new Dictionary<ushort, ushort>();
        foreach ((ushort gid, double declared) in targetByGid)
        {
            var fontUnits = (ushort)Math.Clamp(Math.Round(declared * upm / 1000.0), 0, ushort.MaxValue);
            if (fontUnits != metrics.GetAdvanceWidth(gid))
                advanceByGid[gid] = fontUnits;
        }

        byte[] program = classified.Program;
        if (advanceByGid.Count > 0)
        {
            byte[]? patched = SfntAdvancePatcher.Patch(classified.Program, advanceByGid, out string? failReason);
            if (patched is null)
            {
                return new MergedResult(DeclineAll(allEntries, ruleId,
                    $"The substitute's program cannot be width-patched to this font's declared widths: "
                    + failReason),
                    classified.Format);
            }
            program = patched;
        }

        // RestoredCodeCount (spec §4 step 5): sum over siblings of distinct used codes that resolve to
        // .notdef in THEIR OWN old program — same predicate BuildReplacement uses for a singleton.
        var restored = 0;
        foreach ((FontInventoryEntry entry, Type0Font type0, CidFont cid) in siblings)
        {
            EmbeddedFontMetrics? oldMetrics = type0.GetEmbeddedMetrics();
            if (oldMetrics is null || !oldMetrics.IsValid)
            {
                return new MergedResult(DeclineAll(allEntries, ruleId,
                    "The font-program finding could not be reproduced against this document's current "
                    + "state, so there is nothing Pellucid can safely correct."),
                    classified.Format);
            }
            bool cidKeyed = entry.Kind == FontKind.Type0CidType0;
            restored += entry.UsedCodes.Distinct().Count(code => MapsToNotdefGlyph(code, cidKeyed, cid, oldMetrics));
        }

        FontDescriptorValues? descriptorValues = FontDescriptorMetrics.Compute(program, FontProgramFormat.TrueType);
        if (descriptorValues is null)
        {
            return new MergedResult(DeclineAll(allEntries, ruleId,
                "The substitute program's metrics could not be read, so an honest /FontDescriptor "
                + "cannot be written for it."),
                classified.Format);
        }

        int flags = FontDescriptorFlags.Compute(metrics);
        string newBaseFont = metrics.PostScriptName ?? (metrics.FamilyName ?? resolvedFamily).Replace(" ", "");
        string style = (metrics.IsBold, metrics.IsItalic) switch
        {
            (true, true) => "Bold Italic",
            (true, false) => "Bold",
            (false, true) => "Italic",
            (false, false) => "Regular",
        };
        string source = $"{resolvedFamily} ({style}) — from your system fonts";

        var proposal = new ReplaceProgramProposal(
            targets, ruleId, source, program, FontProgramFormat.TrueType,
            restored, newBaseFont, descriptorValues, flags);
        return new MergedResult([proposal], FontProgramFormat.TrueType);
    }

    /// <summary>Result of <see cref="BuildMergedReplacement"/>: either N <see cref="DeclineProposal"/>s
    /// (one per group member, <see cref="DeclineAll"/>) or exactly one <see cref="ReplaceProgramProposal"/>,
    /// and the classified format whenever classification succeeded — even on a decline — so
    /// <see cref="ProposeMergedReplace"/>'s retry can decide without reclassifying.</summary>
    private readonly record struct MergedResult(IReadOnlyList<FontProposal> Proposals, FontProgramFormat? Format);

    /// <summary>Declines every member of a group with the SAME reason text — one
    /// <see cref="DeclineProposal"/> per logical font, so every UI row for the group gets its own,
    /// identical, explanation (spec §4).</summary>
    private static IReadOnlyList<FontProposal> DeclineAll(
        IReadOnlyList<FontInventoryEntry> group, string ruleId, string reason) =>
        group.Select(entry => (FontProposal)Decline(entry, ruleId, reason)).ToList();

    /// <summary>Verbatim per the controller brief (spec §6) — a later sweep's taxonomy keys on this
    /// exact template. Wraps a failing sibling's own would-be SINGLETON decline reason (e.g. what
    /// <see cref="ProposeProgramReplace"/> would have said about it alone) for the whole group's
    /// per-member decline.</summary>
    private static string MergeBlockedSibling(string reason) =>
        "Another font sharing this font's embedded program cannot be included in a merged replacement "
        + $"({reason}), and replacing the shared program for only some of its fonts would corrupt the "
        + "others.";

    /// <summary>
    /// Mirrors <see cref="AssessCandidate"/>'s shape for the whole-program-replacement path: the SAME
    /// entry-shape gates <see cref="ProposeProgramReplace"/> runs 1–4 (unaddressable, non-composite
    /// kind, a shared program holder, an unreadable/non-Identity composite, no /ToUnicode) become hard
    /// blocks here instead of declines, then <paramref name="candidateBytes"/> runs through the SAME
    /// <see cref="BuildReplacement"/> core the automatic path uses. Unlike <see cref="AssessCandidate"/>,
    /// a coverage gap is a HARD BLOCK here, not a warning (design Decision 7): the embed path's warning
    /// only ever adds .notdef glyphs the user can already see are missing, but a replacement CID whose
    /// ToUnicode value the substitute cannot render has no honest fallback — the CIDToGIDMap entry would
    /// point at a real but wrong glyph, or 0, either way silently.
    /// </summary>
    public CandidateAssessment AssessReplacementCandidate(
        PdfDocument document, FontInventoryEntry entry, string ruleId,
        byte[] candidateBytes, int faceIndex, string sourceDescription)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(candidateBytes);
        ArgumentNullException.ThrowIfNull(sourceDescription);

        if (!entry.IsAddressable)
        {
            return new CandidateAssessment(null,
                "This font is written directly into the page's resources rather than as its own "
                + "object, so Pellucid cannot address its font program to correct it.",
                [], null);
        }

        if (entry.Kind is not (FontKind.Type0CidType0 or FontKind.Type0CidType2))
            return new CandidateAssessment(null, SimpleFontMissingGlyphReason, [], null);

        // TEMPORARY (F-4b Task 4, tracker issue 38): the manual path does not know about merged
        // groups — a user hand-picking a substitute for one of several fonts sharing a program still
        // gets the old last-write-wins protection here, unlike the automatic path (Propose), which
        // now merges instead of declining. Task 7 is expected to extend the manual path to groups too
        // and retire this call.
        if (SharedHolderReason(document, entry, FontInventory.Read(document)) is { } sharedReason)
            return new CandidateAssessment(null, sharedReason, [], null);

        if (document.GetObject(entry.Id.ObjectNumber) is not PdfDictionary fontDict
            || PdfFont.Create(fontDict, document) is not Type0Font type0
            || type0.DescendantFont is not CidFont cid)
        {
            return new CandidateAssessment(null,
                "This font's dictionary could not be read as a composite font, so Pellucid cannot "
                + "correct its program.",
                [], null);
        }

        if (type0.EncodingName is not ("Identity-H" or "Identity-V"))
        {
            return new CandidateAssessment(null,
                "This composite font's encoding is not an Identity CMap, so Pellucid cannot prove "
                + "which glyph each character selects.",
                [], null);
        }

        if (type0.ToUnicode is null)
        {
            return new CandidateAssessment(null,
                "This font declares no /ToUnicode mapping, which is the only honest source for what "
                + "its characters mean — a replacement face cannot be chosen without it.",
                [], null);
        }

        FontId holder = entry.ProgramHolderId ?? entry.Id;
        ReplacementResult result = BuildReplacement(
            document, entry, ruleId, holder, type0, cid, candidateBytes, faceIndex, sourceDescription);

        return result.Proposal switch
        {
            DeclineProposal decline => new CandidateAssessment(result.Format, decline.Reason, [], null),
            ReplaceProgramProposal replace => new CandidateAssessment(result.Format, null, [], replace),
            _ => throw new InvalidOperationException(
                "BuildReplacement returned a proposal type neither Decline nor Replace produces."),
        };
    }

    /// <summary>
    /// The core shared by <see cref="ProposeProgramReplace"/> (automatic) and
    /// <see cref="AssessReplacementCandidate"/> (manual): runs <paramref name="bytes"/> through the byte
    /// gates, resolves every used CID against <paramref name="type0"/>'s /ToUnicode into the substitute
    /// (spec §3 step 2), composes the substitute's advances to the declared widths (step 8), and returns
    /// a ready-to-apply <see cref="ReplaceProgramProposal"/> or an honest <see cref="DeclineProposal"/>.
    /// Returns the classified <see cref="FontProgramFormat"/> alongside the proposal — even on a decline
    /// — so <see cref="AssessReplacementCandidate"/> can report it without re-running the gates.
    /// </summary>
    private ReplacementResult BuildReplacement(
        PdfDocument document, FontInventoryEntry entry, string ruleId, FontId holder,
        Type0Font type0, CidFont cid, byte[] bytes, int faceIndex, string? sourceDescription)
    {
        // simpleFont: false — this substitute will hold a CIDFont's /FontFile2, never a simple font's
        // /FontFile or /FontFile3, so the Table-124-for-a-simple-font gate (and the PFB-segments gate,
        // which only Type1 programs reach anyway) must not run here. See RunByteGates' doc comment.
        ByteGateOutcome gates = RunByteGates(bytes, faceIndex, entry.FamilyName, simpleFont: false);
        if (gates.HardBlockReason is not null)
            return new ReplacementResult(Decline(entry, ruleId, gates.HardBlockReason), gates.Classified?.Format);

        EmbeddedFontMetrics metrics = gates.Metrics!;
        string resolvedFamily = gates.ResolvedFamily!;
        ClassifiedProgram classified = gates.Classified!;

        // Decision 2: only a TrueType substitute can replace this font's program without rewriting CFF
        // charstrings — CidToGid maps CODES to GLYPH IDS, and a CFF program's glyph selection is not
        // addressable by a bare numeric id the way glyf/hmtx is.
        if (classified.Format != FontProgramFormat.TrueType)
        {
            return new ReplacementResult(Decline(entry, ruleId,
                $"The face found for '{entry.FamilyName}' is not a TrueType program, and only a "
                + "TrueType program can replace this font's without rewriting CFF charstrings."),
                classified.Format);
        }

        CidReplacementMapResult mapResult =
            CidReplacementMap.Build(type0.ToUnicode!, entry.UsedCodes, metrics);
        if (mapResult.Unresolvable.Count > 0)
        {
            int first = mapResult.Unresolvable[0];
            return new ReplacementResult(Decline(entry, ruleId,
                $"'{resolvedFamily}' cannot honestly render {mapResult.Unresolvable.Count} of this "
                + $"font's characters (first: CID {first}), so replacing the program would still leave "
                + "missing glyphs — Pellucid makes no partial replacements."),
                classified.Format);
        }

        // entry.UsedCodes empty (reachable only through AssessReplacementCandidate — Propose() never
        // attributes a font-program finding to a font with no used codes) leaves CidToGid empty too:
        // ToStreamBytes' "GID 0 for every CID not in the map" rule would then write EVERY CID to
        // .notdef, silently regressing a font that draws nothing into one that (if it ever drew
        // anything) would draw nothing but .notdef. Distinct from the Unresolvable case above — there
        // is no partial coverage to report, because there is nothing to cover.
        if (mapResult.CidToGid.Count == 0)
        {
            return new ReplacementResult(Decline(entry, ruleId,
                "This font draws no characters Pellucid can resolve, so there is nothing a replacement "
                + "program could restore."),
                classified.Format);
        }

        // Compose step (spec §3 step 8): pin the substitute's advances to the declared widths so
        // applying this proposal can never create a NEW width finding. Declared widths (/W, /DW) are
        // already 1000-per-em glyph space (PDF convention, independent of the substitute's own upm), so
        // the same-gid conflict check below compares them directly — exactly as ProgramWidthResolver's
        // callers do.
        int upm = metrics.UnitsPerEm <= 0 ? 1000 : metrics.UnitsPerEm;
        var targetByGid = new Dictionary<ushort, double>();
        foreach ((int cidCode, ushort gid) in mapResult.CidToGid)
        {
            double declared = cid.GetCharacterWidth(cidCode);
            if (targetByGid.TryGetValue(gid, out double existing))
            {
                if (Math.Abs(existing - declared) > FontProgramRule.WidthTolerance)
                {
                    return new ReplacementResult(Decline(entry, ruleId,
                        "Two character codes share one glyph but declare different widths, so no "
                        + "single program advance can satisfy both."),
                        classified.Format);
                }
                continue;
            }
            targetByGid[gid] = declared;
        }

        // Declared-zero is PATCHED, not declined (Decision 5): the swap already changes appearance —
        // every code renders in the substitute's letterforms — so pinning the advance to the declared
        // width (even zero) is what keeps layout invariant in renderers that fall back to program
        // advances. Unlike ProposeWidthPatch's in-place patch, there is no existing conforming layout
        // to protect from a visible shift here.
        var advanceByGid = new Dictionary<ushort, ushort>();
        foreach ((ushort gid, double declared) in targetByGid)
        {
            var fontUnits = (ushort)Math.Clamp(Math.Round(declared * upm / 1000.0), 0, ushort.MaxValue);
            if (fontUnits != metrics.GetAdvanceWidth(gid))
                advanceByGid[gid] = fontUnits;
        }

        byte[] program = classified.Program;
        if (advanceByGid.Count > 0)
        {
            byte[]? patched = SfntAdvancePatcher.Patch(classified.Program, advanceByGid, out string? failReason);
            if (patched is null)
            {
                return new ReplacementResult(Decline(entry, ruleId,
                    $"The substitute's program cannot be width-patched to this font's declared widths: "
                    + failReason),
                    classified.Format);
            }
            program = patched;
        }

        // RestoredCodeCount: how many distinct used CIDs currently resolve to .notdef in the OLD
        // program — read via the rule's own expression, and via the OLD program's metrics (the wrapper
        // still holds it at planning time; the descriptor is only rewritten when this proposal is
        // applied). Not derived from mapResult.Unresolvable (empty here) or from targetByGid: this
        // counts the CURRENT defect the replacement fixes, not anything about the substitute.
        EmbeddedFontMetrics? oldMetrics = type0.GetEmbeddedMetrics();
        if (oldMetrics is null || !oldMetrics.IsValid)
        {
            return new ReplacementResult(Decline(entry, ruleId,
                "The font-program finding could not be reproduced against this document's current "
                + "state, so there is nothing Pellucid can safely correct."),
                classified.Format);
        }
        bool cidKeyed = entry.Kind == FontKind.Type0CidType0;
        int restored = entry.UsedCodes.Distinct().Count(code =>
            MapsToNotdefGlyph(code, cidKeyed, cid, oldMetrics));

        FontDescriptorValues? descriptorValues = FontDescriptorMetrics.Compute(program, FontProgramFormat.TrueType);
        if (descriptorValues is null)
        {
            return new ReplacementResult(Decline(entry, ruleId,
                "The substitute program's metrics could not be read, so an honest /FontDescriptor "
                + "cannot be written for it."),
                classified.Format);
        }

        // head/post are untouched by SfntAdvancePatcher (only hmtx/hhea/head.checkSumAdjustment move),
        // so reading flags and names off the PRE-patch `metrics` agrees with the post-patch `program`
        // and avoids reparsing it.
        int flags = FontDescriptorFlags.Compute(metrics);
        string newBaseFont = metrics.PostScriptName ?? (metrics.FamilyName ?? resolvedFamily).Replace(" ", "");
        string style = (metrics.IsBold, metrics.IsItalic) switch
        {
            (true, true) => "Bold Italic",
            (true, false) => "Bold",
            (false, true) => "Italic",
            (false, false) => "Regular",
        };
        string source = sourceDescription ?? $"{resolvedFamily} ({style}) — from your system fonts";

        // ClosesFinding: true (Task 3 amendment). For the automatic path, ProposeProgramReplace's
        // cid0 gate (above) already declined every font that draws CID 0 before construction ever
        // gets here, so a singleton reaching this point through THAT caller closes its own 6.2.11.8
        // finding. NOTE for Task 4 (which is expected to replace this hardcoded true with a real
        // per-target computation): BuildReplacement is also reached by AssessReplacementCandidate
        // (the manual path), which has NO equivalent cid0 gate — a user hand-picking a substitute for
        // a font that draws CID 0 can reach here today and get ClosesFinding: true even though the
        // finding can never actually close (issue 40). Pre-existing gap (Task 2 added the gate only
        // to ProposeProgramReplace), not introduced by Task 3 and out of Task 3's zero-behavioural-
        // movement scope to close — flagged here so Task 4's real computation covers both callers.
        var target = new ReplaceTarget(holder, entry.Id, mapResult.CidToGid, mapResult.MaxCid, ClosesFinding: true);
        var proposal = new ReplaceProgramProposal(
            [target], ruleId, source, program, FontProgramFormat.TrueType,
            restored, newBaseFont, descriptorValues, flags);
        return new ReplacementResult(proposal, FontProgramFormat.TrueType);
    }

    /// <summary>Result of <see cref="BuildReplacement"/>: the proposal (a <see cref="ReplaceProgramProposal"/>
    /// or a <see cref="DeclineProposal"/>) and the classified format whenever classification succeeded —
    /// even on a decline — so <see cref="AssessReplacementCandidate"/> can report it without a second
    /// byte-gate run.</summary>
    private readonly record struct ReplacementResult(FontProposal Proposal, FontProgramFormat? Format);

    /// <summary>
    /// Controller ruling (tracker issue 38): a <c>ReplaceProgramProposal</c>-style editor write is
    /// last-write-wins PER PROGRAM HOLDER, but the planner (and the manual replace path) emit a
    /// proposal per LOGICAL font. If two inventory entries share one <c>ProgramHolderId</c>, two
    /// proposals would each build their <c>CidToGid</c> map from only their OWN wrapper's used codes and
    /// silently clobber each other's when applied — missing glyphs with no error anywhere. Compares
    /// <c>ProgramHolderId.ObjectNumber</c> (both non-null) rather than <c>FontId</c> equality directly,
    /// matching <see cref="FontInventory.Find"/>'s own object-number comparison.
    ///
    /// <para>The same failure mode reappears one level down: two DISTINCT program-holder objects (e.g.
    /// two Type0 wrappers' own, separately-numbered descendant CIDFonts) can still point at the SAME
    /// <c>/FontDescriptor</c> object. The editor write lands on the descriptor's <c>/FontFile2</c> or
    /// <c>/FontFile3</c>, so two independently-built proposals would still clobber one program with the
    /// other's, each descendant keeping its own (now-mismatched) CIDToGIDMap — wrong glyphs, no error.
    /// So this also declines when two entries' program holders resolve to the same
    /// <c>/FontDescriptor</c> object number. Descriptor-level identity is enough: two program holders
    /// cannot share a <c>/FontFile2</c>/<c>/FontFile3</c> stream object without sharing the descriptor
    /// that names it, since nothing else in this walk references the stream directly.</para>
    ///
    /// <para>F-4b Task 4: the automatic path (<see cref="ProposeProgramReplace"/>) no longer calls
    /// this — <see cref="Propose(PdfDocument, IEnumerable{ValueTuple{string, int}})"/> groups the shape
    /// this method detects and merges instead of declining. Kept ONLY for
    /// <see cref="AssessReplacementCandidate"/>'s manual path, which has no merged-group equivalent yet
    /// — see that call site, and Task 7 for retiring this method entirely.</para>
    /// </summary>
    private static string? SharedHolderReason(
        PdfDocument document, FontInventoryEntry entry, IReadOnlyList<FontInventoryEntry> inventory)
    {
        if (entry.ProgramHolderId is not { } holder) return null;

        bool sharedHolder = inventory.Any(other =>
            other.Id != entry.Id && other.ProgramHolderId?.ObjectNumber == holder.ObjectNumber);
        if (sharedHolder) return SharedProgramReason;

        if (DescriptorObjectNumber(document, holder) is not { } descriptorNumber) return null;

        bool sharedDescriptor = inventory.Any(other =>
            other.Id != entry.Id
            && other.ProgramHolderId is { } otherHolder
            && otherHolder.ObjectNumber != holder.ObjectNumber
            && DescriptorObjectNumber(document, otherHolder) == descriptorNumber);

        return sharedDescriptor ? SharedProgramReason : null;
    }

    private const string SharedProgramReason =
        "Another font in this document shares this font's embedded program, and replacing one "
        + "program for two fonts in step is not something Pellucid does yet.";

    /// <summary>The program holder's <c>/FontDescriptor</c> object number, or null when the holder
    /// dictionary cannot be read or the descriptor is not an indirect reference (a direct descriptor
    /// dictionary cannot be shared by object identity, so there is nothing to collide on).</summary>
    private static int? DescriptorObjectNumber(PdfDocument document, FontId holder)
    {
        if (document.GetObject(holder.ObjectNumber) is not PdfDictionary holderDict) return null;
        return holderDict.Get("FontDescriptor") is PdfIndirectReference reference
            ? reference.ObjectNumber
            : null;
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
    ///
    /// <para><paramref name="simpleFont"/> (default true, matching every pre-existing caller's
    /// behaviour unchanged) gates the PFB-segments and <see cref="SimpleFontProgramSubtype"/> checks:
    /// both exist to predict a refusal <c>PdfDocumentEditor.EmbedProgram</c> makes for a SIMPLE font
    /// dictionary specifically — <see cref="SimpleFontProgramSubtype"/>'s own doc comment states
    /// "callers must not reach here for a Type0 wrapper or a CIDFont dictionary". <c>BuildReplacement</c>
    /// (F-4b Task 5) calls here for a COMPOSITE font's substitute, where a CID-keyed CFF/OpenType
    /// candidate is not a Table-124 violation at all — it is exactly what a CIDFontType0 descendant is
    /// permitted to carry — so running that gate there would produce a factually inverted decline
    /// ("...permits only for a composite font, never for a simple one" about a font that IS composite).
    /// Classification and the fsType check still run either way: those are facts about the bytes
    /// themselves, not about which kind of PDF font dictionary will hold them.</para>
    /// </summary>
    private static ByteGateOutcome RunByteGates(
        byte[] bytes, int faceIndex, string familyName, bool simpleFont = true)
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

        if (simpleFont)
        {
            // Calls the SAME validation PdfDocumentEditor.EmbedProgram runs (Type1PfbSegments.Split,
            // shared rather than mirrored) so the two cannot diverge: a bare PFA with no PFB segment
            // markers, a corrupt segment table, or a PFB with no binary segment all throw
            // NotSupportedException there — declined here instead, so a proposal never reaches Save
            // only to throw there. The split result itself is discarded; only whether it succeeds
            // matters, since EmbedProgram re-splits the SAME bytes when the proposal is actually
            // applied.
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
            // cannot be read), and a proposal that survived to throw at Save time would be a crash
            // where an honest decline belongs. Reachable only when simpleFont is true — everything
            // reaching here on THAT path is a SIMPLE font (composites declined by the caller before
            // RunByteGates is ever invoked), so the simple-font question is the right one to ask. The
            // current subtype is passed as null deliberately: it only chooses between /Type1 and
            // /MMType1 for a program this ACCEPTS, and never affects whether it throws, so the planner
            // does not need to have resolved the dictionary to predict a refusal. The answer itself is
            // discarded; only whether it succeeds matters, since EmbedProgram re-resolves it when the
            // proposal is applied.
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
    /// subset tag stripped) is the search term. Bold/Italic come from
    /// <paramref name="programStyle"/> — the EMBEDDED program's own <c>head.macStyle</c> — whenever
    /// the caller has one (tracker issue 43: declarations lie, and reference renderers draw the
    /// program). Only when the program has stated nothing are they derived from the name's suffix
    /// convention (<c>-Bold</c>, <c>,Italic</c>, <c>BoldItalic</c> — a stronger signal than a flags
    /// bit), falling back to the program holder's <c>/FontDescriptor</c> Italic flag (bit 6, 0x40)
    /// and a non-zero <c>/ItalicAngle</c> when the name says nothing.
    /// </summary>
    private static FontRequest BuildRequest(
        PdfDocument document, FontInventoryEntry entry, FontId programHolder,
        (bool Bold, bool Italic)? programStyle = null)
    {
        string name = entry.FamilyName;
        if (programStyle is { } fromProgram)
            return new FontRequest(name, fromProgram.Bold, fromProgram.Italic);

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

    // Trailing dotted ISO clause, e.g. "6.2.11.5" out of "ISO 19005-2:2011, 6.2.11.5". Ported from
    // PdfLibrary.Tests' ParitySnapshot.ClauseKey (same regex, same behavior) rather than referenced:
    // that type lives in the TEST project and is internal there, so the main library cannot see it.
    private static readonly Regex TrailingClause = new(@"(\d+(?:\.\d+)*)\s*$", RegexOptions.Compiled);

    private static string? ClauseKey(string? findingClause)
    {
        if (string.IsNullOrWhiteSpace(findingClause)) return null;
        Match m = TrailingClause.Match(findingClause);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static PdfObject? Resolve(PdfDocument document, PdfObject? obj) =>
        obj is PdfIndirectReference reference ? document.GetObject(reference.ObjectNumber) : obj;

    private static string? Name(PdfDocument document, PdfObject? obj) =>
        (Resolve(document, obj) as PdfName)?.Value;

    /// <summary>
    /// Proposes rewriting a subset declaration to match the embedded program, or declines when the
    /// mismatch indicates a truncated PROGRAM rather than a stale DECLARATION (design §5.4).
    ///
    /// <para>The distinguishing question is whether a surplus declared entry — one the program does
    /// not contain — corresponds to a code the document actually uses. A stale entry for an unused
    /// code is exactly what this repair exists to clean up. A surplus entry for a USED code means the
    /// glyph renders .notdef today, and rewriting the declaration would make the document assert
    /// conformance while that stays true. That is a font-program defect, so it defers to F-4.</para>
    ///
    /// <para>Only Identity CMaps are resolved code→CID here. Under any other CMap the planner cannot
    /// cheaply prove which CID a used code selects, so any surplus entry at all is declined rather
    /// than assumed stale — the conservative direction, because the cost of being wrong is asserting
    /// false conformance.</para>
    ///
    /// <para>Returns NULL — neither a proposal nor a decline — for a font carrying no subset
    /// declaration at all. The rule is silent on such a font, so there is nothing to correct and
    /// nothing to report: a proposal would edit a document with nothing wrong with it and create a
    /// conformance obligation it never had, and a decline would surface that non-problem to the user
    /// through the hard-block channel.</para>
    /// </summary>
    private static FontProposal? ProposeRegenerate(
        PdfDocument document, FontInventoryEntry entry, string ruleId)
    {
        // The declaration lives on /FontDescriptor, which lives on the PROGRAM HOLDER — the descendant
        // CIDFont for a composite font, the font dictionary itself for a simple one.
        FontId holder = entry.ProgramHolderId ?? entry.Id;

        if (document.GetObject(holder.ObjectNumber) is not PdfDictionary holderDict)
        {
            return new DeclineProposal(holder, ruleId,
                "This font is written directly into the page's resources rather than as its own "
                + "object, so Pellucid cannot address its subset declaration to correct it.");
        }

        if (Resolve(document, holderDict.Get("FontDescriptor")) is not PdfDictionary descriptor)
        {
            return new DeclineProposal(holder, ruleId,
                "The font has no /FontDescriptor, so it carries no subset declaration to correct.");
        }

        // A font has one kind of declaration or the other, never both: /CIDSet belongs to a CID font's
        // descriptor and /CharSet to a simple Type1's.
        if (Resolve(document, descriptor.Get("CIDSet")) is PdfStream cidSetStream)
            return ProposeRegenerateCidSet(document, entry, holder, ruleId, cidSetStream);

        if (Resolve(document, descriptor.Get("CharSet")) is PdfString charSet)
            return ProposeRegenerateCharSet(document, entry, holder, ruleId, charSet);

        return null;
    }

    /// <summary>The <c>/CIDSet</c> half of <see cref="ProposeRegenerate"/>.</summary>
    private static FontProposal? ProposeRegenerateCidSet(
        PdfDocument document, FontInventoryEntry entry, FontId holder, string ruleId,
        PdfStream cidSetStream)
    {
        // Read through the LOGICAL font (the Type0 wrapper), exactly as FontSubsetCoverageRule does:
        // Type0Font is what knows how to reach the descendant CIDFont's program.
        if (document.GetObject(entry.Id.ObjectNumber) is not PdfDictionary fontDict
            || PdfFont.Create(fontDict, document) is not Type0Font type0)
        {
            return new DeclineProposal(holder, ruleId,
                "This font's dictionary could not be read as a composite font, so Pellucid cannot "
                + "tell which glyphs its embedded program contains.");
        }

        if (type0.DescendantCidFontDictionary is not { } cidDict)
        {
            return new DeclineProposal(holder, ruleId,
                "This composite font has no descendant CIDFont, so there is no font program to "
                + "check its /CIDSet against.");
        }

        if (type0.GetEmbeddedMetrics() is not { IsValid: true } metrics)
        {
            return new DeclineProposal(holder, ruleId,
                "The embedded font program could not be parsed, so Pellucid cannot tell which "
                + "glyphs it contains — correcting the /CIDSet would be a guess.");
        }

        // Mirrors FontSubsetCoverageRule.CheckCid's own dispatch, not just its enumerators: a CID-keyed
        // CFF (CIDFontType0) is enumerated through its charset, whose entries ARE the CIDs, and a
        // TrueType (CIDFontType2) through its CIDToGIDMap or metric range. Running one program's
        // enumerator on the other would describe glyphs it does not have, so the subtype decides —
        // and sharing the rule's dispatch is what keeps a regenerated declaration one the rule accepts.
        string? subtype = Name(document, cidDict.Get("Subtype"));
        IReadOnlySet<int>? programCids;
        Func<int, bool>? containsCid;
        switch (subtype)
        {
            case "CIDFontType0":
                // Null for anything that is not a CID-keyed CFF carrying a charset (a plain CFF, a
                // predefined charset the parser does not materialise), which falls through to the
                // shared "could not be enumerated" decline below rather than guessing.
                programCids = metrics.EnumerateProgramCids();
                containsCid = programCids is null ? null : programCids.Contains;
                break;
            case "CIDFontType2":
                (programCids, containsCid) = SubsetProgramGlyphs.ProgramCids(document, cidDict, metrics);
                break;
            default:
                // Says only what was observed. The guard is on the subtype, so it must not assert a
                // fact about the PROGRAM — a descendant with a missing, malformed or unresolvable
                // /Subtype reaches here too, and its program may be anything at all.
                return new DeclineProposal(holder, ruleId,
                    subtype is null
                        ? "This composite font's descendant has no readable /Subtype, so Pellucid "
                          + "cannot tell how to enumerate the glyphs its program contains."
                        : $"This composite font's descendant is a /{subtype}, which is not a CID font "
                          + "subtype Pellucid can enumerate glyphs for.");
        }

        if (programCids is null || containsCid is null)
        {
            return new DeclineProposal(holder, ruleId,
                "The embedded font program's glyph set could not be enumerated, so correcting the "
                + "/CIDSet would be a guess.");
        }

        // The enumeration and the predicate must agree, or the rule's CidsAgree is UNSATISFIABLE for
        // this font: direction 1 demands every enumerated CID be declared, direction 2 rejects any
        // declared CID the predicate refuses — so a declaration regenerated from programCids would
        // still be faulted, and RegenerateDeclarationProposal's promise that applying it necessarily
        // satisfies the rule would be false. Reachable only on the Identity CIDToGIDMap branch, whose
        // set is [0, NumberOfHMetrics) read straight off `hhea` with no clamp, while the predicate is
        // `cid != 0 && cid < NumGlyphs`: a malformed font declaring numberOfHMetrics > numGlyphs puts
        // the two out of step. The custom-CIDToGIDMap and CID-keyed-CFF branches both derive their set
        // FROM the predicate and are structurally immune. Not observed in 708 real documents.
        //
        // Declined, not repaired, and NOT fixed in SubsetProgramGlyphs: that enumeration must keep
        // mirroring FontSubsetCoverageRule exactly (its whole reason for being shared), so the
        // disagreement is detected here, where the repair decides what it can honestly promise. A
        // program whose own tables contradict each other is a defective program, which is F-4's
        // territory rather than a stale declaration. (Final whole-branch review, 2026-08-14,
        // Important 2.)
        if (programCids.Any(cid => cid != 0 && !containsCid(cid)))
        {
            return new DeclineProposal(holder, ruleId,
                "The embedded font program's own tables disagree about how many glyphs it contains "
                + "(its horizontal-metrics count exceeds its glyph count), so any /CIDSet Pellucid "
                + "wrote from it would still fail the check. The font program itself needs "
                + "rebuilding — correcting the declaration cannot fix it.");
        }

        // Surplus = declared but absent from the program. CID 0 is excluded for the same reason the
        // rule's own comparison excludes it: .notdef is never part of the agreement.
        IReadOnlySet<int> declared =
            SubsetProgramGlyphs.DeclaredCids(cidSetStream.GetDecodedData(document.Decryptor));
        List<int> surplus = declared.Where(cid => cid != 0 && !containsCid(cid)).ToList();

        if (surplus.Count > 0)
        {
            if (Name(document, fontDict.Get("Encoding")) is not ("Identity-H" or "Identity-V"))
            {
                return new DeclineProposal(holder, ruleId,
                    $"The declaration lists {surplus.Count} glyph(s) the embedded program does not "
                    + "contain, and this font's encoding is not an Identity CMap, so Pellucid cannot "
                    + "prove the document does not use them — correcting the declaration might "
                    + "assert conformance the file does not have.");
            }

            // Under /Identity-H and /Identity-V a character code IS the CID (ISO 32000-1 9.7.5.2), so
            // the codes the content streams draw are directly comparable with the declared CIDs.
            var usedCids = new HashSet<int>(entry.UsedCodes);
            int usedSurplus = surplus.Count(usedCids.Contains);
            if (usedSurplus > 0)
            {
                return new DeclineProposal(holder, ruleId,
                    $"The declaration lists {usedSurplus} glyph(s) the embedded program does not "
                    + "contain, and the document uses them — the program itself is incomplete, so "
                    + "correcting the declaration would assert conformance the file does not have.");
            }
        }

        return new RegenerateDeclarationProposal(holder, ruleId, null, programCids);
    }

    /// <summary>The <c>/CharSet</c> half of <see cref="ProposeRegenerate"/>. A simple font maps code to
    /// glyph name through its own /Encoding with no CMap in the way, so "is this surplus name used?" is
    /// answered directly and there is no Identity-CMap caveat.</summary>
    private static FontProposal? ProposeRegenerateCharSet(
        PdfDocument document, FontInventoryEntry entry, FontId holder, string ruleId, PdfString charSet)
    {
        if (document.GetObject(entry.Id.ObjectNumber) is not PdfDictionary fontDict
            || PdfFont.Create(fontDict, document) is not { } font)
        {
            return new DeclineProposal(holder, ruleId,
                "This font's dictionary could not be read, so Pellucid cannot tell which glyphs its "
                + "embedded program contains.");
        }

        if (font.GetEmbeddedMetrics() is not { IsValid: true } metrics)
        {
            return new DeclineProposal(holder, ruleId,
                "The embedded font program could not be parsed, so Pellucid cannot tell which "
                + "glyphs it contains — correcting the /CharSet would be a guess.");
        }

        if (SubsetProgramGlyphs.ProgramGlyphNames(metrics) is not { } programNames)
        {
            return new DeclineProposal(holder, ruleId,
                "The embedded font program's glyph names could not be enumerated, so correcting the "
                + "/CharSet would be a guess.");
        }

        IReadOnlySet<string> declared = SubsetProgramGlyphs.DeclaredGlyphNames(charSet.Value);
        List<string> surplus = declared
            .Where(name => name != ".notdef" && !programNames.Contains(name))
            .ToList();

        if (surplus.Count > 0)
        {
            // The exact counterpart of the CID half's non-Identity-CMap decline: without an encoding
            // there is no code→name mapping, so "no used names" would be a GUESS that every surplus
            // name is unused — and the guess falls on the side of asserting conformance. Only reached
            // when there IS a surplus: with none, there is nothing the encoding could be wrong about.
            if (font.Encoding is not { } encoding)
            {
                return new DeclineProposal(holder, ruleId,
                    $"The declaration lists {surplus.Count} glyph(s) the embedded program does not "
                    + "contain, and this font's encoding could not be resolved, so Pellucid cannot "
                    + "prove the document does not use them — correcting the declaration might "
                    + "assert conformance the file does not have.");
            }

            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (int code in entry.UsedCodes)
                if (encoding.GetGlyphName(code) is { Length: > 0 } glyphName)
                    usedNames.Add(glyphName);

            int usedSurplus = surplus.Count(usedNames.Contains);
            if (usedSurplus > 0)
            {
                return new DeclineProposal(holder, ruleId,
                    $"The declaration lists {usedSurplus} glyph(s) the embedded program does not "
                    + "contain, and the document uses them — the program itself is incomplete, so "
                    + "correcting the declaration would assert conformance the file does not have.");
            }
        }

        return new RegenerateDeclarationProposal(holder, ruleId, programNames, null);
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
