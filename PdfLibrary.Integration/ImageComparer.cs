using Codeuctivity.ImageSharpCompare;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PdfLibrary.Integration;

/// <summary>
/// Compares images and generates diff visualizations
/// </summary>
public static class ImageComparer
{
    /// <summary>
    /// Compares two images and returns comparison results
    /// </summary>
    /// <param name="goldenImagePath">Path to the golden (expected) image</param>
    /// <param name="actualImagePath">Path to the actual (test) image</param>
    /// <param name="diffOutputPath">Optional path to save a diff image</param>
    /// <returns>Comparison result with match statistics</returns>
    public static ComparisonResult Compare(string goldenImagePath, string actualImagePath, string? diffOutputPath = null)
    {
        if (!File.Exists(goldenImagePath))
            return new ComparisonResult(false, 0, 0, $"Golden image not found: {goldenImagePath}");

        if (!File.Exists(actualImagePath))
            return new ComparisonResult(false, 0, 0, $"Actual image not found: {actualImagePath}");

        using Image<Rgba32> goldenImage = Image.Load<Rgba32>(goldenImagePath);
        using Image<Rgba32> actualImage = Image.Load<Rgba32>(actualImagePath);

        // Calculate similarity
        ICompareResult diff = ImageSharpCompare.CalcDiff(goldenImage, actualImage);
        // PixelErrorPercentage is already a percentage (0-100), not a ratio (0-1)
        double matchPercentage = 100.0 - diff.PixelErrorPercentage;

        // Generate diff image if requested
        if (diffOutputPath is null)
            return new ComparisonResult(
                Success: true,
                MatchPercentage: matchPercentage,
                PixelErrorCount: diff.PixelErrorCount,
                Message: null
            );
        using Image diffImage = ImageSharpCompare.CalcDiffMaskImage(goldenImage, actualImage);
        diffImage.SaveAsPng(diffOutputPath);

        return new ComparisonResult(
            Success: true,
            MatchPercentage: matchPercentage,
            PixelErrorCount: diff.PixelErrorCount,
            Message: null
        );
    }
}

/// <summary>
/// Result of an image comparison
/// </summary>
/// <param name="Success">Whether the comparison completed successfully</param>
/// <param name="MatchPercentage">Percentage of pixels that match (0-100)</param>
/// <param name="PixelErrorCount">Number of pixels that differ</param>
/// <param name="Message">Error message if comparison failed</param>
public record ComparisonResult(
    bool Success,
    double MatchPercentage,
    int PixelErrorCount,
    string? Message
);
