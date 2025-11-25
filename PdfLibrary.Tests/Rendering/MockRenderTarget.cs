using PdfLibrary.Content;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Mock rendering target for testing that records all rendering operations
/// </summary>
public class MockRenderTarget : IRenderTarget
{
    public List<string> Operations { get; } = [];
    private int _stateDepth = 0;

    public int CurrentPageNumber { get; private set; }

    public void BeginPage(int pageNumber, double width, double height, double scale = 1.0)
    {
        CurrentPageNumber = pageNumber;
        Operations.Add($"BeginPage: Page {pageNumber}, Size={width}x{height}, Scale={scale:F2}");
    }

    public void EndPage()
    {
        Operations.Add($"EndPage: Page {CurrentPageNumber}");
    }

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

    public void DrawText(string text, List<double> glyphWidths, PdfGraphicsState state, PdfFont? font, List<int>? charCodes = null)
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

    public void SetClippingPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        Operations.Add($"SetClippingPath: {GetPathDescription(path)}, EvenOdd={evenOdd}");
    }

    public void ApplyCtm(System.Numerics.Matrix3x2 ctm)
    {
        Operations.Add($"ApplyCtm: CTM=[{ctm.M11},{ctm.M12},{ctm.M21},{ctm.M22},{ctm.M31},{ctm.M32}]");
    }

    private static string GetPathDescription(IPathBuilder path)
    {
        if (path is PathBuilder builder)
        {
            if (builder.Segments.Count == 0)
                return "empty";

            List<string> segmentTypes = builder.Segments
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

    private static string GetColorDescription(List<double> color, string colorSpace)
    {
        if (color.Count == 0)
            return "none";

        string values = string.Join(", ", color.Select(c => $"{c:F2}"));
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
