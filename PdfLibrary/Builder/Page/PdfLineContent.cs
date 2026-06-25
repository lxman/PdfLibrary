namespace PdfLibrary.Builder.Page;

/// <summary>
/// A line-drawing content element. Coordinates are in PDF points.
/// </summary>
public class PdfLineContent : PdfContentElement
{
    /// <summary>Start X in points.</summary>
    public double X1 { get; set; }
    /// <summary>Start Y in points.</summary>
    public double Y1 { get; set; }
    /// <summary>End X in points.</summary>
    public double X2 { get; set; }
    /// <summary>End Y in points.</summary>
    public double Y2 { get; set; }
    /// <summary>Stroke colour.</summary>
    public PdfColor StrokeColor { get; set; } = PdfColor.Black;
    /// <summary>Stroke width in points.</summary>
    public double LineWidth { get; set; } = 1;
}
