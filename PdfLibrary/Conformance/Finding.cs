namespace PdfLibrary.Conformance;

/// <summary>Severity of a conformance <see cref="Finding"/>.</summary>
public enum FindingSeverity
{
    /// <summary>A violation that makes the document non-conformant for the target profile.</summary>
    Error,

    /// <summary>A deviation that is permitted but discouraged, or that could not be fully verified.</summary>
    Warning,

    /// <summary>Informational note; does not affect conformance.</summary>
    Info,
}

/// <summary>
/// A single conformance observation produced by an <see cref="IConformanceRule"/>. Errors make a
/// document non-conformant (see <see cref="PreflightResult.Conforms"/>); warnings and info notes
/// do not.
/// </summary>
public sealed class Finding
{
    /// <summary>Stable identifier of the rule that produced this finding (e.g. <c>"encrypt"</c>).</summary>
    public required string RuleId { get; init; }

    /// <summary>Severity of the finding.</summary>
    public required FindingSeverity Severity { get; init; }

    /// <summary>
    /// Reference to the governing clause in the target standard (e.g. <c>"ISO 19005-2:2011, 6.1.3"</c>).
    /// </summary>
    public required string Clause { get; init; }

    /// <summary>Human-readable description of the problem.</summary>
    public required string Message { get; init; }

    /// <summary>Zero-based page index the finding applies to, or null for a document-level finding.</summary>
    public int? PageIndex { get; init; }

    /// <summary>Object number of the offending object, or null when not applicable.</summary>
    public int? ObjectNumber { get; init; }

    public override string ToString()
    {
        string where = PageIndex is { } p ? $" (page {p + 1})" : string.Empty;
        return $"[{Severity}] {RuleId} — {Message}{where} [{Clause}]";
    }
}
