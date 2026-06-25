namespace PdfLibrary.Builder.Page;

/// <summary>
/// A text-drawing content element (the data behind <see cref="PdfTextBuilder"/>).
/// Coordinates are in PDF points with a bottom-left origin.
/// </summary>
public class PdfTextContent : PdfContentElement
{
    /// <summary>The text to draw.</summary>
    public string Text { get; set; } = "";
    /// <summary>X position in points.</summary>
    public double X { get; set; }
    /// <summary>Y position (text baseline) in points.</summary>
    public double Y { get; set; }
    /// <summary>Font name — a standard-14 name or a custom font alias registered via LoadFont.</summary>
    public string FontName { get; set; } = "Helvetica";
    /// <summary>Font size in points.</summary>
    public double FontSize { get; set; } = 12;
    /// <summary>Glyph fill colour.</summary>
    public PdfColor FillColor { get; set; } = PdfColor.Black;
    /// <summary>Glyph outline (stroke) colour, or null for no stroke.</summary>
    public PdfColor? StrokeColor { get; set; }
    /// <summary>Rotation in degrees, counter-clockwise about the start point.</summary>
    public double Rotation { get; set; }
    /// <summary>Extra spacing added between characters, in points (PDF Tc).</summary>
    public double CharacterSpacing { get; set; }
    /// <summary>Extra spacing added at space characters, in points (PDF Tw).</summary>
    public double WordSpacing { get; set; }
    /// <summary>Horizontal scaling as a percentage (100 = normal, PDF Tz).</summary>
    public double HorizontalScale { get; set; } = 100;
    /// <summary>Vertical baseline shift in points (PDF Ts); positive raises the text.</summary>
    public double TextRise { get; set; }
    /// <summary>Line spacing in points, used when the text wraps across <see cref="MaxWidth"/>.</summary>
    public double LineSpacing { get; set; }
    /// <summary>How the glyphs are painted (fill, stroke, both, invisible, …).</summary>
    public PdfTextRenderMode RenderMode { get; set; } = PdfTextRenderMode.Fill;
    /// <summary>Stroke width in points when <see cref="RenderMode"/> strokes.</summary>
    public double StrokeWidth { get; set; } = 1;
    /// <summary>Horizontal alignment within <see cref="MaxWidth"/>.</summary>
    public PdfTextAlignment Alignment { get; set; } = PdfTextAlignment.Left;
    /// <summary>Maximum line width in points before wrapping, or null for no wrapping.</summary>
    public double? MaxWidth { get; set; }
}
