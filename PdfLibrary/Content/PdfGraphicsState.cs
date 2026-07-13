using System.Numerics;
using Logging;
using PdfLibrary.Document;

namespace PdfLibrary.Content;

/// <summary>
/// Represents the graphics state during content stream processing (ISO 32000-1:2008 section 8.4)
/// Tracks the Current Transformation Matrix (CTM), text state, and other graphics parameters
/// </summary>
public class PdfGraphicsState
{
    // The CTM is accumulated in DOUBLE precision. PDF/A allows real numbers up to ±3.4×10^38, which
    // exceeds float.MaxValue (3.40282e38); accumulating in float overflowed to ∞/NaN on legal
    // max-magnitude transforms (e.g. a +MAX/−MAX cm pair), corrupting the CTM and blanking the whole
    // page. Consumers still read a narrowed Matrix3x2 — page-content coordinates are normal-range.
    private double _ctm11 = 1, _ctm12, _ctm21, _ctm22 = 1, _ctm31, _ctm32;

    /// <summary>
    /// Current Transformation Matrix - transforms from user space to device space
    /// Represented as [a b c d e f] where:
    /// x' = a*x + c*y + e
    /// y' = b*x + d*y + f
    /// Accumulated in double precision (see field note) and exposed narrowed to float.
    /// </summary>
    public Matrix3x2 Ctm
    {
        get => new((float)_ctm11, (float)_ctm12, (float)_ctm21, (float)_ctm22, (float)_ctm31, (float)_ctm32);
        set
        {
            _ctm11 = value.M11; _ctm12 = value.M12; _ctm21 = value.M21;
            _ctm22 = value.M22; _ctm31 = value.M31; _ctm32 = value.M32;
        }
    }

    /// <summary>
    /// Initial transformation matrix set at BeginPage.
    /// Maps the PDF page coordinate space to the rendering surface (viewport transformation).
    /// Following Melville.Pdf architecture: separate from the CTM, which handles PDF content transformations.
    /// </summary>
    public Matrix3x2 InitialTransformMatrix { get; init; } = Matrix3x2.Identity;

    /// <summary>
    /// Text matrix - determines position and orientation of text
    /// </summary>
    public Matrix3x2 TextMatrix { get; set; } = Matrix3x2.Identity;

    /// <summary>
    /// Text line matrix - position of the start of the current line
    /// </summary>
    public Matrix3x2 TextLineMatrix { get; set; } = Matrix3x2.Identity;

    // Text state parameters
    /// <summary>Character spacing (Tc)</summary>
    public double CharacterSpacing { get; set; }

    /// <summary>Word spacing (Tw)</summary>
    public double WordSpacing { get; set; }

    /// <summary>Horizontal scaling (Tz) as a percentage</summary>
    public double HorizontalScaling { get; set; } = 100;

    /// <summary>Text leading (TL)</summary>
    public double Leading { get; set; }

    /// <summary>Current font name</summary>
    public string? FontName { get; set; }

    /// <summary>Current font size</summary>
    public double FontSize { get; set; }

    /// <summary>Text rendering mode (Tr): 0=Fill, 1=Stroke, 2=FillStroke, 3=Invisible, etc.</summary>
    public int RenderingMode { get; set; }

    /// <summary>Text rise (Ts)</summary>
    public double TextRise { get; set; }

    // Line graphics state
    /// <summary>Line width</summary>
    public double LineWidth { get; set; } = 1.0;

    /// <summary>Line cap style: 0=butt, 1=round, 2=square</summary>
    public int LineCap { get; set; }

    /// <summary>Line join style: 0=miter, 1=round, 2=bevel</summary>
    public int LineJoin { get; set; }

    /// <summary>Miter limit</summary>
    public double MiterLimit { get; set; } = 10.0;

    /// <summary>Flatness tolerance</summary>
    public double Flatness { get; set; } = 1.0;

    /// <summary>Dash pattern array - defines the pattern of dashes and gaps for stroked paths</summary>
    public double[]? DashPattern { get; set; }

    /// <summary>Dash phase - the distance into the dash pattern at which to start</summary>
    public double DashPhase { get; set; }

    // Color state
    /// <summary>Stroke color space name (default: DeviceGray) - may be a named color space like Cs9</summary>
    public string StrokeColorSpace { get; set; } = "DeviceGray";

    /// <summary>Fill color space name (default: DeviceGray) - may be a named color space like Cs9</summary>
    public string FillColorSpace { get; set; } = "DeviceGray";

