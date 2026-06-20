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
}
