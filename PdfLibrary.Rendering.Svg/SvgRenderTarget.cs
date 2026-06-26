using System.Globalization;
using System.Numerics;
using System.Text;
using PdfLibrary.Content;
using PdfLibrary.Document;
using PdfLibrary.Rendering;

namespace PdfLibrary.Rendering.Svg;

/// <summary>
/// A SkiaSharp-free <see cref="IRenderTarget"/> that renders a PDF page to SVG. Demonstrates the
/// geometry-only render SPI: paths arrive in CTM-baked PDF user space and are emitted verbatim as
/// SVG path data; the page initial-transform (Y-flip + scale + crop + rotation) is the root group.
/// </summary>
public sealed class SvgRenderTarget : IRenderTarget
{
    private readonly StringBuilder _sb = new();
    private int _clipDepth;                 // open <g clip-path> groups
    private int _clipId;                    // monotonic counter for clipPath id="cN"
    private readonly Stack<int> _saveStack = new();
    private Matrix3x2 _currentCtm = Matrix3x2.Identity;
    private (int w, int h, double scale) _dims;

    public int CurrentPageNumber { get; private set; }

    public void BeginPage(int pageNumber, double width, double height, double scale = 1.0,
        double cropOffsetX = 0, double cropOffsetY = 0, int rotation = 0)
    {
        CurrentPageNumber = pageNumber;
        _clipDepth = 0; _clipId = 0; _saveStack.Clear(); _currentCtm = Matrix3x2.Identity;

        double pw = (rotation is 90 or 270 ? height : width) * scale;
        double ph = (rotation is 90 or 270 ? width : height) * scale;
        _dims = ((int)Math.Ceiling(pw), (int)Math.Ceiling(ph), scale);

        Matrix3x2 init = InitialTransform(width, height, scale, cropOffsetX, cropOffsetY, rotation);

        _sb.Append(F($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{pw}\" height=\"{ph}\" "))
           .Append(F($"viewBox=\"0 0 {pw} {ph}\">"))
           .Append(F($"<g transform=\"matrix({init.M11},{init.M12},{init.M21},{init.M22},{init.M31},{init.M32})\">"));
    }

    public void EndPage()
    {
        while (_clipDepth-- > 0) _sb.Append("</g>");   // close any dangling clip groups
        _sb.Append("</g></svg>");
    }

    public void Clear() { _sb.Clear(); _clipDepth = 0; _saveStack.Clear(); CurrentPageNumber = 0; }

    public string GetSvg() => _sb.ToString();

