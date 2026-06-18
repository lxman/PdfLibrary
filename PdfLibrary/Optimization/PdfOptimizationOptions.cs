namespace PdfLibrary.Optimization;

/// <summary>Controls which optimization passes run.</summary>
public sealed class PdfOptimizationOptions
{
    /// <summary>Flate-compress streams stored uncompressed.</summary>
    public bool CompressStreams { get; set; } = true;

    /// <summary>Drop objects unreachable from the catalog/info (garbage collection).</summary>
    public bool RemoveUnusedObjects { get; set; } = true;

    public static PdfOptimizationOptions Default => new();
}
