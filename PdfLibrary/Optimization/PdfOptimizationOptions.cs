namespace PdfLibrary.Optimization;

/// <summary>Controls which optimization passes run. Declared partial so each Phase 3 track can add
/// its tuning options in a separate file (PdfOptimizationOptions.Image.cs / .Font.cs) without both
/// branches editing this file.</summary>
public sealed partial class PdfOptimizationOptions
{
    /// <summary>Flate-compress streams stored uncompressed.</summary>
    public bool CompressStreams { get; set; } = true;

    /// <summary>Drop objects unreachable from the catalog/info (garbage collection).</summary>
    public bool RemoveUnusedObjects { get; set; } = true;

    /// <summary>Pack objects into object streams + a cross-reference stream (PDF 1.5+). Much smaller
    /// output on object-heavy documents.</summary>
    public bool UseObjectStreams { get; set; } = true;

    /// <summary>Downsample and re-compress embedded image XObjects (Phase 3, image track). Off until
    /// the image integration lands and is validated.</summary>
    public bool RecompressImages { get; set; } = false;

    /// <summary>Subset embedded TrueType (/FontFile2) programs to the glyphs actually used (Phase 3,
    /// font track). Off until the font integration lands and is validated.</summary>
    public bool SubsetFonts { get; set; } = false;

    public static PdfOptimizationOptions Default => new();
}
