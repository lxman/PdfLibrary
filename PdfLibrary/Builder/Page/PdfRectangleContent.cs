namespace PdfLibrary.Builder.Page;

/// <summary>
/// A rectangle-drawing content element.
/// </summary>
public class PdfRectangleContent : PdfContentElement
{
    /// <summary>The rectangle in points.</summary>
    public PdfRect Rect { get; set; }
    /// <summary>Fill colour, or null to leave it unfilled.</summary>
    public PdfColor? FillColor { get; set; }
    /// <summary>Stroke colour, or null to leave it unstroked.</summary>
    public PdfColor? StrokeColor { get; set; }
    /// <summary>Stroke width in points.</summary>
    public double LineWidth { get; set; } = 1;
}
