using System.Numerics;
using PdfLibrary.Content;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Small test-only probe over the renderer-neutral <see cref="PageDrawList"/> contract.
/// It deliberately handles only the simple, axis-aligned fixtures that use it; backend-specific
/// rasterization belongs in the supported renderer projects, not in the core test assembly.
/// </summary>
internal static class RecordedPageProbe
{
    public static PageDrawList Record(PdfPage page, double scale = 1.0) =>
        RecordingRenderTarget.Record(page, scale);

    public static PageDrawList Record(byte[] pdf, double scale = 1.0)
    {
        using var stream = new MemoryStream(pdf);
        using PdfDocument document = PdfDocument.Load(stream);
        return Record(document.GetPage(0)!, scale);
    }

    public static IReadOnlyList<DrawCommand> PaintCommands(PageDrawList list)
    {
        var result = new List<DrawCommand>();
        AddPaintCommands(list.Commands, result);
        return result;
    }

    public static int CountPaintCommandsInRect(PageDrawList list,
        double left, double bottom, double right, double top)
    {
        return PaintCommands(list).Count(command =>
            IsVisible(command) && BoundsOf(command) is { } bounds &&
            bounds.Right >= left && bounds.Left <= right &&
            bounds.Top >= bottom && bounds.Bottom <= top);
    }

    public static int CountPaintCommandsInRect(byte[] pdf,
        double left, double bottom, double right, double top) =>
        CountPaintCommandsInRect(Record(pdf), left, bottom, right, top);

    public static int CountPaintCommandsInRect(PdfPage page,
        double left, double bottom, double right, double top) =>
        CountPaintCommandsInRect(Record(page), left, bottom, right, top);

    public static int InkRowBands(byte[] pdf, double left, double bottom, double right, double top)
    {
        List<(double Bottom, double Top)> ranges = PaintCommands(Record(pdf))
            .Where(IsVisible)
            .Select(BoundsOf)
            .Where(bounds => bounds is not null && bounds.Value.Right >= left && bounds.Value.Left <= right &&
                             bounds.Value.Top >= bottom && bounds.Value.Bottom <= top)
            .Select(bounds => (Math.Max(bottom, bounds!.Value.Bottom), Math.Min(top, bounds.Value.Top)))
            .OrderBy(range => range.Item1)
            .ToList();

        if (ranges.Count == 0) return 0;

        var bands = 1;
        double currentTop = ranges[0].Top;
        foreach ((double rangeBottom, double rangeTop) in ranges.Skip(1))
        {
            if (rangeBottom > currentTop + 1.0)
            {
                bands++;
                currentTop = rangeTop;
            }
            else
            {
                currentTop = Math.Max(currentTop, rangeTop);
            }
        }

        return bands;
    }

    public static RecordedColor ColorAt(byte[] pdf, double x, double y) => ColorAt(Record(pdf), x, y);

    public static RecordedColor ColorAt(PageDrawList list, double x, double y)
    {
        var color = new RecordedColor(255, 255, 255, 255);
        foreach (DrawCommand command in PaintCommands(list))
        {
            switch (command)
            {
                case FillCommand fill when Contains(fill.Segments, x, y):
                    color = Composite(color, StateColor(fill.State), fill.State.FillAlpha);
                    break;
                case FillStrokeCommand fillStroke when Contains(fillStroke.Segments, x, y):
                    color = Composite(color, StateColor(fillStroke.State), fillStroke.State.FillAlpha);
                    break;
                case TilingFillCommand tiling when Contains(tiling.Segments, x, y):
                    color = Composite(color, StateColor(tiling.State), tiling.State.FillAlpha);
                    break;
                case ShadingPatternFillCommand shadingPattern
                    when Contains(shadingPattern.Segments, x, y) && shadingPattern.Shading.Colors.Length > 0:
                    color = Composite(color, PackedColor(shadingPattern.Shading.Colors[0]),
                        shadingPattern.State.FillAlpha);
                    break;
                case ImageCommand image when TrySample(image, x, y, out RecordedColor sampled):
                    color = Composite(color, sampled, image.State.FillAlpha);
                    break;
            }
        }

        return color;
    }

    private static void AddPaintCommands(IEnumerable<DrawCommand> commands, ICollection<DrawCommand> result)
    {
        foreach (DrawCommand command in commands)
        {
            if (command is GroupCommand group)
                AddPaintCommands(group.Content.Commands, result);
            else if (command is FillCommand or StrokeCommand or FillStrokeCommand or TilingFillCommand or
                     ImageCommand or ShadingCommand or ShadingPatternFillCommand)
                result.Add(command);
        }
    }

