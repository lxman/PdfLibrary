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
        // ProposeWidthPatch itself gates on below) — seed a merge group keyed by HolderGroupKey
        // (issue 38).
        var resolved = new List<(string RuleId, FontInventoryEntry Entry)>();
        // Keyed the SAME shape `seen` uses (review finding I1), not FontId alone: FontId(0) is the
        // overloaded direct-dictionary sentinel `seen`'s own comment above explains, so two distinct
        // direct dictionaries could otherwise collide on Id==0 here too.
        var memberOfGroup = new Dictionary<(int ObjectNumber, int? ProgramHolderObjectNumber), long>();
        var groupMembers = new Dictionary<long, List<FontInventoryEntry>>();
        // SEED members — those carrying an ACTUAL notdef finding IN THIS CALL — as opposed to a
        // sibling pulled in only by the inventory-scoped expansion below. Only a seed's own finding
        // can be said to CLOSE by this operation (spec §4, "Group membership is INVENTORY-scoped, not
        // findings-scoped", 2026-08-18 clarification, commit 16d7585 — Task 4 review finding C1): a
        // caller that never asked about a sibling should not have Propose() silently claim credit for
        // fixing something it was never told to fix, even where the shared-program rewrite this call
        // DOES make necessarily touches that sibling too.
        var seedIdsByKey = new Dictionary<long, HashSet<int>>();

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
                memberOfGroup[(entry.Id.ObjectNumber, entry.ProgramHolderId?.ObjectNumber)] = key;
                if (!groupMembers.TryGetValue(key, out List<FontInventoryEntry>? members))
                    groupMembers[key] = members = [];
                members.Add(entry);
                if (!seedIdsByKey.TryGetValue(key, out HashSet<int>? seeds))
                    seedIdsByKey[key] = seeds = [];
                seeds.Add(entry.Id.ObjectNumber);
            }
        }

        // Inventory-scoped expansion (spec §4, 2026-08-18 clarification — Task 4 review finding C1): a
        // group seeded by a notdef finding is NOT limited to the fonts THIS CALL happened to name.
        // ExpandHolderGroup (shared with AssessReplacementCandidate's manual path — Task 7 review; see
        // its own doc comment for the full rationale and the dedup-key shape) pulls in every other
        // inventory entry sharing the group's key. A sibling pulled in only by this expansion still
        // becomes a FULL target inside ProposeMergedReplace — its used codes join the coverage union,
        // its descendant gets a real map, and ITS OWN gates run too (a gate failure blocks the whole
        // group) — but it is not in `seedIdsByKey`, so it gets `ClosesFinding: false` and no
        // RestoredCodeCount credit: nothing of its was asked to be fixed, so nothing of its is
        // reported as closed.
        foreach ((long key, List<FontInventoryEntry> members) in groupMembers)
        {
            foreach (FontInventoryEntry candidate in ExpandHolderGroup(document, inventory, members, key))
                memberOfGroup[(candidate.Id.ObjectNumber, candidate.ProgramHolderId?.ObjectNumber)] = key;

            // Review fix (Important 2): canonicalize BEFORE ProposeMergedReplace ever sees this group —
            // see CanonicalizeGroupOrder's own doc comment for why.
            CanonicalizeGroupOrder(members);
        }

        // Task 6 (tracker issue 38): WIDTH-family grouping — kind-agnostic (a simple TrueType font and
        // a Type0CidType2 descendant can share one FontFile2/descriptor) and INDEPENDENT of the notdef
        // family above: a pair sharing a holder purely over a width mismatch never seeds a notdef
        // group at all. SEEDED from entries THIS CALL actually named a font-program finding for — an
        // in-place hmtx PATCH only ever touches glyph ids the union of MEMBERS' own declared widths
        // names, and the union's cross-member conflict check (BuildMergedWidthPatch) already catches
        // two members disagreeing about a shared glyph id. CFF-family entries (Type0CidType0, Type1,
        // Type3) are excluded by the Kind check from becoming width-family MEMBERS — they always
        // decline independently, on their own kind, via the ordinary per-entry switch below, exactly
        // as before this task.
        //
        // Task 8b (review finding I3): membership no longer STAYS findings-scoped past the seed —
        // the expansion pass right below pulls in every other same-kind, addressable inventory entry
        // sharing the holder, mirroring the notdef family's own C1 fix. Two defects forced this: (a)
        // production stages ONE finding per Propose() call (RemediationRunner.StageDomainZeroDecision,
        // Pellucid), so a findings-scoped group could never exceed size 1 outside a test harness
        // handing every finding to one call at once — Task 6's own 20/66-to-52/66 result was
        // unreachable by a user clicking Fix; (b) a same-kind, addressable sibling with no finding of
        // its own in THIS call was neither a blocked shape (blockedWidthKeys, below) nor a group
        // member, so its own declared widths never joined the union a merged OR singleton patch
        // writes — silently shifting that sibling's advances whenever it shares a glyph id with a
        // member whose own declared value differs, with no error anywhere. This comment used to read
        // "no inventory-scoped expansion, unlike the notdef family" — that was the gap.
        var widthMemberOfGroup = new Dictionary<(int ObjectNumber, int? ProgramHolderObjectNumber), long>();
        var widthGroupMembers = new Dictionary<long, List<FontInventoryEntry>>();
        // Task 8b review fix (Important 2): object numbers of entries that carry an ACTUAL 6.2.11.5
        // finding THIS CALL — as opposed to an entry pulled into `widthGroupMembers` only by the
        // expansion pass below. Mirrors the notdef family's own `seedIdsByKey`, but as a flat
        // HashSet<int> rather than a per-key dictionary of tuple keys: every width-family seed is
        // gated on `entry.IsAddressable` immediately below (unlike a notdef seed, which is NOT), so
        // `entry.Id.ObjectNumber` can never be FontInventory's FontId(0) direct-dictionary sentinel
        // here — a bare ObjectNumber is an unambiguous key for this set specifically. Two uses: (1)
        // `BuildMergedWidthPatch`'s per-member-fact declines (below) must not assert a fact about an
        // expansion-only member's OWN dictionary/program that is only actually true of the SEED that
        // triggered it; (2) the pass-2 dispatch's width-family capture must not hijack an
        // expansion-only member's OWN, unrelated font-program finding (a genuine 6.2.11.4.1, say) —
        // see both sites' own comments.
        var widthSeedIds = new HashSet<int>();
        foreach ((string ruleId, FontInventoryEntry entry) in resolved)
        {
            if (ruleId != "font-program") continue;
            if (entry.Kind is not (FontKind.TrueType or FontKind.Type0CidType2)) continue;
            if (!entry.IsAddressable || entry.ProgramHolderId is null) continue;
            if (!HasWidthFinding(entry, fontProgramFindings.Value)) continue;

            long key = HolderGroupKey(document, entry);
            var idKey = (entry.Id.ObjectNumber, entry.ProgramHolderId?.ObjectNumber);
            widthMemberOfGroup[idKey] = key;
            widthSeedIds.Add(entry.Id.ObjectNumber);
            if (!widthGroupMembers.TryGetValue(key, out List<FontInventoryEntry>? members))
                widthGroupMembers[key] = members = [];
            members.Add(entry);
        }

        // Inventory-scoped expansion (Task 8b, review finding I3) — reuses ExpandHolderGroup (Task 7)
        // rather than a third copy of its traversal. ExpandHolderGroup itself is kind-agnostic (shared
        // verbatim with the notdef family above and the manual path), so it would happily add a
        // Type1/Type3/non-addressable sibling to `scratch` too; only a same-kind
        // (TrueType/Type0CidType2), addressable candidate is promoted into the REAL width group here.
        // That filter is deliberate, not an oversight (interaction with I1, below): a blocked-shape
        // sibling stays caught by `blockedWidthKeys`'s OWN full-inventory scan, unaffected by whatever
        // this loop adds, which already produces that sibling's correct per-kind reason
        // (BlockingSiblingReason) and declines the group WITHOUT reporting a decline for the blocker
        // itself (the falsifying test — A_mixed_kind_sibling_sharing_the_descriptor_blocks_the_width_merge
        // — pins exactly 2 declines, not 3, for its two width-family seeds). Admitting the blocker into
        // `widthGroupMembers` too would let both mechanisms fire for the same sibling redundantly, and
        // for a non-addressable candidate specifically, BuildMergedWidthPatch has no gate that produces
        // ITS correct singleton reason (only BlockingSiblingReason does) — a bare admission risks a
        // misleading decline reaching the user. A scratch COPY is expanded, not the live group list
        // directly, precisely so this filter can run before anything touches `widthGroupMembers`.
        foreach ((long key, List<FontInventoryEntry> members) in widthGroupMembers)
        {
            var scratch = new List<FontInventoryEntry>(members);
            foreach (FontInventoryEntry candidate in ExpandHolderGroup(document, inventory, scratch, key))
            {
                if (candidate.Kind is not (FontKind.TrueType or FontKind.Type0CidType2)
                    || !candidate.IsAddressable)
                    continue;

                members.Add(candidate);
                widthMemberOfGroup[(candidate.Id.ObjectNumber, candidate.ProgramHolderId?.ObjectNumber)] = key;
            }

            // Controller ruling, extending review fix Important 2 to its width-family analogue: the
            // IDENTICAL seed-order defect exists here — BuildMergedWidthPatch's `holder0` is
            // `members[0].ProgramHolderId`, and under descriptor-level sharing (distinct descendants,
            // one shared /FontDescriptor) two separately-seeded width findings used to yield two
            // DIFFERENT PatchWidthsProposal.Font values for the SAME shared program, exactly like the
            // replace family before this fix — two plan entries, both staged, both patching the same
            // stream last-write-wins. Same helper, same guarantee (every member here has a non-null
            // ProgramHolderId: seeds are gated on it at the seeding loop above, and ExpandHolderGroup's
            // own filter guarantees it for every expansion-added candidate too), same no-op-on-<2-members
            // safety.
            CanonicalizeGroupOrder(members);
        }

        // Review round 1, finding I1: excluding a CFF/Type1/Type3/Unknown-kind entry from width-family
        // MEMBERSHIP (above) is NOT the same as that entry never sharing the physical stream a merged
        // patch rewrites — HolderGroupKey keys on the resolved /FontDescriptor object number (or the
        // holder's own object number), never on which /FontFile* key the descriptor happens to carry,
        // and FontKind is derived purely from /Subtype (FontInventory.KindOf). A malformed-but-real
        // /Subtype /Type1 (or /Type3, or an unrecognized subtype) font whose /FontDescriptor happens to
        // be the SAME descriptor a TrueType/Type0CidType2 sibling's width patch targets shares the
        // EXACT stream that patch rewrites; it declines independently ("CFF charstrings" or similar)
        // while the sibling's patch shifts hmtx advances in the SAME bytes out from under it — the
        // notdef family's own C1 corruption shape, recurring here because the width family's kind
        // filter, unlike the notdef family's Kind check, has no inventory-scoped consultation of what
        // ELSE shares the key. A non-addressable sibling (direct dictionary) poses the identical risk
        // whenever its program holder is still indirect (ProgramHolderId non-null, IsAddressable false)
        // — it too resolves to the SAME key without ever being a width-family candidate itself.
        //
        // Scanned against the FULL inventory (not just `resolved`): a blocking sibling need not carry
        // any finding of its own — same posture as the notdef family's own C1 fix, and for the same
        // reason (the risk is about what SHARES the bytes being rewritten, not about what this call was
        // told to fix).
        //
        // Task 8b: this scan is UNCHANGED by (and independent of) the inventory-scoped expansion just
        // above it — it always re-derives its own blocker straight from `inventory`, never from
        // `widthGroupMembers`'s membership, which is exactly why the expansion loop above deliberately
        // does NOT promote a blocked-shape candidate into `widthGroupMembers`: doing so would not help
        // this scan (it does not consult that dictionary's contents) and would only risk a second,
        // redundant decline path for the same sibling. See the pass-2 dispatch below for the precedence
        // between the two mechanisms.
        var blockedWidthKeys = new Dictionary<long, string>();
        foreach (long key in widthGroupMembers.Keys)
        {
            FontInventoryEntry? blocker = inventory.FirstOrDefault(candidate =>
                candidate.ProgramHolderId is not null
                && HolderGroupKey(document, candidate) == key
                && (candidate.Kind is not (FontKind.TrueType or FontKind.Type0CidType2)
                    || !candidate.IsAddressable));
            if (blocker is not null)
                blockedWidthKeys[key] = BlockingSiblingReason(blocker);
        }

        // Pass 2: dispatch. A multi-entry group is built ONCE (at the position of its FIRST member in
        // `resolved` — an expansion-only member never appears in `resolved` at all, since it carried
        // no finding this call) by the merged builder; every other entry — including a notdef-eligible
        // SINGLETON, whose own group has exactly one member (no sharing sibling anywhere in the
        // inventory) — takes the EXISTING per-entry switch unchanged, so the degenerate (non-sharing)
        // case is byte-identical to before this task. A width-family finding on a holder claimed by a
        // multi-member group is SUBSUMED the same way: it reaches `resolved` (it carried its OWN
        // finding), `memberOfGroup` routes it to the SAME group, and it is skipped here exactly like a
        // second seed would be — the merged replacement's advance patch already covers its declared
        // widths, so a second, independent PatchWidthsProposal against the SAME program stream would
        // be a last-write-wins corruption, not just redundant.
        //
        // Review round 2, finding 1 (controller ruling): the group-routing branch below is gated on
        // `entry.Kind` being COMPOSITE, in addition to group membership. A non-composite entry (e.g. a
        // simple font whose /FontDescriptor happens to collide with a composite seed's descriptor) can
        // still be a group MEMBER via inventory-scoped expansion — Step 1 inside ProposeMergedReplace
        // sees it and correctly declines the WHOLE group on its account (writing a composite substitute
        // over a shared program a simple font depends on would corrupt it) — but that member must NOT
        // be captured/skipped here: its OWN finding (e.g. a genuinely independent 6.2.11.5 width
        // mismatch) still needs the ordinary per-entry dispatch below to run for it. This is safe even
        // though the entry is also a member of a group that declines: a DECLINED group writes nothing,
        // so there is no double-writer to corrupt — only ONE proposal (this entry's own) ever touches
        // its program.
        var processedGroups = new HashSet<long>();
        // Task 6: whether a given notdef group's ONE call to ProposeMergedReplace ended up PROPOSING
        // (a single ReplaceProgramProposal) rather than declining (N DeclineProposals) — read by every
        // member's own turn through this loop, not just the member that happened to trigger the call.
        var groupProposedReplace = new Dictionary<long, bool>();
        var processedWidthGroups = new HashSet<long>();
        foreach ((string ruleId, FontInventoryEntry entry) in resolved)
        {
            var idKey = (entry.Id.ObjectNumber, entry.ProgramHolderId?.ObjectNumber);
            var freedFromDeclinedGroup = false;

            if (ruleId == "font-program"
                && entry.Kind is FontKind.Type0CidType0 or FontKind.Type0CidType2
                && memberOfGroup.TryGetValue(idKey, out long key))
            {
                List<FontInventoryEntry> members = groupMembers[key];
                if (members.Count > 1)
                {
                    if (processedGroups.Add(key))
                    {
                        HashSet<int> seedIds = seedIdsByKey[key];
                        IReadOnlyList<FontProposal> groupProposals = ProposeMergedReplace(
                            document, members, ruleId, fontProgramFindings.Value, seedIds);
                        proposals.AddRange(groupProposals);
                        groupProposedReplace[key] = groupProposals is [ReplaceProgramProposal];
                    }

                    // Ruling (proposed-only skip, 2026-08-17): the subsumption skip applies ONLY when
                    // the group actually PROPOSED — its merged advance patch already unions every
                    // member's declared widths (BuildMergedReplacement), so a member's own 6.2.11.5
                    // finding is covered by construction and a second, independent width proposal
                    // against the SAME program would be a last-write-wins corruption, not merely
                    // redundant. A DECLINED group writes nothing at all, so it frees every member's
                    // width finding for the width-family arm below instead — including a member that
                    // ALSO carries its own notdef finding (the group's per-member decline already
                    // speaks for that half; the width half is a genuinely separate, independently
                    // fixable fact, exactly like a simple font's own "patches widths, leaves the other
                    // finding" convention). This entry must NOT fall into the ordinary switch's
                    // ProposeWidthPatch call below unconditionally, though: that method's own
                    // hasNotdef&&composite gate would re-attempt a SINGLETON ProposeProgramReplace,
                    // unguarded by the group's own shared-holder gates (deleted in Task 4) — exactly
                    // the corruption shape Task 4's C1 fix exists to prevent. `freedFromDeclinedGroup`
                    // routes such an entry to the width-only core directly instead.
                    if (groupProposedReplace[key]) continue;

                    // A member with no width finding of its own gets nothing further: the group's
                    // per-member decline (emitted above, for every member) already speaks for it, and
                    // ProposeWidthPatchOnly's own `!hasWidth` branch would otherwise produce a WRONG
                    // decline text for a composite entry here (it names "a simple font" — see
                    // SimpleFontMissingGlyphReason — which is only correct for the singleton dispatch's
                    // own non-composite callers).
                    //
                    // Task 8b review fix (Important 2): `widthMemberOfGroup` no longer implies "this
                    // entry carries its own 6.2.11.5 finding" — inventory-scoped expansion (above) can
                    // list an entry here purely because it shares a SIBLING's holder. Setting
                    // `freedFromDeclinedGroup = true` for such an entry below is still safe, through a
                    // non-obvious argument worth writing down: reaching this line already requires
                    // `key`'s notdef GROUP to have `members.Count > 1` (line ~331), so at least one
                    // OTHER entry shares this entry's HolderGroupKey. Every possible such sibling is
                    // either (a) width-eligible (TrueType/Type0CidType2, addressable) — in which case
                    // it was ALSO pulled into THIS entry's width group by the SAME expansion pass, so
                    // `widthMembers.Count > 1` below is guaranteed — or (b) width-ineligible (wrong
                    // kind or non-addressable) — in which case `blockedWidthKeys` (I1) unconditionally
                    // catches it, since its scan predicate is the exact complement of width
                    // eligibility. Either way this entry's own turn through the block below always hits
                    // the BLOCKED or MERGED branch and `continue`s before ever reaching the switch's
                    // `ProposeWidthPatchOnly` call — the `!hasWidth` branch this comment used to guard
                    // against is unreachable from here, not merely avoided by the flag. (A
                    // Type0CidType0 entry can never reach this line at all: it fails the width-family
                    // Kind filter, so `widthMemberOfGroup` never contains it, and the check right below
                    // continues past it first.)
                    if (!widthMemberOfGroup.ContainsKey(idKey)) continue;
                    freedFromDeclinedGroup = true;
                }
            }

            // Task 8b review fix (Important 2): gated on `widthSeedIds` (or `freedFromDeclinedGroup`,
            // whose own safety is the non-obvious argument documented just above), not bare
            // `widthMemberOfGroup` membership — an entry pulled into a group only by expansion can
            // independently carry a DIFFERENT font-program finding this call (a genuine 6.2.11.4.1, or
            // a 6.2.11.8 on a SIMPLE TrueType font, which never seeds a notdef group per the Kind gate
            // above). Capturing such an entry here would route ITS OWN dispatch into the width-family
            // branch below and, once some OTHER resolved seed has already `processedWidthGroups`-
            // marked the same key, silently swallow its finding with NO proposal at all — see
            // MergedWidthPatchTests.An_expansion_only_members_own_unrelated_finding_still_dispatches.
            // This gate only excludes such an entry from TRIGGERING/being captured by the width
            // dispatch; `widthMembers` (below, looked up from `widthGroupMembers[widthKey]`) still
            // includes it as a merge participant whenever the group's own genuine seed reaches this
            // block — its declared widths still join the union either way.
            if (ruleId == "font-program"
                && entry.Kind is FontKind.TrueType or FontKind.Type0CidType2
                && widthMemberOfGroup.TryGetValue(idKey, out long widthKey)
                && (freedFromDeclinedGroup || widthSeedIds.Contains(entry.Id.ObjectNumber)))
            {
                List<FontInventoryEntry> widthMembers = widthGroupMembers[widthKey];

                // I1: a mixed-kind or non-addressable sibling shares the SAME physical stream this
                // holder's width patch would rewrite — decline the WHOLE width-family group (merged OR
                // singleton; the risk is identical either way) rather than patch under it.
                //
                // Task 8b precedence: this check runs BEFORE `widthMembers.Count > 1` below, on
                // purpose — a key can be blocked regardless of how many (same-kind, addressable)
                // members the inventory-scoped expansion above found for it, so a blocked group must
                // never reach BuildMergedWidthPatch. The two mechanisms do not actually overlap in
                // practice: expansion only ever adds same-kind, addressable candidates to
                // `widthMembers` (see the filter above), so the blocking sibling itself is never IN
                // `widthMembers` here — `DeclineAll` below reports exactly the seeded/expanded
                // width-eligible members, not the blocker.
                if (blockedWidthKeys.TryGetValue(widthKey, out string? blockedReason))
                {
                    if (!processedWidthGroups.Add(widthKey)) continue;
                    proposals.AddRange(
                        DeclineAll(widthMembers, ruleId, MergeBlockedSibling(blockedReason, widthFamily: true)));
                    continue;
                }

                if (widthMembers.Count > 1)
                {
                    if (!processedWidthGroups.Add(widthKey)) continue; // this holder's width group already emitted
                    proposals.AddRange(BuildMergedWidthPatch(
                        document, widthMembers, ruleId, fontProgramFindings.Value, widthSeedIds));
                    continue;
                }
                // singleton, unblocked — fall through unchanged to the ordinary dispatch below.
            }

            // Null means "this font has nothing to propose AND nothing to report" — only
            // ProposeRegenerate produces it, for a font carrying no subset declaration at all (see its
            // own doc comment). A DeclineProposal is a REPORT, surfaced to the user; emitting one for a
            // font the rule is silent about would be noise about a document with nothing wrong with it.
            FontProposal? proposal = ruleId switch
            {
                "font-embedded" => ProposeEmbed(document, entry, ruleId),
                "font-subset-coverage" => ProposeRegenerate(document, entry, ruleId),
                "font-program" => freedFromDeclinedGroup
                    ? ProposeWidthPatchOnly(
                        document, entry, ruleId, FontProgramFindingsFor(entry, fontProgramFindings.Value))
                    : ProposeWidthPatch(document, entry, ruleId, fontProgramFindings.Value),
                _ => ProposeToUnicode(document, entry, ruleId),
            };

            if (proposal is not null)
                proposals.Add(proposal);
        }

        // Review round 1, finding I2: a font can be independently declined by BOTH the notdef-family
        // group (its own per-member decline) and the width-family arm freed from that same group,
        // whose own decline can reuse the SAME shared-constant reason text (e.g. MergeWidthConflictReason
        // when both arms hit the identical cross-sibling width disagreement) — two byte-identical
        // DeclineProposal rows for one font/rule. FontsDomain renders every DeclineProposal as its own
        // row, so the user would see the identical sentence twice and a ledger would double-count.
        // Dedup EXACT (Font, RuleId, Reason) triples right before returning — distinct reason texts for
        // genuinely distinct facts about the SAME font/rule are untouched; only a literal duplicate
        // collapses. Non-decline proposals are never deduped here: two PatchWidthsProposals or
        // ReplaceProgramProposals with identical fields would be a planner bug worth seeing, not noise
        // to hide.
        var seenDeclines = new HashSet<(int ObjectNumber, string RuleId, string Reason)>();
        var deduped = new List<FontProposal>(proposals.Count);
        foreach (FontProposal p in proposals)
        {
            if (p is DeclineProposal decline
                && !seenDeclines.Add((decline.Font.ObjectNumber, decline.RuleId, decline.Reason)))
                continue;
            deduped.Add(p);
        }

        return new FontRemediationProposal(deduped);
    }

    /// <summary>
    /// Groups a multi-entry notdef-family font-program share for <see cref="Propose(PdfDocument, IEnumerable{ValueTuple{string, int}})"/>
    /// (spec §4, tracker issue 38): the resolved indirect <c>/FontDescriptor</c> object number when
    /// <paramref name="entry"/>'s program holder has one, else the holder's own object number — the two
    /// domains tagged into disjoint halves of the <c>long</c> (review finding I2) so a descriptor
    /// object number can never collide with an UNRELATED holder's own object number even though both
    /// are drawn from the same PDF object-number space. Only meaningful for an entry with a non-null
    /// <see cref="FontInventoryEntry.ProgramHolderId"/> — every caller (the notdef-eligibility check in
    /// <see cref="Propose(PdfDocument, IEnumerable{ValueTuple{string, int}})"/>'s seeding pass, and its
    /// inventory-scoped expansion pass) gates on that before calling this.
    ///
    /// <para>NOT a guarantee of <see cref="FontInventoryEntry.IsAddressable"/> (review finding I1,
    /// correcting this doc comment's prior claim): a non-null <c>ProgramHolderId</c> only means the
    /// PROGRAM HOLDER is indirect — the LOGICAL font dictionary itself may still be direct, in which
    /// case the entry is groupable by key, but <see cref="ProposeMergedReplace"/>'s own per-sibling
    /// shape gate (step 1, <c>IsAddressable</c>) declines it like any other shape failure.</para>
    /// </summary>
    private static long HolderGroupKey(PdfDocument document, FontInventoryEntry entry)
    {
        if (entry.ProgramHolderId is not { } holder) return entry.Id.ObjectNumber;
        // Tag the descriptor-number domain into the upper half of the long: object numbers in a real
        // PDF are always far below 2^32, so this fallback (untagged holder-number) domain and the
        // descriptor domain above can never produce the same key for two unrelated program holders.
        return DescriptorObjectNumber(document, holder) is { } descriptorNumber
            ? (long)descriptorNumber | (1L << 32)
            : holder.ObjectNumber;
    }

    /// <summary>
    /// Inventory-scoped expansion (spec §4, 2026-08-18 clarification — Task 4 review finding C1),
    /// shared by <see cref="Propose(PdfDocument, IEnumerable{ValueTuple{string, int}})"/>'s automatic
    /// path (whose group may already hold several SEED members before this runs) and
    /// <see cref="AssessReplacementCandidate"/>'s manual path (whose group is seeded by exactly one
    /// picked entry) — extracted from a near-identical copy in each (Task 7 review): every OTHER
    /// inventory entry sharing <paramref name="key"/> — the SAME <see cref="HolderGroupKey"/> already
    /// computed for the seed(s) — joins <paramref name="members"/>, kind-agnostic. The shared program
    /// is being rewritten (or the whole group declines) either way, and skipping a sibling here is
    /// exactly the silent-.notdef corruption the review caught: ToStreamBytes writes GID 0 for every
    /// CID a target's map does not cover, so an uncovered sibling's fine text would render .notdef
    /// with no error anywhere.
    ///
    /// <para>Mutates <paramref name="members"/> in place and returns the SAME entries it added, in
    /// inventory order, so a caller with per-entry bookkeeping beyond bare membership (e.g.
    /// <c>Propose</c>'s own <c>memberOfGroup</c> map) can update it without re-deriving which entries
    /// are new. Takes the group's already-built member LIST rather than a single seed entry: unlike
    /// the manual path, <c>Propose</c>'s group can already hold more than one SEED before expansion
    /// runs (two wrappers both carrying THIS call's own notdef finding, sharing one holder), so a
    /// signature scoped to one seed would force restructuring that loop — out of scope for a pure
    /// refactor.</para>
    ///
    /// <para>Dedup-keyed on <c>(ObjectNumber, ProgramHolderObjectNumber)</c> (review round 2, finding
    /// 4), not <c>Id.ObjectNumber</c> alone — a bare ObjectNumber dedup would collide on
    /// <see cref="FontInventory"/>'s <c>FontId(0)</c> direct-dictionary sentinel, silently dropping a
    /// second, genuinely distinct direct-dictionary candidate that happens to share this group's
    /// key.</para>
    /// </summary>
    private static List<FontInventoryEntry> ExpandHolderGroup(
        PdfDocument document, IReadOnlyList<FontInventoryEntry> inventory,
        List<FontInventoryEntry> members, long key)
    {
        var memberIds = new HashSet<(int, int?)>(
            members.Select(m => (m.Id.ObjectNumber, m.ProgramHolderId?.ObjectNumber)));
        var added = new List<FontInventoryEntry>();
        foreach (FontInventoryEntry candidate in inventory)
        {
            if (candidate.ProgramHolderId is not { } holder) continue;
            (int, int?) candidateKey = (candidate.Id.ObjectNumber, holder.ObjectNumber);
            if (memberIds.Contains(candidateKey)) continue;
            if (HolderGroupKey(document, candidate) != key) continue;

            members.Add(candidate);
            memberIds.Add(candidateKey);
            added.Add(candidate);
        }
        return added;
    }

    /// <summary>
    /// Whole-branch review fix, Important 2 (and its width-family analogue, applied by controller
    /// ruling in the same wave): sorts a shared-holder group's members by
    /// <c>(ProgramHolderId.ObjectNumber, Id.ObjectNumber)</c> ascending, so the group's FIRST member —
    /// and therefore <see cref="ReplaceProgramProposal"/>'s <c>Targets[0].Font</c> identity
    /// (<see cref="FontProposal.Font"/>, via the record's base-call), <see cref="ProposeMergedReplace"/>'s
    /// <c>BuildRequest</c> source entry, AND <see cref="BuildMergedWidthPatch"/>'s <c>holder0</c>
    /// (<c>PatchWidthsProposal.Font</c>, via its own <c>members[0]</c>) — is INDEPENDENT of which
    /// sibling's finding (or which manual pick) happened to seed the call.
    ///
    /// <para>Without this, descriptor-level sharing (distinct descendants, one shared /FontDescriptor)
    /// let two different callers produce two DIFFERENT <see cref="FontId"/>s for the SAME physical
    /// program: clicking "Fix this" on wrapper A first put A's descendant at <c>group[0]</c>; a later,
    /// separate click on wrapper B's own row put B's descendant at <c>group[0]</c> instead (each "Fix
    /// this" stages exactly one finding per call — <c>RemediationRunner.StageDomainZeroDecision</c> —
    /// so these really are two independent calls, not one call naming both). <c>FontRemediationPlan</c>
    /// keys on <c>(proposal.Font.ObjectNumber, RuleId)</c>, so the two proposals landed as TWO plan
    /// entries for one shared program instead of one, and <c>FontRemediationService.ApplyAndSave</c>
    /// applied both — each calling <c>ReplaceCompositeProgram</c>, each doing
    /// <c>descriptor.Set("FontFile2", …)</c> on the SAME descriptor, last-write-wins, with an orphaned
    /// program stream and duplicate <c>/CIDToGIDMap</c> streams left behind. Where the siblings'
    /// FamilyNames also differ, the two calls' own <c>BuildRequest</c> (built from whichever sibling
    /// happened to be <c>group[0]</c>/<c>siblings[0]</c>) could resolve two DIFFERENT substitute faces
    /// for the one program, so one staged row's confirmation text could name a face that never ends up
    /// in the saved file.</para>
    ///
    /// <para>Called AFTER <see cref="ExpandHolderGroup"/> has finished appending every sibling, so it
    /// sorts the WHOLE group — including a call whose group already held more than one SEED before
    /// expansion ran — not just the newly-added members. Does not touch <c>seedIds</c>: seed membership
    /// is tracked in a separate <c>HashSet&lt;int&gt;</c> keyed by object number, never by list
    /// position, so canonicalizing <paramref name="group"/>'s order cannot change which members are
    /// seeds or what they credit. Every member here is guaranteed a non-null
    /// <see cref="FontInventoryEntry.ProgramHolderId"/> by construction: every caller only ever adds a
    /// member after checking that (the notdef-family seeding gate, and <see cref="ExpandHolderGroup"/>'s
    /// own filter), so <c>.Value</c> below never throws. A group of 0 or 1 members is a no-op —
    /// <see cref="List{T}.Sort()"/> never invokes the comparer for fewer than two elements — matching
    /// the degenerate (non-sharing) singleton case's byte-identical-to-today guarantee (spec §3).</para>
    ///
    /// <para>Applied at TWO call sites: the notdef-family group in <c>Propose</c> (after its own
    /// <see cref="ExpandHolderGroup"/> call) and the manual path's group in
    /// <see cref="AssessReplacementCandidate"/> (same). Also applied to the WIDTH-family group in
    /// <c>Propose</c> (after ITS OWN expansion loop, which builds the real <c>widthGroupMembers</c>
    /// list via a filtered `scratch` copy rather than mutating the live list through
    /// <see cref="ExpandHolderGroup"/> directly — canonicalization is applied to the real list, after
    /// that filtering, not to the scratch copy) — the identical seed-order defect existed there too:
    /// <see cref="BuildMergedWidthPatch"/>'s <c>holder0</c> is <c>members[0].ProgramHolderId</c>, so
    /// under descriptor-level sharing two separately-seeded width findings used to produce two
    /// different <c>PatchWidthsProposal.Font</c> values for the SAME shared program — same two-plan-
    /// entries, last-write-wins consequence as the replace family. First identified as out of the
    /// initial fix's literal scope (which named <c>Targets[0].Font</c>/<c>BuildRequest</c>, both
    /// replace-family-only) and flagged as a residual; the controller then ruled it in-scope for this
    /// same wave rather than a second one, since it is the identical defect with the identical
    /// consequence. There is no manual path for the width family to canonicalize —
    /// <see cref="BuildMergedWidthPatch"/> has exactly one caller, <c>Propose</c>'s own pass-2
    /// dispatch.</para>
    /// </summary>
    private static void CanonicalizeGroupOrder(List<FontInventoryEntry> group) =>
        group.Sort((a, b) =>
        {
            int byHolder = a.ProgramHolderId!.Value.ObjectNumber
                .CompareTo(b.ProgramHolderId!.Value.ObjectNumber);
            return byHolder != 0 ? byHolder : a.Id.ObjectNumber.CompareTo(b.Id.ObjectNumber);
        });

    /// <summary>The <c>font-program</c> findings attributable to <paramref name="entry"/> — its own
    /// object, plus its program holder's when that differs. Corrected (Task 6 review, finding M7): a
    /// Type0 wrapper's 6.2.11.8/6.2.11.5 findings are actually reported against the WRAPPER's own
    /// object number, never the descendant — <see cref="FontProgramRule.Make"/> reads
    /// <c>font.FontDictionary</c>, where <c>font</c> is the <c>Type0Font</c> (the wrapper) passed into
    /// <c>CheckType0</c>, so <c>ruleFindings[entry.ProgramHolderId.ObjectNumber]</c> is empty for a
    /// Type0 entry in practice; the union below still concatenates it defensively (a direct or future
    /// caller could attribute there), but every composite font's real findings arrive via
    /// <c>ruleFindings[entry.Id.ObjectNumber]</c> alone, scoped to the codes THAT WRAPPER itself draws
    /// — a sibling sharing the same descendant/program does NOT see a co-drawn code's finding unless it
    /// draws that code too. This is the exact fact <see cref="HasWidthFinding"/>'s Task 6 width-family
    /// seeding depends on: a sibling that only draws a dead code never gets its own 6.2.11.5 finding,
    /// regardless of what another sibling sharing its holder draws. Shared by
    /// <see cref="ProposeWidthPatch"/>'s own dispatch and <see cref="Propose(PdfDocument, IEnumerable{ValueTuple{string, int}})"/>'s
    /// group-eligibility checks (<see cref="HasNotdefFinding"/>, <see cref="HasWidthFinding"/>) so
    /// none of the three can disagree about what "this entry's findings" means.</summary>
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

    /// <summary>Whether <paramref name="entry"/> carries a 6.2.11.5 (declared-width) font-program
    /// finding — Task 6's own analogue of <see cref="HasNotdefFinding"/>, used to seed the
    /// WIDTH-family grouping in <see cref="Propose(PdfDocument, IEnumerable{ValueTuple{string, int}})"/>.</summary>
    private static bool HasWidthFinding(FontInventoryEntry entry, ILookup<int, Finding> ruleFindings) =>
        FontProgramFindingsFor(entry, ruleFindings).Any(f => ClauseKey(f.Clause) == "6.2.11.5");

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

        List<Finding> mine = FontProgramFindingsFor(entry, ruleFindings);

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

        return ProposeWidthPatchOnly(document, entry, ruleId, mine);
    }

    /// <summary>
    /// Task 6 (tracker issue 38): the width-only core <see cref="ProposeWidthPatch"/> extracted from,
    /// once the notdef&amp;&amp;composite routing decision is already settled — reused directly by
    /// <see cref="Propose(PdfDocument, IEnumerable{ValueTuple{string, int}})"/>'s "declined-replace-
    /// group-frees-width" path (a composite member whose notdef GROUP declined, which must NOT
    /// re-enter <see cref="ProposeWidthPatch"/>'s own hasNotdef&amp;&amp;composite gate — that would
    /// re-attempt a SINGLETON <see cref="ProposeProgramReplace"/>, unguarded by the group's own
    /// shared-holder gates, exactly the corruption shape Task 4's C1 fix exists to prevent) — and by
    /// the ordinary singleton dispatch for every other <c>font-program</c> entry, unchanged. Callers
    /// that already know <paramref name="mine"/> is non-empty and width-carrying (Task 6's freed-width
    /// callers) still pass through the same <c>!hasWidth</c> gate below; it simply never fires for
    /// them.
    /// </summary>
    private FontProposal ProposeWidthPatchOnly(
        PdfDocument document, FontInventoryEntry entry, string ruleId, List<Finding> mine)
    {
        FontId holder = entry.ProgramHolderId ?? entry.Id;
        bool hasWidth = mine.Any(f => ClauseKey(f.Clause) == "6.2.11.5");
        bool hasOther = mine.Any(f => ClauseKey(f.Clause) != "6.2.11.5");
        bool hasNotdef = mine.Any(f => ClauseKey(f.Clause) == "6.2.11.8");

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

    /// <summary>Verbatim shared between <see cref="BuildMergedReplacement"/>'s own width-union conflict
    /// (the SUBSTITUTE program's advances) and <see cref="BuildMergedWidthPatch"/>'s (the EXISTING
    /// program's advances) — a later sweep's taxonomy keys on this exact text, so both call sites use
    /// one source rather than two copies that could drift.</summary>
    private const string MergeWidthConflictReason =
        "Two fonts sharing this embedded program declare different widths for the "
        + "same glyph, so one patched program cannot serve both.";

    /// <summary>
    /// Task 6 (tracker issue 38): the merged builder for a multi-entry WIDTH-family group — N logical
    /// fonts (simple TrueType and/or Type0CidType2 composite, kind-agnostic where the program is
    /// shared) sharing one embedded program via <see cref="HolderGroupKey"/>, each already known (by
    /// <see cref="Propose(PdfDocument, IEnumerable{ValueTuple{string, int}})"/>'s width-family seeding)
    /// to carry its own 6.2.11.5 finding. Mirrors <see cref="BuildMergedReplacement"/>'s shape for N
    /// siblings instead of one, but for an IN-PLACE hmtx patch of the EXISTING program rather than a
    /// whole-face substitute: per-member target resolution (the SAME <see cref="ProgramWidthResolver"/>
    /// enumeration the singleton core above uses), union with cross-member conflict detection, ONE
    /// <see cref="SfntAdvancePatcher.Patch"/> call.
    ///
    /// <para>Task 8b review fix (Important 1): this doc comment used to say every member here already
    /// carries its own genuine width finding, with "there is no non-seed to wrap." Task 8b's own
    /// inventory-scoped expansion made that false — <paramref name="members"/> can now include an
    /// expansion-only sibling with NO width finding of its own at all. A per-member SHAPE/PARSE fact
    /// (missing /FontDescriptor, no /FontFile2, unreadable dictionary, unparseable metrics,
    /// non-Identity composite encoding, no /Widths, a self-inconsistent zero-vs-real-advance or
    /// two-codes-one-glyph declaration) is evaluated per entry as the loop below walks
    /// <paramref name="members"/>, and is genuinely true only of WHICHEVER entry failed the check —
    /// exactly the same shape <see cref="BuildMergedReplacement"/>'s notdef declines distinguish a SEED
    /// from a non-seed for. Those sites now route through <see cref="DeclineGroupFact"/> keyed on
    /// <paramref name="seedIds"/>: a SEED gets the raw reason (a genuine candidate for the fact being
    /// about it specifically), a non-seed gets it wrapped in <see cref="MergeBlockedSibling"/> so its
    /// row reads "a font sharing your program has this condition," not a lie about itself.
    /// <see cref="DeclineAll"/> is kept ONLY for the genuinely GROUP-LEVEL facts below — the
    /// cross-member width conflict, "nothing to correct," and "nothing to patch" — which are equally
    /// true of every member by construction (they are computed FROM the union, not from any one
    /// member's own shape).</para>
    ///
    /// <para>A per-member shape/parse failure still declines the WHOLE group, matching the notdef
    /// family's own "any failure blocks everyone" convention — simpler to reason about than a partial
    /// merge, and this task's own test list does not need finer granularity. Only the REASON TEXT each
    /// member receives changed (raw vs. wrapped), not which members get declined.</para>
    /// </summary>
    private static IReadOnlyList<FontProposal> BuildMergedWidthPatch(
        PdfDocument document, IReadOnlyList<FontInventoryEntry> members, string ruleId,
        ILookup<int, Finding> ruleFindings, IReadOnlySet<int> seedIds)
    {
        FontInventoryEntry first = members[0];
        FontId holder0 = first.ProgramHolderId ?? first.Id; // non-null: seeding required ProgramHolderId

        var perMember = new List<(FontInventoryEntry Entry, bool HasOther, Dictionary<ushort, double> TargetByGid, double Worst)>();
        EmbeddedFontMetrics? sharedMetrics = null;
        PdfStream? sharedFontFile2 = null;

        foreach (FontInventoryEntry entry in members)
        {
            FontId holder = entry.ProgramHolderId ?? entry.Id;
            List<Finding> mine = FontProgramFindingsFor(entry, ruleFindings);
            bool hasOther = mine.Any(f => ClauseKey(f.Clause) != "6.2.11.5");

            // Unreachable through the width-family seeding gate (Kind is already TrueType or
            // Type0CidType2 there), kept as a defensive gate for a direct caller — mirrors the
            // singleton core's own kind switch. Per-member fact (Task 8b review fix, Important 1):
            // DeclineGroupFact, not DeclineAll — see this method's own doc comment.
            if (entry.Kind is FontKind.Type3 or FontKind.Type0CidType0 or FontKind.Type1)
            {
                return DeclineGroupFact(members, ruleId, entry.Kind == FontKind.Type3
                    ? "Type 3 font widths come from each glyph's own drawing procedure, which Pellucid "
                      + "does not rewrite."
                    : "This font's program stores its advances in CFF charstrings, which Pellucid "
                      + "cannot yet rewrite.", seedIds, widthFamily: true);
            }

            if (document.GetObject(holder.ObjectNumber) is not PdfDictionary holderDict
                || Resolve(document, holderDict.Get("FontDescriptor")) is not PdfDictionary descriptor)
            {
                return DeclineGroupFact(members, ruleId,
                    "The font has no /FontDescriptor, so there is no embedded program to correct.",
                    seedIds, widthFamily: true);
            }
            if (Resolve(document, descriptor.Get("FontFile2")) is not PdfStream fontFile2)
            {
                return DeclineGroupFact(members, ruleId,
                    "The font's program is not carried as a /FontFile2 sfnt, so its advances cannot be "
                    + "patched in place.", seedIds, widthFamily: true);
            }
            sharedFontFile2 ??= fontFile2;

            if (document.GetObject(entry.Id.ObjectNumber) is not PdfDictionary fontDict
                || PdfFont.Create(fontDict, document) is not { } pdfFont)
            {
                return DeclineGroupFact(members, ruleId,
                    "This font's dictionary could not be read, so Pellucid cannot compare its widths.",
                    seedIds, widthFamily: true);
            }
            EmbeddedFontMetrics? metrics = pdfFont.GetEmbeddedMetrics();
            if (metrics is null || !metrics.IsValid)
            {
                return DeclineGroupFact(members, ruleId,
                    "The embedded font program could not be parsed, so correcting its advances would "
                    + "be a guess.", seedIds, widthFamily: true);
            }
            sharedMetrics ??= metrics;

            IEnumerable<WidthComparison> tuples;
            if (entry.Kind == FontKind.Type0CidType2)
            {
                if (pdfFont is not Type0Font type0 || type0.DescendantFont is not CidFont cid
                    || type0.EncodingName is not ("Identity-H" or "Identity-V"))
                {
                    return DeclineGroupFact(members, ruleId,
                        "This composite font's encoding is not an Identity CMap, so Pellucid cannot "
                        + "prove which glyph each character selects.", seedIds, widthFamily: true);
                }
                tuples = ProgramWidthResolver.Composite(
                    cid, metrics, cidKeyedCff: false, entry.UsedCodes.Distinct());
            }
            else
            {
                if (Resolve(document, fontDict.Get("Widths")) is not PdfArray widths)
                {
                    return DeclineGroupFact(members, ruleId,
                        "The font declares no /Widths array, so there is nothing to reconcile the "
                        + "program against.", seedIds, widthFamily: true);
                }
                tuples = ProgramWidthResolver.Simple(
                    pdfFont, metrics, widths, entry.UsedCodes.Distinct(), isTrueType: true);
            }

            var targetByGid = new Dictionary<ushort, double>();
            double memberWorst = 0;
            foreach (WidthComparison w in tuples)
            {
                memberWorst = Math.Max(memberWorst, Math.Abs(w.Declared - w.Program));

                if (w.Declared == 0 && w.Program > 0)
                {
                    return DeclineGroupFact(members, ruleId,
                        "The document declares a zero width where the program has a real advance; "
                        + "patching the program to zero would visibly change layout in renderers that "
                        + "fall back to program advances, so Pellucid leaves it alone.", seedIds,
                        widthFamily: true);
                }
                if (targetByGid.TryGetValue(w.Gid, out double existing))
                {
                    if (Math.Abs(existing - w.Declared) > FontProgramRule.WidthTolerance)
                    {
                        return DeclineGroupFact(members, ruleId,
                            "Two character codes share one glyph but declare different widths, so no "
                            + "single program advance can satisfy both.", seedIds, widthFamily: true);
                    }
                    continue;
                }
                targetByGid[w.Gid] = w.Declared;
            }

            perMember.Add((entry, hasOther, targetByGid, memberWorst));
        }

        // Union across members with cross-member conflict detection (spec-mirroring
        // BuildMergedReplacement's own width union) — the reason text is load-bearing verbatim.
        var unionTargetByGid = new Dictionary<ushort, double>();
        foreach ((FontInventoryEntry _, bool _, Dictionary<ushort, double> targetByGid, double _) in perMember)
        {
            foreach ((ushort gid, double declared) in targetByGid)
            {
                if (unionTargetByGid.TryGetValue(gid, out double existing))
                {
                    if (Math.Abs(existing - declared) > FontProgramRule.WidthTolerance)
                        return DeclineAll(members, ruleId, MergeWidthConflictReason);
                    continue;
                }
                unionTargetByGid[gid] = declared;
            }
        }

        double worst = perMember.Count > 0 ? perMember.Max(m => m.Worst) : 0;
        if (worst <= FontProgramRule.WidthTolerance)
        {
            return DeclineAll(members, ruleId,
                "The width mismatch could not be reproduced over the character codes this document "
                + "uses, so there is nothing Pellucid can safely correct.");
        }

        int upm = sharedMetrics!.UnitsPerEm <= 0 ? 1000 : sharedMetrics.UnitsPerEm;
        var advanceByGid = new Dictionary<ushort, ushort>();
        foreach ((ushort gid, double declared) in unionTargetByGid)
        {
            var fontUnits = (ushort)Math.Clamp(Math.Round(declared * upm / 1000.0), 0, ushort.MaxValue);
            if (fontUnits != sharedMetrics.GetAdvanceWidth(gid))
                advanceByGid[gid] = fontUnits;
        }
        if (advanceByGid.Count == 0)
        {
            return DeclineAll(members, ruleId,
                "Every used glyph's program advance already matches its declared width after "
                + "rounding, so there is nothing to patch.");
        }

        byte[] program = sharedFontFile2!.GetDecodedData(document.Decryptor);
        byte[]? patched = SfntAdvancePatcher.Patch(program, advanceByGid, out string? failReason);
        if (patched is null)
        {
            return DeclineAll(members, ruleId, $"The font program cannot be patched: {failReason}");
        }

        bool leavesOther = perMember.Any(m => m.HasOther);
        var proposal = new PatchWidthsProposal(
            holder0, ruleId, patched, advanceByGid.Count, worst, leavesOther,
            CoveredFonts: members.Select(m => m.Id).ToList());
        return [proposal];
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
        // findings this call was given (see Propose's own doc comment on that scoping). Task 7
        // (tracker issue 38) retired the guard itself (SharedHolderReason) for good — the manual path
        // (AssessReplacementCandidate) now builds its own holder-scoped group too, calling
        // BuildMergedReplacement directly for a multi-member group; see that method's own doc comment.
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
    /// Replaces the last-write-wins decline the automatic path used to emit here (the
    /// <c>SharedHolderReason</c> guard — retired entirely as of Task 7, including from the manual
    /// path): instead of refusing to touch any of them, this builds ONE
    /// <see cref="ReplaceProgramProposal"/> naming a target per member.
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
    /// <c>fonts.Resolve</c> call): if no SEED member (<paramref name="seedIds"/> below) would close,
    /// the whole group declines <see cref="Cid0OnlyDeclineReason"/> unconditionally — regardless of
    /// whether a substitute is even available — exactly like the singleton gate. An inventory-
    /// expansion-only member never counts either way here: it has no notdef finding to close, so it
    /// can neither rescue nor sink this gate.</para>
    ///
    /// <para><paramref name="seedIds"/>: object numbers of members that carry an ACTUAL notdef finding
    /// THIS CALL (spec §4, "Group membership is INVENTORY-scoped, not findings-scoped", 2026-08-18
    /// clarification, commit 16d7585 — review finding C1). Every entry in <paramref name="group"/> NOT
    /// in this set was pulled in purely by <see cref="Propose(PdfDocument, IEnumerable{ValueTuple{string, int}})"/>'s
    /// inventory-scoped expansion — it still becomes a full target (used codes join the coverage
    /// union, its descendant gets a real map, its own shape gates still run), but gets
    /// <c>ClosesFinding: false</c> and no <c>RestoredCodeCount</c> credit, in
    /// <see cref="BuildMergedReplacement"/>.</para>
    /// </summary>
    private IReadOnlyList<FontProposal> ProposeMergedReplace(
        PdfDocument document, IReadOnlyList<FontInventoryEntry> group, string ruleId,
        ILookup<int, Finding> ruleFindings, IReadOnlySet<int> seedIds)
    {
        // Step 1: gate every sibling individually via the shared ValidateSiblingShape validator (Task
        // 7 review) — the SAME gates ProposeProgramReplace runs 1–4 for a singleton, just fanned out
        // to the whole group: every failure wraps in MergeBlockedSibling, uniformly, on the first
        // failure (DeclineAll — contrast AssessReplacementCandidate's manual path, which wraps only a
        // non-picked member's failure).
        var siblings = new List<(FontInventoryEntry Entry, Type0Font Type0, CidFont Cid)>();
        foreach (FontInventoryEntry entry in group)
        {
            SiblingShapeResult shape = ValidateSiblingShape(document, entry);
            if (shape.Reason is { } reason)
                return DeclineAll(group, ruleId, MergeBlockedSibling(reason));

            siblings.Add((entry, shape.Type0!, shape.Cid!));
        }

        // Task 3 amendment, narrowed by review finding C1: group-wide cid0-only gate, BEFORE substitute
        // resolution — a member drawing CID 0 never closes its own finding (ISO 32000 §9.7.4.2, issue
        // 40), so if no SEED member would close, decline unconditionally, exactly like the singleton
        // gate. Only SEED members are consulted: an inventory-expansion-only sibling has no notdef
        // finding to close regardless of what it draws, so it cannot rescue this gate either.
        if (!siblings.Any(s => seedIds.Contains(s.Entry.Id.ObjectNumber) && !s.Entry.UsedCodes.Contains(0)))
            return DeclineGroupFact(group, ruleId, Cid0OnlyDeclineReason, seedIds);

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
            ? new MergedResult(DeclineGroupFact(group, ruleId,
                $"No font matching '{first.FamilyName}' is installed on this computer. Installing it "
                + "would let Pellucid replace the deficient program.", seedIds), null)
            : BuildMergedReplacement(siblings, ruleId, match.Data, match.FaceIndex, seedIds);

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
                    MergedResult retry = BuildMergedReplacement(
                        siblings, ruleId, retryMatch.Data, retryMatch.FaceIndex, seedIds);
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
    /// on a decline, so <see cref="ProposeMergedReplace"/>'s retry can decide without reclassifying.
    /// <paramref name="seedIds"/> is <see cref="ProposeMergedReplace"/>'s own parameter of the same
    /// name, forwarded unchanged (review finding C1) — see that method's doc comment.
    /// <paramref name="sourceDescription"/> (Task 7, tracker issue 38) mirrors
    /// <see cref="BuildReplacement"/>'s own optional override: null — <see cref="ProposeMergedReplace"/>'s
    /// only caller shape — falls back to the resolved substitute's own description;
    /// <see cref="AssessReplacementCandidate"/>'s manual path always supplies the user-visible source
    /// it was given, so its merged proposals report it exactly like its singleton ones already do.
    /// </summary>
    private static MergedResult BuildMergedReplacement(
        List<(FontInventoryEntry Entry, Type0Font Type0, CidFont Cid)> siblings, string ruleId,
        byte[] bytes, int faceIndex, IReadOnlySet<int> seedIds, string? sourceDescription = null)
    {
        IReadOnlyList<FontInventoryEntry> allEntries = siblings.Select(s => s.Entry).ToList();
        FontInventoryEntry first = siblings[0].Entry;

        ByteGateOutcome gates = RunByteGates(bytes, faceIndex, first.FamilyName, simpleFont: false);
        if (gates.HardBlockReason is not null)
        {
            return new MergedResult(
                DeclineGroupFact(allEntries, ruleId, gates.HardBlockReason, seedIds), gates.Classified?.Format);
        }

        EmbeddedFontMetrics metrics = gates.Metrics!;
        string resolvedFamily = gates.ResolvedFamily!;
        ClassifiedProgram classified = gates.Classified!;

        if (classified.Format != FontProgramFormat.TrueType)
        {
            return new MergedResult(DeclineGroupFact(allEntries, ruleId,
                $"The face found for '{first.FamilyName}' is not a TrueType program, and only a "
                + "TrueType program can replace this font's without rewriting CFF charstrings.",
                seedIds),
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
                return new MergedResult(DeclineGroupFact(allEntries, ruleId,
                    $"'{resolvedFamily}' cannot honestly render {mapResult.Unresolvable.Count} of this "
                    + $"font's characters (first: CID {firstUnresolvable}), so replacing the program "
                    + "would still leave missing glyphs — Pellucid makes no partial replacements.",
                    seedIds),
                    classified.Format);
            }

            // M1 (review finding), symmetric with BuildReplacement's own guard: an inventory-scoped
            // expansion (C1) can pull in a sibling that is never actually drawn anywhere in the
            // document — FontInventory still creates an entry for an unused font resource — whose
            // UsedCodes is empty and therefore whose CidToGid is empty too (not a partial-coverage
            // problem; Unresolvable is empty as well, since there is nothing to resolve). Left
            // unguarded, ToStreamBytes would write .notdef for every CID in that sibling's slice.
            if (mapResult.CidToGid.Count == 0)
            {
                return new MergedResult(DeclineGroupFact(allEntries, ruleId,
                    "This font draws no characters Pellucid can resolve, so there is nothing a "
                    + "replacement program could restore.",
                    seedIds),
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
        // ClosesFinding (review finding C1): only a SEED member — one carrying an ACTUAL notdef
        // finding this call — can ever close anything; an inventory-expansion-only sibling's finding
        // is always false, whether or not it draws CID 0 (spec §4, "Group membership is
        // INVENTORY-scoped, not findings-scoped", 2026-08-18 clarification).
        bool Closes(FontInventoryEntry entry) =>
            seedIds.Contains(entry.Id.ObjectNumber) && !entry.UsedCodes.Contains(0);

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
                    ClosesFinding: Closes(entry)));
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
                            return new MergedResult(DeclineGroupFact(allEntries, ruleId,
                                $"Two fonts sharing this embedded program map character code {cidCode} "
                                + "to different characters, so one replacement program cannot serve "
                                + "both.",
                                seedIds),
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
                    sharedHolder, entry.Id, unionMap, maxCid, ClosesFinding: Closes(entry)));
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
                        return new MergedResult(DeclineGroupFact(
                            allEntries, ruleId, MergeWidthConflictReason, seedIds),
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
                return new MergedResult(DeclineGroupFact(allEntries, ruleId,
                    $"The substitute's program cannot be width-patched to this font's declared widths: "
                    + failReason,
                    seedIds),
                    classified.Format);
            }
            program = patched;
        }

        // RestoredCodeCount (spec §4 step 5): sum over SEED siblings of distinct used codes that
        // resolve to .notdef in THEIR OWN old program — same predicate BuildReplacement uses for a
        // singleton. An inventory-expansion-only sibling contributes nothing (review finding C1):
        // nothing of its was asked to be fixed, so nothing of its counts as "restored" even where its
        // own old program happens to draw some other .notdef code this call's finding set never named
        // — its old-program metrics are not even read, since nothing here depends on them.
        var restored = 0;
        foreach ((FontInventoryEntry entry, Type0Font type0, CidFont cid) in siblings)
        {
            if (!seedIds.Contains(entry.Id.ObjectNumber)) continue;

            EmbeddedFontMetrics? oldMetrics = type0.GetEmbeddedMetrics();
            if (oldMetrics is null || !oldMetrics.IsValid)
            {
                return new MergedResult(DeclineGroupFact(allEntries, ruleId,
                    "The font-program finding could not be reproduced against this document's current "
                    + "state, so there is nothing Pellucid can safely correct.",
                    seedIds),
                    classified.Format);
            }
            bool cidKeyed = entry.Kind == FontKind.Type0CidType0;
            restored += entry.UsedCodes.Distinct().Count(code => MapsToNotdefGlyph(code, cidKeyed, cid, oldMetrics));
        }

        FontDescriptorValues? descriptorValues = FontDescriptorMetrics.Compute(program, FontProgramFormat.TrueType);
        if (descriptorValues is null)
        {
            return new MergedResult(DeclineGroupFact(allEntries, ruleId,
                "The substitute program's metrics could not be read, so an honest /FontDescriptor "
                + "cannot be written for it.",
                seedIds),
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
        string source = sourceDescription ?? $"{resolvedFamily} ({style}) — from your system fonts";

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
    /// identical, explanation (spec §4). Used ONLY by the Step 1 per-sibling shape-gate call sites,
    /// which already wrap <paramref name="reason"/> in <see cref="MergeBlockedSibling"/> themselves
    /// before calling here (the reason genuinely IS "another font's" for every member except the one
    /// sibling that actually failed, and the brief's own spec text wants identical text for everyone
    /// regardless). Every OTHER decline site uses <see cref="DeclineGroupFact"/> instead (review
    /// round 2, finding 3) — see that method's doc comment for why the two must not be conflated.</summary>
    private static IReadOnlyList<FontProposal> DeclineAll(
        IReadOnlyList<FontInventoryEntry> group, string ruleId, string reason) =>
        group.Select(entry => (FontProposal)Decline(entry, ruleId, reason)).ToList();

    /// <summary>
    /// Declines every member of a group over a GROUP-LEVEL fact (no substitute installed, a
    /// non-TrueType face, a coverage gap, a CID/width conflict, an unparseable substitute, ...) —
    /// every decline site in <see cref="ProposeMergedReplace"/>/<see cref="BuildMergedReplacement"/>
    /// AFTER Step 1's per-sibling shape gate. Review round 2, finding 3: <paramref name="reason"/> is
    /// often untrue as a statement about a NON-SEED (inventory-expansion-only) member specifically —
    /// "this font draws character code 0" is false for a sibling that draws no CID 0 at all but
    /// happens to share a holder with one that does; "no font matching '{first sibling's family}' is
    /// installed" names a DIFFERENT font's family for every member but the first. A SEED member
    /// (<paramref name="seedIds"/>) gets <paramref name="reason"/> verbatim — it is a genuine
    /// candidate for the fact being about it specifically (the group's own seed(s) are always seeds).
    /// A NON-SEED member gets it wrapped in <see cref="MergeBlockedSibling"/> instead, so its row
    /// reads truthfully as "a font SHARING your program has this condition," not a claim about
    /// itself. NOT used by Step 1's shape-gate declines (<see cref="DeclineAll"/>) — those already
    /// wrap their reason once, uniformly for every member per the brief's own spec text; wrapping
    /// again here would double the template.
    ///
    /// <para><paramref name="widthFamily"/> (review fix, M-1): forwarded to <see cref="MergeBlockedSibling"/>
    /// so a width-family caller (<see cref="BuildMergedWidthPatch"/>) gets wording that matches what it
    /// actually does (patching shared advances in place) rather than the replace family's "merged
    /// replacement" template, which used to be worded for a whole-face swap regardless of which family
    /// routed a per-member fact through here. Defaults to <c>false</c> — every pre-existing (replace-
    /// family) call site is unaffected; Task 9's decline taxonomy keys on ITS exact text, unchanged.</para>
    /// </summary>
    private static IReadOnlyList<FontProposal> DeclineGroupFact(
        IReadOnlyList<FontInventoryEntry> group, string ruleId, string reason, IReadOnlySet<int> seedIds,
        bool widthFamily = false) =>
        group.Select(entry => (FontProposal)Decline(
                entry, ruleId,
                seedIds.Contains(entry.Id.ObjectNumber) ? reason : MergeBlockedSibling(reason, widthFamily)))
            .ToList();

    /// <summary>Verbatim per the controller brief (spec §6) — a later sweep's taxonomy keys on this
    /// exact template, for the REPLACE family (<paramref name="widthFamily"/>: false, the default).
    /// Wraps a failing sibling's own would-be SINGLETON decline reason (e.g. what
    /// <see cref="ProposeProgramReplace"/> would have said about it alone) for the whole group's
    /// per-member decline.
    ///
    /// <para>Review fix (M-1): <see cref="BuildMergedWidthPatch"/> routes ITS OWN per-member facts
    /// through this same helper (via <see cref="DeclineGroupFact"/>'s <c>widthFamily</c> parameter and
    /// directly at its blocked-key path), but the width family patches shared advances in place — it
    /// never replaces a program or writes a new face — so "cannot be included in a merged replacement"
    /// and "replacing the shared program... would corrupt the others" describe an operation the width
    /// family does not perform. <paramref name="widthFamily"/> true selects wording naming what
    /// actually happens instead. The REPLACE family's template text is UNCHANGED — Task 9's sweep keys
    /// on it verbatim.</para>
    /// </summary>
    private static string MergeBlockedSibling(string reason, bool widthFamily = false) =>
        widthFamily
            ? "Another font sharing this font's embedded program cannot be included in a merged width "
              + $"patch ({reason}), and patching the shared program's advances for only some of its "
              + "fonts would corrupt the others."
            : "Another font sharing this font's embedded program cannot be included in a merged "
              + $"replacement ({reason}), and replacing the shared program for only some of its fonts "
              + "would corrupt the others.";

    /// <summary>Result of <see cref="ValidateSiblingShape"/>: either the parsed
    /// (<see cref="Type0Font"/>, <see cref="CidFont"/>) pair, or the RAW (unwrapped) <c>Reason</c> a
    /// shape gate failed — exactly the same reason text a singleton assessment of this entry alone
    /// would report. Wrapping that reason for a group context (or not) is the CALLER's decision, not
    /// this validator's: <see cref="ProposeMergedReplace"/>'s Step 1 wraps every failure uniformly
    /// (<see cref="MergeBlockedSibling"/>, via <see cref="DeclineAll"/>), while
    /// <see cref="AssessReplacementCandidate"/>'s manual-path group loop wraps only a non-picked
    /// member's failure — two different policies over the SAME underlying fact, kept in the two
    /// callers rather than flattened into this helper.</summary>
    private readonly record struct SiblingShapeResult(Type0Font? Type0, CidFont? Cid, string? Reason);

    /// <summary>
    /// The per-sibling shape gate <see cref="ProposeMergedReplace"/>'s Step 1 and
    /// <see cref="AssessReplacementCandidate"/>'s manual-path group loop both run over every group
    /// member (extracted from a near-identical copy in each, including all five reason-string
    /// literals — Task 7 review): unaddressable, non-composite kind, unreadable-as-composite,
    /// non-Identity encoding, no <c>/ToUnicode</c>. Shared so these five strings — pinned verbatim by
    /// Task 9's corpus-wide decline-reason sweep — exist in exactly one place; a caller that revises
    /// one no longer needs a second, independently-maintained copy to follow.
    /// </summary>
    private static SiblingShapeResult ValidateSiblingShape(PdfDocument document, FontInventoryEntry entry)
    {
        if (!entry.IsAddressable)
        {
            return new SiblingShapeResult(null, null,
                "This font is written directly into the page's resources rather than as its own "
                + "object, so Pellucid cannot address its font program to correct it.");
        }

        if (entry.Kind is not (FontKind.Type0CidType0 or FontKind.Type0CidType2))
            return new SiblingShapeResult(null, null, SimpleFontMissingGlyphReason);

        if (document.GetObject(entry.Id.ObjectNumber) is not PdfDictionary fontDict
            || PdfFont.Create(fontDict, document) is not Type0Font type0
            || type0.DescendantFont is not CidFont cid)
        {
            return new SiblingShapeResult(null, null,
                "This font's dictionary could not be read as a composite font, so Pellucid cannot "
                + "correct its program.");
        }

        if (type0.EncodingName is not ("Identity-H" or "Identity-V"))
        {
            return new SiblingShapeResult(null, null,
                "This composite font's encoding is not an Identity CMap, so Pellucid cannot prove "
                + "which glyph each character selects.");
        }

        if (type0.ToUnicode is null)
        {
            return new SiblingShapeResult(null, null,
                "This font declares no /ToUnicode mapping, which is the only honest source for what "
                + "its characters mean — a replacement face cannot be chosen without it.");
        }

        return new SiblingShapeResult(type0, cid, null);
    }

    /// <summary>Review round 1, finding I1: the reason a width-family group is blocked by
    /// <paramref name="blocker"/> — a sibling sharing the SAME <see cref="HolderGroupKey"/> whose own
    /// Kind is outside the width-patchable set, or which is not addressable. Mirrors the SAME per-kind
    /// text <see cref="ProposeWidthPatchOnly"/>'s own kind switch and unaddressable gate produce for
    /// that sibling if it were assessed directly — a font declines with the SAME sentence about itself
    /// whether it is the one being assessed or the one blocking a neighbor's merge.</summary>
    private static string BlockingSiblingReason(FontInventoryEntry blocker) =>
        !blocker.IsAddressable
            ? "This font is written directly into the page's resources rather than as its own object, "
              + "so Pellucid cannot address its font program to correct it."
            : blocker.Kind switch
            {
                FontKind.Type3 => "Type 3 font widths come from each glyph's own drawing procedure, "
                    + "which Pellucid does not rewrite.",
                FontKind.Type0CidType0 or FontKind.Type1 => "This font's program stores its advances "
                    + "in CFF charstrings, which Pellucid cannot yet rewrite.",
                _ => "This font's program is not one Pellucid can patch in place.",
            };

    /// <summary>
    /// Mirrors <see cref="AssessCandidate"/>'s shape for the whole-program-replacement path: the SAME
    /// entry-shape gates <see cref="ProposeMergedReplace"/>'s Step 1 runs (unaddressable, non-composite
    /// kind, an unreadable/non-Identity composite, no /ToUnicode) become hard blocks here instead of
    /// declines, then <paramref name="candidateBytes"/> runs through the SAME construction core the
    /// automatic path uses — <see cref="BuildReplacement"/> for a singleton,
    /// <see cref="BuildMergedReplacement"/> for a shared-holder group.
    ///
    /// <para>Task 7 (tracker issue 38): the TEMPORARY last-write-wins guard this method used to call
    /// (<c>SharedHolderReason</c>) is retired. Assessing a candidate against ANY member of a group of
    /// fonts sharing one embedded program now assesses the WHOLE group — built the same way
    /// <see cref="Propose(PdfDocument, IEnumerable{ValueTuple{string, int}})"/>'s inventory-scoped
    /// expansion does (every OTHER inventory entry sharing <paramref name="entry"/>'s
    /// <see cref="HolderGroupKey"/> joins, kind-agnostic) — with <paramref name="entry"/> as the
    /// group's ONLY seed: every sibling pulled in by the expansion gets <c>ClosesFinding: false</c>,
    /// exactly mirroring the automatic path's "nothing of theirs was asked to be fixed" semantics.
    /// Routes through <see cref="BuildMergedReplacement"/> DIRECTLY, never
    /// <see cref="ProposeMergedReplace"/> (controller ruling): the manual path is a deliberate user
    /// override, and <see cref="ProposeMergedReplace"/>'s group-wide <see cref="Cid0OnlyDeclineReason"/>
    /// gate would be a NEW restriction on that override — honesty here is carried by a target's own
    /// <c>ClosesFinding</c>, the same way the pre-existing singleton manual path already reports
    /// <c>ClosesFinding: false</c> for a CID-0-drawing pick rather than refusing it outright. A
    /// single-member group (no sibling shares the holder, or the picked entry has none) is
    /// byte-identical to the pre-Task-7 singleton behaviour.</para>
    ///
    /// <para>Unlike <see cref="AssessCandidate"/>, a coverage gap is a HARD BLOCK here, not a warning
    /// (design Decision 7): the embed path's warning only ever adds .notdef glyphs the user can already
    /// see are missing, but a replacement CID whose ToUnicode value the substitute cannot render has no
    /// honest fallback — the CIDToGIDMap entry would point at a real but wrong glyph, or 0, either way
    /// silently.</para>
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

        // Task 7 (tracker issue 38): build the shared-holder group the SAME way Propose's inventory-
        // scoped expansion does (spec §4), via the shared ExpandHolderGroup helper (Task 7 review —
        // see its own doc comment for the dedup-key rationale) — the picked entry seeds it, every
        // OTHER inventory entry sharing its HolderGroupKey joins, kind-agnostic. IsAddressable (just
        // gated above) guarantees entry.ProgramHolderId is non-null (FontInventory.BuildEntry ties the
        // two together — IsAddressable requires the program holder to be indirect, and only an
        // indirect holder gets a ProgramHolderId at all), so the "no ProgramHolderId" branch below is
        // defensive, not reachable through this call site.
        IReadOnlyList<FontInventoryEntry> inventory = FontInventory.Read(document);
        var group = new List<FontInventoryEntry> { entry };
        if (entry.ProgramHolderId is not null)
        {
            ExpandHolderGroup(document, inventory, group, HolderGroupKey(document, entry));
            // Review fix (Important 2): same canonicalization the automatic path applies — see
            // CanonicalizeGroupOrder's own doc comment. The picked entry (`entry`) is no longer
            // guaranteed to land at group[0] after this; the picked-row lookup below (and the
            // FirstOrDefault a few lines further down) is keyed by identity, not position, so neither
            // is affected by the reorder.
            CanonicalizeGroupOrder(group);
        }

        // Step 1: the shared ValidateSiblingShape validator (Task 7 review) runs the SAME gates
        // ProposeMergedReplace's own Step 1 runs — unaddressable, non-composite kind, unreadable-as-
        // composite, non-Identity encoding, no /ToUnicode — over every group member. A failure BLOCKS
        // the whole assessment — a non-composite (or otherwise malformed) sibling sharing the holder is
        // a real corruption hazard, not an over-decline (Task 4 round-2 ruling): writing the shared
        // program would corrupt it. Unlike ProposeMergedReplace's uniform MergeBlockedSibling wrap
        // (DeclineAll), the picked entry's own failure reports DIRECTLY here (it already passed the two
        // gates above, so only the latter three checks can still fire for it); any OTHER member's
        // failure wraps in MergeBlockedSibling, naming it as a fact about a sibling.
        var seedIds = new HashSet<int> { entry.Id.ObjectNumber };
        var siblings = new List<(FontInventoryEntry Entry, Type0Font Type0, CidFont Cid)>();
        foreach (FontInventoryEntry member in group)
        {
            SiblingShapeResult shape = ValidateSiblingShape(document, member);
            if (shape.Reason is { } reason)
            {
                bool isPicked = member.Id.ObjectNumber == entry.Id.ObjectNumber
                    && member.ProgramHolderId?.ObjectNumber == entry.ProgramHolderId?.ObjectNumber;
                return new CandidateAssessment(
                    null, isPicked ? reason : MergeBlockedSibling(reason), [], null);
            }

            siblings.Add((member, shape.Type0!, shape.Cid!));
        }

        if (siblings.Count == 1)
        {
            // Singleton group: byte-identical to the pre-Task-7 path — BuildReplacement's own core,
            // never BuildMergedReplacement's group machinery (whose per-target and per-member decline
            // text is worded for a genuine multi-font group).
            (FontInventoryEntry singleton, Type0Font singletonType0, CidFont singletonCid) = siblings[0];
            FontId holder = singleton.ProgramHolderId ?? singleton.Id;
            ReplacementResult single = BuildReplacement(
                document, singleton, ruleId, holder, singletonType0, singletonCid,
                candidateBytes, faceIndex, sourceDescription);

            return single.Proposal switch
            {
                DeclineProposal decline => new CandidateAssessment(single.Format, decline.Reason, [], null),
                ReplaceProgramProposal replace => new CandidateAssessment(single.Format, null, [], replace),
                _ => throw new InvalidOperationException(
                    "BuildReplacement returned a proposal type neither Decline nor Replace produces."),
            };
        }

        MergedResult merged = BuildMergedReplacement(
            siblings, ruleId, candidateBytes, faceIndex, seedIds, sourceDescription);

        if (merged.Proposals is [ReplaceProgramProposal mergedReplace])
            return new CandidateAssessment(merged.Format, null, [], mergedReplace);

        // Every row is a DeclineProposal here (BuildMergedReplacement's only two shapes). Doc
        // correction (whole-branch review, Important 2 follow-on): before CanonicalizeGroupOrder
        // existed, `group` always started as `{ entry }` (see this method's own construction above)
        // and only ever grew by appending, so the picked entry was ALWAYS `group[0]` — the FirstOrDefault
        // search below was provably dead code, always resolving to `declines[0]`, and an earlier version
        // of this comment wrongly explained that inertness as "reason texts are uniform across rows" (they
        // are not — DeclineGroupFact gives a seed a different, unwrapped reason from a non-seed's wrapped
        // one). Important 2's canonicalization changed the actual mechanics: `group` (and therefore
        // `merged.Proposals`/`declines`, which preserve `siblings`/`group` order throughout
        // BuildMergedReplacement) is now sorted by (ProgramHolderId.ObjectNumber, Id.ObjectNumber), so the
        // picked entry can land at ANY position depending on its object numbers relative to its siblings'.
        // The lookup below is therefore load-bearing now, not defensive: it is what still finds the
        // picked entry's own (correctly unwrapped) row after the reorder. The `?? declines[0]` fallback
        // stays defensive only — the picked entry is always a group member, so the search should never
        // actually miss.
        IReadOnlyList<DeclineProposal> declines = merged.Proposals.Cast<DeclineProposal>().ToList();
        DeclineProposal pickedDecline =
            declines.FirstOrDefault(d => d.Font.ObjectNumber == entry.Id.ObjectNumber) ?? declines[0];
        return new CandidateAssessment(merged.Format, pickedDecline.Reason, [], null);
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

        // ClosesFinding (review finding I4, fixing Task 3's hardcoded `true`): a font that draws CID 0
        // never closes its 6.2.11.8 finding (ISO 32000 §9.7.4.2, issue 40) — no font-side fix can
        // change that. For the AUTOMATIC path (ProposeProgramReplace), the cid0 gate above already
        // declined every such font before construction ever reaches here, so `!UsedCodes.Contains(0)`
        // is always true there — byte-identical to the old hardcoded value. For the MANUAL path
        // (AssessReplacementCandidate), which has no equivalent cid0 gate, this now correctly reports
        // false for a user-picked substitute on a CID-0-drawing font instead of falsely claiming the
        // finding closes.
        var target = new ReplaceTarget(
            holder, entry.Id, mapResult.CidToGid, mapResult.MaxCid,
            ClosesFinding: !entry.UsedCodes.Contains(0));
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
