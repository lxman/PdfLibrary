namespace PdfLibrary.Fonts.Remediation;

/// <summary>What the planner proposes doing to one font for one rule. Data only — applying a
/// proposal is <see cref="Editing.PdfDocumentEditor"/>'s job, and the separation is what lets a
/// caller stage a proposal without writing anything.</summary>
public abstract record FontProposal(FontId Font, string RuleId);

/// <summary>
/// Write a <c>/ToUnicode</c> CMap. <paramref name="Provable"/> holds mappings DERIVED from glyph
/// names or a standard encoding — never inferred. <paramref name="NeedsUserInput"/> lists the codes
/// with no honest answer, which the user supplies.
///
/// <para>A proposal with entries in both is the normal case and does NOT resolve the finding on its
/// own. Callers must not report it as a completed fix.</para>
/// </summary>
public sealed record ToUnicodeProposal(
    FontId Font,
    string RuleId,
    IReadOnlyDictionary<int, string> Provable,
    IReadOnlyList<int> NeedsUserInput) : FontProposal(Font, RuleId);

/// <summary>
/// This font cannot be remediated here, and why.
///
/// <para>NOT the same as Pellucid's <c>DeclinedByDesign</c>, which is a position about a RULE that
/// holds on every document. This is a fact about THIS font in THIS document on THIS machine — a
/// direct dictionary, an unparseable program, a substitute that is not installed. Rendering them
/// identically would let a machine-specific gap read as deliberate policy.</para>
/// </summary>
public sealed record DeclineProposal(
    FontId Font, string RuleId, string Reason) : FontProposal(Font, RuleId);

/// <summary>
/// Embed <paramref name="Program"/> as <paramref name="Font"/>'s font program. <paramref name="Font"/>
/// is the PROGRAM HOLDER (design §3.2) — <c>entry.ProgramHolderId ?? entry.Id</c> — never the logical
/// font, because <c>/FontFile*</c> and <c>/FontDescriptor</c> live there.
///
/// <para><paramref name="SourceDescription"/> is derived from the RESOLVED bytes in
/// <paramref name="Program"/>, never from the request that produced them — the confirmation the user
/// sees must name the face that will actually be written (design §7), and a fuzzy system-font locator
/// can resolve to something other than what was asked for.</para>
/// </summary>
public sealed record EmbedProposal(
    FontId Font, string RuleId,
    string SourceDescription,
    byte[] Program, FontProgramFormat Format) : FontProposal(Font, RuleId);

/// <summary>
/// Rewrite <paramref name="Font"/>'s subset declaration to describe the glyphs its embedded program
/// actually contains. <paramref name="Font"/> is the PROGRAM HOLDER (design §3.2), because
/// <c>/FontDescriptor</c> lives there.
///
/// <para>Exactly one of <paramref name="GlyphNames"/> (a Type1 <c>/CharSet</c>) and
/// <paramref name="Cids"/> (a CID <c>/CIDSet</c>) is non-null — a font has one kind of declaration or
/// the other, never both.</para>
///
/// <para>Both sets are enumerated from the program by <c>SubsetProgramGlyphs</c>, the same code the
/// rule compares against, so applying this proposal necessarily satisfies the rule.</para>
/// </summary>
public sealed record RegenerateDeclarationProposal(
    FontId Font, string RuleId,
    IReadOnlySet<string>? GlyphNames,
    IReadOnlySet<int>? Cids) : FontProposal(Font, RuleId);

