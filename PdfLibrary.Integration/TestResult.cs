namespace PdfLibrary.Integration;

/// <summary>
/// Result of a single test document comparison
/// </summary>
public record TestResult(
    string Name,
    string Description,
    bool Passed,
    double MatchPercentage,
    string? ErrorMessage
);
