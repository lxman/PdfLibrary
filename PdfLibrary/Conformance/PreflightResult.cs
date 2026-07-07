namespace PdfLibrary.Conformance;

/// <summary>
/// Outcome of a preflight run: the profile checked and every <see cref="Finding"/> produced.
/// </summary>
public sealed class PreflightResult
{
    /// <summary>The conformance profile that was checked.</summary>
    public required ConformanceProfile Profile { get; init; }

    /// <summary>All findings, in the order rules produced them.</summary>
    public required IReadOnlyList<Finding> Findings { get; init; }

    /// <summary>True when no finding has <see cref="FindingSeverity.Error"/> severity.</summary>
    public bool Conforms => !Findings.Any(f => f.Severity == FindingSeverity.Error);

    /// <summary>Findings with <see cref="FindingSeverity.Error"/> severity.</summary>
    public IEnumerable<Finding> Errors => Findings.Where(f => f.Severity == FindingSeverity.Error);

    /// <summary>Findings with <see cref="FindingSeverity.Warning"/> severity.</summary>
    public IEnumerable<Finding> Warnings => Findings.Where(f => f.Severity == FindingSeverity.Warning);
}