    /// <summary>Stroke color components as specified in the content stream (default: black)</summary>
    public List<double> StrokeColor { get; set; } = [0.0];

    /// <summary>Fill color components as specified in the content stream (default: black)</summary>
    public List<double> FillColor { get; set; } = [0.0];

    // Resolved color state - used by renderers after color space resolution
    /// <summary>Resolved stroke color space (device color space)</summary>
    public string ResolvedStrokeColorSpace { get; set; } = "DeviceGray";

    /// <summary>Resolved fill color space (device color space)</summary>
    public string ResolvedFillColorSpace { get; set; } = "DeviceGray";

    /// <summary>Resolved stroke color components (in device color space)</summary>
    public List<double> ResolvedStrokeColor { get; set; } = [0.0];

    /// <summary>Resolved fill color components (in device color space)</summary>
    public List<double> ResolvedFillColor { get; set; } = [0.0];

    // Pattern state
    /// <summary>Fill pattern name (when using Pattern color space)</summary>
    public string? FillPatternName { get; set; }

    /// <summary>Stroke pattern name (when using Pattern color space)</summary>
    public string? StrokePatternName { get; set; }

    // ExtGState parameters (set via gs operator)
    /// <summary>Stroking alpha (CA) - 0.0 = fully transparent, 1.0 = fully opaque</summary>
    public double StrokeAlpha { get; set; } = 1.0;

    /// <summary>Non-stroking (fill) alpha (ca) - 0.0 = fully transparent, 1.0 = fully opaque</summary>
    public double FillAlpha { get; set; } = 1.0;

    /// <summary>Blend mode (BM) - default is Normal</summary>
    public string BlendMode { get; set; } = "Normal";

    /// <summary>Soft mask dictionary (SMask) - for transparency masking</summary>
    public PdfSoftMask? SoftMask { get; set; }

    /// <summary>Alpha source flag (AIS) - if true, alpha comes from shape; if false, from opacity</summary>
    public bool AlphaIsShape { get; set; }

    /// <summary>Text knockout flag (TK) - affects text rendering in transparency groups</summary>
    public bool TextKnockout { get; set; } = true;

    /// <summary>Overprint mode for stroking (OP)</summary>
    public bool StrokeOverprint { get; set; }

    /// <summary>Overprint mode for non-stroking (op)</summary>
    public bool FillOverprint { get; set; }

    /// <summary>Overprint mode (OPM) - 0 or 1</summary>
    public int OverprintMode { get; set; }

    /// <summary>
    /// Per-plate CMYK overprint mask for non-stroking (fill), derived from a Separation/DeviceN source
    /// colour space's colorant names (Cyan→C, Magenta→M, Yellow→Y, Black→K, All→all four). Per
    /// ISO 32000 §8.6.6.3, an overprinting Separation/DeviceN colour affects ONLY the device plates its
    /// colorants map to and preserves the others REGARDLESS of the overprint mode (OPM). When set,
    /// renderers paint only these plates and ignore OPM; null for DeviceCMYK/RGB/Gray or spot-named
    /// colorants (the existing OPM zero-component logic applies instead).
    /// </summary>
    public (bool C, bool M, bool Y, bool K)? FillOverprintPlates { get; set; }

    /// <summary>Per-plate CMYK overprint mask for stroking; see <see cref="FillOverprintPlates"/>.</summary>
    public (bool C, bool M, bool Y, bool K)? StrokeOverprintPlates { get; set; }

    /// <summary>
    /// Black-point compensation flag (PDF 2.0 /UseBlackPtComp, ISO 32000-2 Table 57). When true,
    /// ICC colour conversions map the source profile's darkest reproducible black to the
    /// destination's, so dark CMYK shadows render full rather than washed-out grey. Defaults to
    /// true to match Adobe/Acrobat's default colour management — a no-op for profiles without a
    /// raised black point, so only CMYK/ink content changes; an explicit /UseBlackPtComp /OFF
    /// disables it.
    /// </summary>
    public bool UseBlackPointCompensation { get; set; } = true;

    /// <summary>
    /// Colour rendering intent (PDF <c>ri</c> operator / ExtGState /RI), as the PDF name —
    /// "Perceptual", "RelativeColorimetric", "Saturation", or "AbsoluteColorimetric". Selects the
    /// ICC rendering intent for colour conversions. Defaults to relative colorimetric (the PDF and
    /// ICC default); an image's own /Intent overrides this for that image.
    /// </summary>
    public string RenderingIntent { get; set; } = "RelativeColorimetric";

