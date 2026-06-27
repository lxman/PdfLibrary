namespace PdfLibrary.Builder.Page;

/// <summary>
/// Fluent builder for image configuration
/// </summary>
public class PdfImageBuilder(PdfPageBuilder pageBuilder, PdfImageContent content)
{
    /// <summary>
    /// Set image opacity (0.0 = transparent, 1.0 = opaque)
    /// </summary>
    public PdfImageBuilder Opacity(double opacity)
    {
        content.Opacity = Math.Clamp(opacity, 0, 1);
        return this;
    }

    /// <summary>
    /// Rotate the image by the specified degrees
    /// </summary>
    public PdfImageBuilder Rotate(double degrees)
    {
        content.Rotation = degrees;
        return this;
    }

    /// <summary>
    /// Preserve the image's aspect ratio when scaling
    /// </summary>
    public PdfImageBuilder PreserveAspectRatio(bool preserve = true)
    {
        content.PreserveAspectRatio = preserve;
        return this;
    }

    /// <summary>
    /// Stretch image to fill the entire rect (don't preserve aspect ratio)
    /// </summary>
    public PdfImageBuilder Stretch()
    {
        content.PreserveAspectRatio = false;
        return this;
    }

    /// <summary>
    /// Set the compression method for the image
    /// </summary>
    public PdfImageBuilder Compression(PdfImageCompression compression, int jpegQuality = 85)
    {
        content.Compression = compression;
        content.JpegQuality = Math.Clamp(jpegQuality, 1, 100);
        return this;
    }

    /// <summary>
    /// Enable or disable image interpolation (smoothing when scaled)
    /// </summary>
    public PdfImageBuilder Interpolate(bool interpolate = true)
    {
        content.Interpolate = interpolate;
        return this;
    }

    /// <summary>
    /// Disable interpolation for crisp pixel-perfect rendering
    /// </summary>
    public PdfImageBuilder NearestNeighbor()
    {
        content.Interpolate = false;
        return this;
    }

    /// <summary>
    /// Return to the page builder to continue adding content.
    /// </summary>
    public PdfPageBuilder Done() => pageBuilder;

    /// <summary>
    /// Implicit conversion back to the page builder.
    /// </summary>
    public static implicit operator PdfPageBuilder(PdfImageBuilder builder) => builder.Done();
}