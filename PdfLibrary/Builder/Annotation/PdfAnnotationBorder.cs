namespace PdfLibrary.Builder.Annotation;

/// <summary>
/// Border style for annotations
/// </summary>
public class PdfAnnotationBorder
{
    /// <summary>
    /// Horizontal corner radius
    /// </summary>
    public double HorizontalRadius { get; set; }

    /// <summary>
    /// Vertical corner radius
    /// </summary>
    public double VerticalRadius { get; set; }

    /// <summary>
    /// Border width (0 = invisible)
    /// </summary>
    public double Width { get; set; } = 1;

    /// <summary>
    /// Dash pattern (null = solid)
    /// </summary>
    public double[]? DashPattern { get; set; }

    /// <summary>
    /// No visible border
    /// </summary>
    public static PdfAnnotationBorder None => new() { Width = 0 };

    /// <summary>
    /// Thin border (0.5 pt)
    /// </summary>
    public static PdfAnnotationBorder Thin => new() { Width = 0.5 };

    /// <summary>
    /// Standard border (1 pt)
    /// </summary>
    public static PdfAnnotationBorder Standard => new() { Width = 1 };
}