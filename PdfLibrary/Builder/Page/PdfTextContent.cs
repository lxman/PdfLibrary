namespace PdfLibrary.Builder.Page;

/// <summary>
/// Text content element
/// </summary>
public class PdfTextContent : PdfContentElement
{
    public string Text { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public string FontName { get; set; } = "Helvetica";
    public double FontSize { get; set; } = 12;
    public PdfColor FillColor { get; set; } = PdfColor.Black;
    public PdfColor? StrokeColor { get; set; }
    public double Rotation { get; set; }
    public double CharacterSpacing { get; set; }
    public double WordSpacing { get; set; }
    public double HorizontalScale { get; set; } = 100;
    public double TextRise { get; set; }
    public double LineSpacing { get; set; }
    public PdfTextRenderMode RenderMode { get; set; } = PdfTextRenderMode.Fill;
    public double StrokeWidth { get; set; } = 1;
    public PdfTextAlignment Alignment { get; set; } = PdfTextAlignment.Left;
    public double? MaxWidth { get; set; }
}