    public void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        if (path.IsEmpty) return;
        (byte r, byte g, byte b) = PdfColorToRgb.ToRgb(state.ResolvedFillColor, state.ResolvedFillColorSpace);
        _sb.Append(F($"<path d=\"{D(path)}\" fill=\"rgb({r},{g},{b})\" fill-opacity=\"{state.FillAlpha}\" "))
           .Append(evenOdd ? "fill-rule=\"evenodd\"/>" : "fill-rule=\"nonzero\"/>");
    }

    public void StrokePath(IPathBuilder path, PdfGraphicsState state)
    {
        if (path.IsEmpty) return;
        (byte r, byte g, byte b) = PdfColorToRgb.ToRgb(state.ResolvedStrokeColor, state.ResolvedStrokeColorSpace);
        _sb.Append(F($"<path d=\"{D(path)}\" fill=\"none\" stroke=\"rgb({r},{g},{b})\" "))
           .Append(F($"stroke-opacity=\"{state.StrokeAlpha}\" stroke-width=\"{Math.Max(state.LineWidth, 0.1)}\" "))
           .Append(F($"stroke-linecap=\"{Cap(state.LineCap)}\" stroke-linejoin=\"{Join(state.LineJoin)}\"/>"));
    }

    public void FillAndStrokePath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        FillPath(path, state, evenOdd);
        StrokePath(path, state);
    }

    public void ApplyCtm(Matrix3x2 ctm) => _currentCtm = ctm;
    public void OnGraphicsStateChanged(PdfGraphicsState state) { }
    public (int width, int height, double scale) GetPageDimensions() => _dims;

    // ---- implemented in Task 4 ----
    public void SetClippingPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        if (path.IsEmpty) return;
        int id = ++_clipId;
        string rule = evenOdd ? " clip-rule=\"evenodd\"" : "";
        _sb.Append(F($"<clipPath id=\"c{id}\"><path d=\"{D(path)}\"{rule}/></clipPath>"))
           .Append(F($"<g clip-path=\"url(#c{id})\">"));
        _clipDepth++;
    }

    public void DrawImage(PdfImage image, PdfGraphicsState state)
    {
        // The image is drawn in a 1×1 unit square; flip Y within it (PDF image rows are top-first),
        // then place by state.Ctm. The root <g> applies the page initial transform on top.
        Matrix3x2 m = state.Ctm;
        string xform = F($"matrix({m.M11},{m.M12},{m.M21},{m.M22},{m.M31},{m.M32}) translate(0,1) scale(1,-1)");

        byte[] encoded = image.GetEncodedData();
        bool isJpeg = encoded.Length > 3 && encoded[0] == 0xFF && encoded[1] == 0xD8 && encoded[2] == 0xFF;
        if (isJpeg)
        {
            string b64 = Convert.ToBase64String(encoded);
            _sb.Append(F($"<image transform=\"{xform}\" width=\"1\" height=\"1\" preserveAspectRatio=\"none\" "))
               .Append(F($"href=\"data:image/jpeg;base64,{b64}\"/>"));
        }
        else
        {
            // Non-JPEG images need the PdfImage→RGBA + PNG path (deferred). Placeholder for now.
            _sb.Append("<!-- non-JPEG image omitted (RGBA path deferred) -->")
               .Append(F($"<rect transform=\"{xform}\" width=\"1\" height=\"1\" fill=\"#cccccc\" fill-opacity=\"0.3\"/>"));
        }
    }

    public void FillPathWithTilingPattern(IPathBuilder path, PdfGraphicsState state, bool evenOdd,
        PdfTilingPattern pattern, Action<IRenderTarget> renderPatternContent)
    {
        FillPath(path, state, evenOdd);
        _sb.Append("<!-- tiling pattern approximated as solid fill -->");
    }

    public void SaveState() => _saveStack.Push(_clipDepth);
    public void RestoreState()
    {
        if (_saveStack.Count > 0)
        {
            int target = _saveStack.Pop();
            while (_clipDepth > target) { _sb.Append("</g>"); _clipDepth--; }
        }
    }

    public void RenderSoftMask(string maskSubtype, Action<IRenderTarget> renderMaskContent)
        => _sb.Append(F($"<!-- soft mask '{maskSubtype}' not applied (deferred) -->"));

    public void ClearSoftMask() { }

    // ---- helpers ----
    private static string F(FormattableString s) => FormattableString.Invariant(s);

    private static string D(IPathBuilder path)
    {
        var sb = new StringBuilder();
        foreach (PathSegment s in path.Segments)
            sb.Append(s switch
            {
                MoveToSegment m => F($"M{m.X} {m.Y} "),
                LineToSegment l => F($"L{l.X} {l.Y} "),
                CurveToSegment c => F($"C{c.X1} {c.Y1} {c.X2} {c.Y2} {c.X3} {c.Y3} "),
                ClosePathSegment => "Z ",
                _ => ""
            });
        return sb.ToString().TrimEnd();
    }

    private static Matrix3x2 InitialTransform(double width, double height, double scale,
        double cropX, double cropY, int rotation)
    {
        double finalHeight = rotation is 90 or 270 ? width : height;
        (float tx, float ty) = rotation switch
        {
            90  => (0f, (float)width),
            180 => ((float)width, (float)height),
            270 => ((float)height, 0f),
            _   => (0f, 0f)
        };
        double rad = -rotation * Math.PI / 180.0;
        return Matrix3x2.CreateTranslation((float)-cropX, (float)-cropY)
             * Matrix3x2.CreateRotation((float)rad)
             * Matrix3x2.CreateTranslation(tx, ty)
             * Matrix3x2.CreateScale((float)scale, (float)-scale)
             * Matrix3x2.CreateTranslation(0, (float)(finalHeight * scale));
    }

    private static string Cap(int c) => c switch { 1 => "round", 2 => "square", _ => "butt" };
    private static string Join(int j) => j switch { 1 => "round", 2 => "bevel", _ => "miter" };
}
