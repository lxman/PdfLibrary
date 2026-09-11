namespace PdfLibrary.Integration;

/// <summary>
/// Compares deterministic SVG render output.
/// </summary>
public static class SvgComparer
{
    /// <summary>
    /// Compares two SVG files and returns comparison results.
    /// </summary>
    /// <param name="goldenImagePath">Path to the golden SVG</param>
    /// <param name="actualImagePath">Path to the actual SVG</param>
    /// <returns>Comparison result with match statistics</returns>
    public static ComparisonResult Compare(string goldenImagePath, string actualImagePath)
    {
        if (!File.Exists(goldenImagePath))
            return new ComparisonResult(false, 0, 0, $"Golden image not found: {goldenImagePath}");

        if (!File.Exists(actualImagePath))
            return new ComparisonResult(false, 0, 0, $"Actual image not found: {actualImagePath}");

        string golden = File.ReadAllText(goldenImagePath).Replace("\r\n", "\n", StringComparison.Ordinal);
        string actual = File.ReadAllText(actualImagePath).Replace("\r\n", "\n", StringComparison.Ordinal);
        bool matches = string.Equals(golden, actual, StringComparison.Ordinal);
        return new ComparisonResult(true, matches ? 100.0 : 0.0, matches ? 0 : 1,
            matches ? null : "SVG output differs from the golden baseline");
    }
}

/// <summary>
/// Result of an image comparison
/// </summary>
/// <param name="Success">Whether the comparison completed successfully</param>
/// <param name="MatchPercentage">Percentage of pixels that match (0-100)</param>
/// <param name="DifferenceCount">Zero when the SVG documents match, otherwise one</param>
/// <param name="Message">Error message if comparison failed</param>
public record ComparisonResult(
    bool Success,
    double MatchPercentage,
    int DifferenceCount,
    string? Message
);
