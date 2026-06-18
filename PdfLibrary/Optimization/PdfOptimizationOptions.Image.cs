namespace PdfLibrary.Optimization;

public sealed partial class PdfOptimizationOptions
{
    /// <summary>JPEG quality (1–100) used when re-encoding image XObjects.
    /// 75 is a good trade-off between visual quality and file size.</summary>
    public int ImageJpegQuality { get; set; } = 75;

    /// <summary>When > 0, any image whose larger dimension exceeds this cap is
    /// downsampled (box-filter) so that the larger side equals this value,
    /// preserving aspect ratio. Set to 0 (default) to skip downsampling.</summary>
    public int MaxImagePixelDimension { get; set; } = 0;
}