    private static bool IsVisible(DrawCommand command) => command switch
    {
        FillCommand fill => fill.State.FillAlpha > 0,
        StrokeCommand stroke => stroke.State.StrokeAlpha > 0,
        FillStrokeCommand fillStroke => fillStroke.State.FillAlpha > 0 || fillStroke.State.StrokeAlpha > 0,
        TilingFillCommand tiling => tiling.State.FillAlpha > 0,
        ImageCommand image => HasNonZeroAlpha(image.Rgba),
        ShadingCommand shading => shading.State.FillAlpha > 0,
        ShadingPatternFillCommand shadingPattern => shadingPattern.State.FillAlpha > 0,
        _ => false,
    };

    private static Bounds? BoundsOf(DrawCommand command) => command switch
    {
        FillCommand fill => BoundsOf(fill.Segments),
        StrokeCommand stroke => BoundsOf(stroke.Segments),
        FillStrokeCommand fillStroke => BoundsOf(fillStroke.Segments),
        TilingFillCommand tiling => BoundsOf(tiling.Segments),
        ShadingPatternFillCommand shadingPattern => BoundsOf(shadingPattern.Segments),
        ImageCommand image => BoundsOf(image.Ctm),
        _ => null,
    };

    private static Bounds? BoundsOf(IReadOnlyList<PathSegment> segments)
    {
        var points = new List<(double X, double Y)>();
        foreach (PathSegment segment in segments)
        {
            switch (segment)
            {
                case MoveToSegment move:
                    points.Add((move.X, move.Y));
                    break;
                case LineToSegment line:
                    points.Add((line.X, line.Y));
                    break;
                case CurveToSegment curve:
                    points.Add((curve.X1, curve.Y1));
                    points.Add((curve.X2, curve.Y2));
                    points.Add((curve.X3, curve.Y3));
                    break;
            }
        }

        return points.Count == 0
            ? null
            : new Bounds(points.Min(p => p.X), points.Min(p => p.Y),
                points.Max(p => p.X), points.Max(p => p.Y));
    }

    private static Bounds BoundsOf(Matrix3x2 matrix)
    {
        Vector2[] corners =
        [
            Vector2.Transform(Vector2.Zero, matrix),
            Vector2.Transform(Vector2.UnitX, matrix),
            Vector2.Transform(Vector2.UnitY, matrix),
            Vector2.Transform(Vector2.One, matrix),
        ];
        return new Bounds(corners.Min(p => p.X), corners.Min(p => p.Y),
            corners.Max(p => p.X), corners.Max(p => p.Y));
    }

    private static bool Contains(IReadOnlyList<PathSegment> segments, double x, double y) =>
        BoundsOf(segments) is { } bounds && x >= bounds.Left && x <= bounds.Right &&
        y >= bounds.Bottom && y <= bounds.Top;

    private static RecordedColor StateColor(PdfGraphicsState state)
    {
        (byte r, byte g, byte b) = PdfColorToRgb.ToRgb(state.ResolvedFillColor, state.ResolvedFillColorSpace);
        return new RecordedColor(r, g, b, 255);
    }

    private static bool HasNonZeroAlpha(byte[] rgba)
    {
        for (var i = 3; i < rgba.Length; i += 4)
            if (rgba[i] > 0)
                return true;
        return false;
    }

    private static RecordedColor PackedColor(uint argb) => new(
        (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));

    private static bool TrySample(ImageCommand image, double x, double y, out RecordedColor color)
    {
        color = default;
        if (!Matrix3x2.Invert(image.Ctm, out Matrix3x2 inverse)) return false;

        Vector2 source = Vector2.Transform(new Vector2((float)x, (float)y), inverse);
        if (source.X < 0 || source.X > 1 || source.Y < 0 || source.Y > 1) return false;

        int px = Math.Clamp((int)(source.X * image.Width), 0, image.Width - 1);
        int py = Math.Clamp((int)((1 - source.Y) * image.Height), 0, image.Height - 1);
        int offset = (py * image.Width + px) * 4;
        color = new RecordedColor(image.Rgba[offset], image.Rgba[offset + 1],
            image.Rgba[offset + 2], image.Rgba[offset + 3]);
        return true;
    }

    private static RecordedColor Composite(RecordedColor backdrop, RecordedColor source, double constantAlpha)
    {
        double alpha = source.Alpha / 255.0 * Math.Clamp(constantAlpha, 0, 1);
        byte Blend(byte foreground, byte background) =>
            (byte)Math.Round(foreground * alpha + background * (1 - alpha));
        return new RecordedColor(Blend(source.Red, backdrop.Red), Blend(source.Green, backdrop.Green),
            Blend(source.Blue, backdrop.Blue), 255);
    }

    private readonly record struct Bounds(double Left, double Bottom, double Right, double Top);
}

internal readonly record struct RecordedColor(byte Red, byte Green, byte Blue, byte Alpha);
