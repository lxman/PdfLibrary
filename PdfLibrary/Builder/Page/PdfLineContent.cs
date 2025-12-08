namespace PdfLibrary.Builder.Page;

/// <summary>
/// Line content element
/// </summary>
public class PdfLineContent : PdfContentElement
{
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public PdfColor StrokeColor { get; set; } = PdfColor.Black;
    public double LineWidth { get; set; } = 1;
}