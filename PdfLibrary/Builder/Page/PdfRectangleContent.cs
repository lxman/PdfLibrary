namespace PdfLibrary.Builder.Page;

/// <summary>
/// Rectangle content element
/// </summary>
public class PdfRectangleContent : PdfContentElement
{
    public PdfRect Rect { get; set; }
    public PdfColor? FillColor { get; set; }
    public PdfColor? StrokeColor { get; set; }
    public double LineWidth { get; set; } = 1;
}