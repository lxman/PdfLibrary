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

/// <summary>Everything the planner proposes for one document.</summary>
public sealed record FontRemediationProposal(IReadOnlyList<FontProposal> Fonts);