/// <summary>
/// Patch <paramref name="Font"/>'s embedded program's hmtx advances to match its declared widths,
/// for a <c>font-program</c> 6.2.11.5 finding. <paramref name="Font"/> is the PROGRAM HOLDER
/// (design §3.2) — <c>entry.ProgramHolderId ?? entry.Id</c> — because <c>/FontFile2</c> lives there.
///
/// <para><paramref name="PatchedProgram"/> is the sfnt with only hmtx (and, on expansion, hhea and
/// head's checksum) touched — every other table is byte-identical. <paramref name="GlyphsPatched"/>
/// counts distinct glyph ids whose advance changed. <paramref name="WorstDiffBefore"/> is the worst
/// declared-vs-program discrepancy (glyph-space units) observed across the used codes BEFORE the
/// patch — the same figure the triggering 6.2.11.5 finding reports. <paramref name="LeavesOtherFindings"/>
/// is true when this font also carries a font-program finding this proposal does not address (e.g. a
/// .notdef glyph), so a caller applying this proposal must not report the font as fully remediated.
/// <paramref name="CoveredFonts"/> lists the sibling LOGICAL font ids that share this program holder's
/// row (row membership, Task 8) — non-empty (the holder's own logical font is always at least one
/// member).</para>
/// </summary>
public sealed record PatchWidthsProposal(
    FontId Font, string RuleId,
    byte[] PatchedProgram,
    int GlyphsPatched,
    double WorstDiffBefore,
    bool LeavesOtherFindings,
    IReadOnlyList<FontId> CoveredFonts) : FontProposal(Font, RuleId)
{
    public IReadOnlyList<FontId> CoveredFonts { get; init; } = CoveredFonts.Count > 0
        ? CoveredFonts
        : throw new ArgumentException(
            "A width-patch proposal must cover at least one logical font.", nameof(CoveredFonts));
}

/// <summary>One wrapper's slice of a whole-face swap: the descendant that carries the program for
/// THIS wrapper (<see cref="Font"/>, the PROGRAM HOLDER), the wrapper itself
/// (<see cref="CompositeFont"/>), and the CIDToGIDMap resolved from THIS wrapper's own /ToUnicode.
/// Direct sharing (N wrappers, one descendant) emits N targets naming the SAME <see cref="Font"/>
/// with IDENTICAL maps — the planner's guarantee, the editor's assertion (Task 5).
///
/// <para><paramref name="ClosesFinding"/> is whether THIS member font's own 6.2.11.8 finding
/// actually closes when the proposal applies — the rule emits at most one 6.2.11.8 finding per font
/// (issue 40), and a font that draws CID 0 keeps that finding no matter what the replacement does,
/// so a member drawing CID 0 closes nothing. In THIS task (singleton proposals only) the planner
/// always constructs targets with <c>true</c>: <see cref="FontRemediationPlanner.ProposeProgramReplace"/>
/// only reaches proposal construction after its CID-0 decline gate, so a singleton that proposes at
/// all always closes its own finding. Task 4 is the one that wires <c>false</c> values, for group
/// members that draw CID 0 while other members in the same group do not.</para>
/// </summary>
public sealed record ReplaceTarget(
    FontId Font,
    FontId CompositeFont,
    IReadOnlyDictionary<int, ushort> CidToGid,
    int MaxCid,
    bool ClosesFinding);

/// <summary>Replace a set of composite fonts' embedded programs entirely with one substitute face —
/// a WHOLE-FACE SWAP: every code of each target font renders in the substitute afterward; only the
/// dead codes gain glyphs, but letterforms change font-wide (spec §3). <paramref name="Targets"/>
/// carries one entry per wrapper this proposal rewrites (§6 direct-sharing: N wrappers sharing one
/// descendant emit N targets naming the SAME <see cref="ReplaceTarget.Font"/>); in this task every
/// proposal is a singleton (exactly one target). <paramref name="Program"/> is the substitute sfnt
/// ALREADY advance-patched to the declared /W + /DW widths (spec §3 step 8), so applying this
/// proposal can never create a width finding. All fields are planner-resolved data; the editor
/// applies mechanically.</summary>
public sealed record ReplaceProgramProposal(
    IReadOnlyList<ReplaceTarget> Targets,
    string RuleId,
    string SourceDescription,
    byte[] Program,
    FontProgramFormat Format,
    int RestoredCodeCount,
    string NewBaseFont,
    FontDescriptorValues Descriptor,
    int DescriptorFlags)
    : FontProposal(Targets[0].Font, RuleId)
{
    public IReadOnlyList<ReplaceTarget> Targets { get; init; } = Targets.Count > 0
        ? Targets
        : throw new ArgumentException(
            "A replace proposal must name at least one target.", nameof(Targets));
}

/// <summary>Everything the planner proposes for one document.</summary>
public sealed record FontRemediationProposal(IReadOnlyList<FontProposal> Fonts);
