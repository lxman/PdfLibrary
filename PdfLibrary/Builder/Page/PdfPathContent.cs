namespace PdfLibrary.Builder.Page;

/// <summary>
/// Path content element supporting complex shapes
/// </summary>
public class PdfPathContent : PdfContentElement
{
    public List<PdfPathSegment> Segments { get; } = [];
    public PdfColor? FillColor { get; set; }
    public PdfColor? StrokeColor { get; set; }
    public double LineWidth { get; set; } = 1;
    public PdfFillRule FillRule { get; set; } = PdfFillRule.NonZeroWinding;
    public PdfLineCap LineCap { get; set; } = PdfLineCap.Butt;
    public PdfLineJoin LineJoin { get; set; } = PdfLineJoin.Miter;
    public double MiterLimit { get; set; } = 10;
    public double[]? DashPattern { get; set; }
    public double DashPhase { get; set; }
    public bool IsClippingPath { get; set; }
    public double FillOpacity { get; set; } = 1.0;
    public double StrokeOpacity { get; set; } = 1.0;
}