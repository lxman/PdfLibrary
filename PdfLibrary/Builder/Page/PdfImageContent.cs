namespace PdfLibrary.Builder.Page;

/// <summary>
/// An image-drawing content element (the data behind <see cref="PdfImageBuilder"/>).
/// </summary>
public class PdfImageContent : PdfContentElement
{
    /// <summary>The encoded image bytes (PNG, JPEG, …).</summary>
    public byte[] ImageData { get; set; } = [];
    /// <summary>Placement rectangle in points.</summary>
    public PdfRect Rect { get; set; }
    /// <summary>Opacity (0 = transparent, 1 = opaque).</summary>
    public double Opacity { get; set; } = 1.0;
    /// <summary>Rotation in degrees about the rectangle centre.</summary>
    public double Rotation { get; set; }
    /// <summary>If true, fit the image within <see cref="Rect"/> preserving its aspect ratio.</summary>
    public bool PreserveAspectRatio { get; set; } = true;
    /// <summary>How the image is (re)compressed when written.</summary>
    public PdfImageCompression Compression { get; set; } = PdfImageCompression.Auto;
    /// <summary>JPEG quality (1–100) when <see cref="Compression"/> uses JPEG.</summary>
    public int JpegQuality { get; set; } = 85;
    /// <summary>If true, smooth the image when scaled (PDF /Interpolate).</summary>
    public bool Interpolate { get; set; } = true;
}