    /// <summary>Smoothness tolerance (SM)</summary>
    public double Smoothness { get; set; }

    /// <summary>
    /// Sets stroke color to grayscale
    /// </summary>
    public void SetStrokeGray(double gray)
    {
        StrokeColorSpace = "DeviceGray";
        StrokeColor = [gray];
    }

    /// <summary>
    /// Sets fill color to grayscale
    /// </summary>
    public void SetFillGray(double gray)
    {
        FillColorSpace = "DeviceGray";
        FillColor = [gray];
    }

    /// <summary>
    /// Sets stroke color to RGB
    /// </summary>
    public void SetStrokeRgb(double r, double g, double b)
    {
        StrokeColorSpace = "DeviceRGB";
        StrokeColor = [r, g, b];
    }

    /// <summary>
    /// Sets fill color to RGB
    /// </summary>
    public void SetFillRgb(double r, double g, double b)
    {
        FillColorSpace = "DeviceRGB";
        FillColor = [r, g, b];
    }

    /// <summary>
    /// Sets stroke color to CMYK
    /// </summary>
    public void SetStrokeCmyk(double c, double m, double y, double k)
    {
        StrokeColorSpace = "DeviceCMYK";
        StrokeColor = [c, m, y, k];
    }

    /// <summary>
    /// Sets fill color to CMYK
    /// </summary>
    public void SetFillCmyk(double c, double m, double y, double k)
    {
        FillColorSpace = "DeviceCMYK";
        FillColor = [c, m, y, k];
    }

    /// <summary>
    /// Creates a deep copy of this graphics state for the graphics state stack
    /// </summary>
    public PdfGraphicsState Clone()
    {
        var clone = new PdfGraphicsState
        {
            InitialTransformMatrix = InitialTransformMatrix,
            TextMatrix = TextMatrix,
            TextLineMatrix = TextLineMatrix,
            CharacterSpacing = CharacterSpacing,
            WordSpacing = WordSpacing,
            HorizontalScaling = HorizontalScaling,
            Leading = Leading,
            FontName = FontName,
            FontSize = FontSize,
            RenderingMode = RenderingMode,
            TextRise = TextRise,
            LineWidth = LineWidth,
            LineCap = LineCap,
            LineJoin = LineJoin,
            MiterLimit = MiterLimit,
            Flatness = Flatness,
            DashPattern = DashPattern is not null ? [..DashPattern] : null,
            DashPhase = DashPhase,
            StrokeColorSpace = StrokeColorSpace,
            FillColorSpace = FillColorSpace,
            StrokeColor = [..StrokeColor],
            FillColor = [..FillColor],
            ResolvedStrokeColorSpace = ResolvedStrokeColorSpace,
            ResolvedFillColorSpace = ResolvedFillColorSpace,
            ResolvedStrokeColor = [..ResolvedStrokeColor],
            ResolvedFillColor = [..ResolvedFillColor],
            // Pattern state
            FillPatternName = FillPatternName,
            StrokePatternName = StrokePatternName,
            // ExtGState parameters
            StrokeAlpha = StrokeAlpha,
            FillAlpha = FillAlpha,
            BlendMode = BlendMode,
            SoftMask = SoftMask is not null ? SoftMask with { BackdropColor = SoftMask.BackdropColor is not null ? [..SoftMask.BackdropColor] : null } : null,
            AlphaIsShape = AlphaIsShape,
            TextKnockout = TextKnockout,
            StrokeOverprint = StrokeOverprint,
            FillOverprint = FillOverprint,
            OverprintMode = OverprintMode,
            FillOverprintPlates = FillOverprintPlates,
            StrokeOverprintPlates = StrokeOverprintPlates,
            UseBlackPointCompensation = UseBlackPointCompensation,
            RenderingIntent = RenderingIntent,
            Smoothness = Smoothness
        };
        // Copy the double-precision CTM accumulator directly (not via the narrowed Ctm property) so
        // precision survives q/Q save/restore.
        clone._ctm11 = _ctm11; clone._ctm12 = _ctm12; clone._ctm21 = _ctm21;
        clone._ctm22 = _ctm22; clone._ctm31 = _ctm31; clone._ctm32 = _ctm32;
        return clone;
    }

