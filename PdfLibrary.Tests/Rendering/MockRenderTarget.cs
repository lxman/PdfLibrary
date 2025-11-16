using PdfLibrary.Content;
using PdfLibrary.Document;
using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Mock rendering target for testing that records all rendering operations
/// </summary>
public class MockRenderTarget : IRenderTarget
{
    public List<string> Operations { get; } = [];
    private int _stateDepth = 0;

    public void StrokePath(IPathBuilder path, PdfGraphicsState state)
    {
        Operations.Add($"StrokePath: {GetPathDescription(path)}, LineWidth={state.LineWidth}, Color={GetColorDescription(state.StrokeColor, state.StrokeColorSpace)}");
    }

    public void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        Operations.Add($"FillPath: {GetPathDescription(path)}, EvenOdd={evenOdd}, Color={GetColorDescription(state.FillColor, state.FillColorSpace)}");
    }

    public void FillAndStrokePath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        Operations.Add($"FillAndStrokePath: {GetPathDescription(path)}, EvenOdd={evenOdd}");
    }

    public void DrawText(string text, List<double> glyphWidths, PdfGraphicsState state)
    {
        Operations.Add($"DrawText: \"{text}\", Font={state.FontName}, Size={state.FontSize}, Color={GetColorDescription(state.FillColor, state.FillColorSpace)}");
    }

    public void DrawImage(PdfImage image, PdfGraphicsState state)
    {
        Operations.Add($"DrawImage: {image.Width}x{image.Height}, {image.ColorSpace}");
    }

    public void SaveState()
    {
        _stateDepth++;
        Operations.Add($"SaveState (depth={_stateDepth})");
    }

    public void RestoreState()
    {
        Operations.Add($"RestoreState (depth={_stateDepth})");
        _stateDepth--;
    }

    public void SetClippingPath(IPathBuilder path, bool evenOdd)
    {
        Operations.Add($"SetClippingPath: {GetPathDescription(path)}, EvenOdd={evenOdd}");
    }

    private string GetPathDescription(IPathBuilder path)
    {
        if (path is PathBuilder builder)
        {
            if (builder.Segments.Count == 0)
                return "empty";

            var segmentTypes = builder.Segments
                .Select(s => s switch
                {
                    MoveToSegment => "M",
                    LineToSegment => "L",
                    CurveToSegment => "C",
                    ClosePathSegment => "Z",
                    _ => "?"
                })
                .ToList();

            return $"{builder.Segments.Count} segments [{string.Join("", segmentTypes)}]";
        }

        return path.IsEmpty ? "empty" : "custom path";
    }

    private string GetColorDescription(List<double> color, string colorSpace)
    {
        if (color.Count == 0)
            return "none";

        var values = string.Join(", ", color.Select(c => $"{c:F2}"));
        return $"{colorSpace}({values})";
    }

    public void Clear()
    {
        Operations.Clear();
        _stateDepth = 0;
    }

    public string GetOperationsSummary()
    {
        return string.Join("\n", Operations.Select((op, i) => $"{i + 1}. {op}"));
    }
}
