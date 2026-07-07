namespace PdfLibrary.Conformance;

/// <summary>
/// A single conformance check. A rule inspects the document via <see cref="ConformanceContext"/>
/// and yields zero or more <see cref="Finding"/>s. Rules are read-only and stateless — one instance
/// is reused across runs — so they must keep no per-document state.
/// </summary>
internal interface IConformanceRule
{
    /// <summary>Stable identifier, copied onto every <see cref="Finding.RuleId"/> this rule emits.</summary>
    string RuleId { get; }

    /// <summary>
    /// Profiles this rule applies to. The <see cref="Preflighter"/> runs the rule only when the
    /// target profile intersects this set.
    /// </summary>
    ConformanceProfile AppliesToProfiles { get; }

    /// <summary>Runs the check and returns any findings (empty when the document satisfies the rule).</summary>
    IEnumerable<Finding> Check(ConformanceContext context);
}
