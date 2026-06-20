using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;

namespace PdfLibrary.Editing.Stamping;

/// <summary>Fluent authoring + placement of a page stamp. Configure inside <c>edit.Pages.Stamp(...)</c>.</summary>
public sealed class PdfStampBuilder
{
    internal double? Width { get; private set; }
    internal double? Height { get; private set; }
    internal Action<PdfPageBuilder>? Author { get; private set; }
    internal StampPlacement Placement { get; private set; } = StampPlacement.Identity();
    internal double ScaleValue { get; private set; } = 1.0;
    internal double RotateValue { get; private set; }
    internal double OpacityValue { get; private set; } = 1.0;
    internal bool IsUnderlay { get; private set; }

    public PdfStampBuilder Size(double width, double height) { Width = width; Height = height; return this; }
    public PdfStampBuilder Content(Action<PdfPageBuilder> author) { Author = author; return this; }

    public PdfStampBuilder At(double x, double y) { Placement = StampPlacement.At(x, y); return this; }
    public PdfStampBuilder Center() { Placement = StampPlacement.Center(); return this; }
    public PdfStampBuilder TopLeft() { Placement = StampPlacement.TopLeft(); return this; }
    public PdfStampBuilder TopRight() { Placement = StampPlacement.TopRight(); return this; }
    public PdfStampBuilder BottomLeft() { Placement = StampPlacement.BottomLeft(); return this; }
    public PdfStampBuilder BottomRight() { Placement = StampPlacement.BottomRight(); return this; }
    public PdfStampBuilder Diagonal() { Placement = StampPlacement.Diagonal(); return this; }
    public PdfStampBuilder Tiled(double spacing)
    {
        if (spacing <= 0) throw new ArgumentOutOfRangeException(nameof(spacing));
        Placement = StampPlacement.Tiled(spacing);
        return this;
    }

    public PdfStampBuilder Scale(double factor) { ScaleValue = factor; return this; }
    public PdfStampBuilder Rotate(double degrees) { RotateValue = degrees; return this; }
    public PdfStampBuilder Opacity(double alpha) { OpacityValue = Math.Clamp(alpha, 0.0, 1.0); return this; }
    public PdfStampBuilder Overlay() { IsUnderlay = false; return this; }
    public PdfStampBuilder Underlay() { IsUnderlay = true; return this; }

    /// <summary>Sugar: a bold text stamp sized to fit the text. Pair with a placement preset (e.g. Diagonal/Center).</summary>
    public PdfStampBuilder Watermark(string text)
    {
        const double fontSize = 48;
        double w = PdfPageBuilder.MeasureText(text, "Helvetica-Bold", fontSize);
        Width = Math.Max(w, 1);
        Height = fontSize * 1.3;
        Author = p => p.AddText(text, 0, fontSize * 0.25, "Helvetica-Bold", fontSize);
        return this;
    }

    /// <summary>Sugar: an image stamp filling a width×height box (placement positions/scales it).</summary>
    public PdfStampBuilder Image(byte[] data, double width, double height)
    {
        Width = width;
        Height = height;
        Author = p => p.AddImage(data, new PdfRect(0, 0, width, height));
        return this;
    }
}
