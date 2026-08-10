namespace PdfLibrary.Fonts.Remediation;

/// <summary>
/// What running a caller-supplied substitute font's bytes through
/// <see cref="FontRemediationPlanner.AssessCandidate"/>'s gate chain found.
///
/// <para>Splits into a HARD block (<paramref name="HardBlockReason"/>, never embeddable — the SAME
/// classify/fsType/PFB/Table-124 gates <c>FontRemediationPlanner.ProposeEmbed</c> runs, because
/// those are facts about what Pellucid can legally and mechanically write, not policy) and WARNINGS
/// (<paramref name="Warnings"/>, a coverage shortfall or a Symbol/Latin mismatch — the two checks that
/// are a judgement call on the manual path precisely because a human picked this candidate on purpose,
/// unlike the automatic path where the same coverage gap is an outright decline).</para>
/// </summary>
/// <param name="Format">The candidate's classified format, or null when the bytes could not be
/// classified at all.</param>
/// <param name="HardBlockReason">Non-null means the candidate cannot be embedded; <paramref name="Proposal"/>
/// is null whenever this is non-null.</param>
/// <param name="Warnings">Selectable-with-consequence findings the user may accept anyway. Empty when
/// <paramref name="HardBlockReason"/> is non-null.</param>
/// <param name="Proposal">Ready to stage iff <paramref name="HardBlockReason"/> is null.</param>
public sealed record CandidateAssessment(
    FontProgramFormat? Format,
    string? HardBlockReason,
    IReadOnlyList<string> Warnings,
    EmbedProposal? Proposal);
