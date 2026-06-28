using PdfLibrary.Builder.Page;

namespace PdfLibrary.Builder.Annotation;

/// <summary>Square (rectangle) markup annotation. Stroke <see cref="Color"/>, optional <see cref="InteriorColor"/> fill.</summary>
public class PdfSquareAnnotation : PdfAnnotation
{
    public override string Subtype => "Square";
    public PdfColor Color { get; internal set; } = PdfColor.Black;
    public PdfColor? InteriorColor { get; internal set; }
    public double LineWidth { get; internal set; } = 1.0;
    internal PdfSquareAnnotation(PdfRect rect) : base(rect) { }
}

/// <summary>Circle (ellipse) markup annotation inscribed in its rectangle.</summary>
public class PdfCircleAnnotation : PdfAnnotation
{
    public override string Subtype => "Circle";
    public PdfColor Color { get; internal set; } = PdfColor.Black;
    public PdfColor? InteriorColor { get; internal set; }
    public double LineWidth { get; internal set; } = 1.0;
    internal PdfCircleAnnotation(PdfRect rect) : base(rect) { }
}

/// <summary>Line markup annotation between two points (PDF user space).</summary>
public class PdfLineAnnotation : PdfAnnotation
{
    public override string Subtype => "Line";
    public double X1 { get; internal set; }
    public double Y1 { get; internal set; }
    public double X2 { get; internal set; }
    public double Y2 { get; internal set; }
    public PdfColor Color { get; internal set; } = PdfColor.Black;
    public double LineWidth { get; internal set; } = 1.0;
    internal PdfLineAnnotation(PdfRect rect) : base(rect) { }
}

/// <summary>Ink (freehand) markup annotation: one or more polyline paths in PDF user space.</summary>
public class PdfInkAnnotation : PdfAnnotation
{
    public override string Subtype => "Ink";
    public List<IReadOnlyList<(double X, double Y)>> Paths { get; } = [];
    public PdfColor Color { get; internal set; } = PdfColor.Black;
    public double LineWidth { get; internal set; } = 1.0;
    internal PdfInkAnnotation(PdfRect rect) : base(rect) { }
}

/// <summary>FreeText markup annotation: text drawn directly on the page within its rectangle.</summary>
public class PdfFreeTextAnnotation : PdfAnnotation
{
    public override string Subtype => "FreeText";
    public string Text { get; internal set; } = string.Empty;
    public double FontSize { get; internal set; } = 12.0;
    public PdfColor Color { get; internal set; } = PdfColor.Black;
    public int Quadding { get; internal set; }
    internal PdfFreeTextAnnotation(PdfRect rect) : base(rect) { }
}