    /// <summary>
    /// Resets text matrices at the start of a text object (BT operator)
    /// </summary>
    public void BeginText()
    {
        TextMatrix = Matrix3x2.Identity;
        TextLineMatrix = Matrix3x2.Identity;
    }

    /// <summary>
    /// Concatenates a matrix to the CTM (cm operator)
    /// </summary>
    public void ConcatenateMatrix(double a, double b, double c, double d, double e, double f)
    {
        // new CTM = [a b c d e f] * current, computed entirely in double so max-magnitude reals
        // (PDF allows up to ±3.4e38, beyond float range) don't overflow to ∞/NaN and blank the page.
        double na = a * _ctm11 + b * _ctm21;
        double nb = a * _ctm12 + b * _ctm22;
        double nc = c * _ctm11 + d * _ctm21;
        double nd = c * _ctm12 + d * _ctm22;
        double ne = e * _ctm11 + f * _ctm21 + _ctm31;
        double nf = e * _ctm12 + f * _ctm22 + _ctm32;
        // Lazy log form: `cm` is a hot operator (thousands per page, and per Type3 char-proc). Building
        // these interpolated strings eagerly formatted ~18 floats per cm even with logging disabled.
        PdfLogger.Log(LogCategory.Transforms, () => $"CONCAT Before: CTM=[{_ctm11:F4}, {_ctm12:F4}, {_ctm21:F4}, {_ctm22:F4}, {_ctm31:F4}, {_ctm32:F4}]");
        PdfLogger.Log(LogCategory.Transforms, () => $"CONCAT Matrix: [{a:F4}, {b:F4}, {c:F4}, {d:F4}, {e:F4}, {f:F4}]");
        _ctm11 = na; _ctm12 = nb; _ctm21 = nc; _ctm22 = nd; _ctm31 = ne; _ctm32 = nf;
        PdfLogger.Log(LogCategory.Transforms, () => $"CONCAT After:  CTM=[{_ctm11:F4}, {_ctm12:F4}, {_ctm21:F4}, {_ctm22:F4}, {_ctm31:F4}, {_ctm32:F4}]");
    }

    /// <summary>
    /// Sets the text matrix (Tm operator)
    /// </summary>
    public void SetTextMatrix(double a, double b, double c, double d, double e, double f)
    {
        TextMatrix = new Matrix3x2((float)a, (float)b, (float)c, (float)d, (float)e, (float)f);
        TextLineMatrix = TextMatrix;
    }

    /// <summary>
    /// Moves text position by (tx, ty) - Td operator
    /// </summary>
    public void MoveTextPosition(double tx, double ty)
    {
        var translation = Matrix3x2.CreateTranslation((float)tx, (float)ty);
        TextMatrix = translation * TextLineMatrix;
        TextLineMatrix = TextMatrix;
    }

    /// <summary>
    /// Advances the text matrix (used after showing text)
    /// Only updates TextMatrix, not TextLineMatrix
    /// </summary>
    public void AdvanceTextMatrix(double tx, double ty)
    {
        var translation = Matrix3x2.CreateTranslation((float)tx, (float)ty);
        TextMatrix = translation * TextMatrix;
        // Do NOT update TextLineMatrix - it stays at the start of the line
    }

    /// <summary>
    /// Moves to the next line (T* operator) - uses leading
    /// </summary>
    public void MoveToNextLine()
    {
        MoveTextPosition(0, -Leading);
    }

    /// <summary>
    /// Transforms a point from text space to user space
    /// </summary>
    public Vector2 TransformPoint(double x, double y)
    {
        var point = new Vector2((float)x, (float)y);
        return Vector2.Transform(point, TextMatrix * Ctm);
    }

    /// <summary>
    /// Gets the current text position in user space
    /// </summary>
    public Vector2 GetTextPosition()
    {
        return TransformPoint(0, 0);
    }

    /// <summary>
    /// Gets the rectangle for an image XObject
    /// In PDF, images are mapped to a 1x1 unit square, and the CTM provides the final dimensions
    /// ISO 32000-1:2008 section 8.9.5
    /// </summary>
    public PdfRectangle GetImageRectangle()
    {
        // In the CTM:
        // M11 = width (scale in X direction)
        // M22 = height (scale in Y direction)
        // M31 = x translation
        // M32 = y translation
        double x = Ctm.M31;
        double y = Ctm.M32;
        double width = Ctm.M11;
        double height = Ctm.M22;

        return new PdfRectangle(x, y, x + width, y + height);
    }
}
