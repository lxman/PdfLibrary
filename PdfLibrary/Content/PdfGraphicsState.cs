using System.Numerics;
using PdfLibrary.Document;

namespace PdfLibrary.Content;

/// <summary>
/// Represents the graphics state during content stream processing (ISO 32000-1:2008 section 8.4)
/// Tracks the Current Transformation Matrix (CTM), text state, and other graphics parameters
/// </summary>
public class PdfGraphicsState
{
    /// <summary>
    /// Current Transformation Matrix - transforms from user space to device space
    /// Represented as [a b c d e f] where:
    /// x' = a*x + c*y + e
    /// y' = b*x + d*y + f
    /// </summary>
    public Matrix3x2 Ctm { get; set; } = Matrix3x2.Identity;

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

    // Color state
    /// <summary>Stroke color space (default: DeviceGray)</summary>
    public string StrokeColorSpace { get; set; } = "DeviceGray";

    /// <summary>Fill color space (default: DeviceGray)</summary>
    public string FillColorSpace { get; set; } = "DeviceGray";

    /// <summary>Stroke color components (default: black)</summary>
    public List<double> StrokeColor { get; set; } = [0.0];

    /// <summary>Fill color components (default: black)</summary>
    public List<double> FillColor { get; set; } = [0.0];

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
        return new PdfGraphicsState
        {
            Ctm = Ctm,
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
            StrokeColorSpace = StrokeColorSpace,
            FillColorSpace = FillColorSpace,
            StrokeColor = [..StrokeColor],
            FillColor = [..FillColor]
        };
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
        var matrix = new Matrix3x2((float)a, (float)b, (float)c, (float)d, (float)e, (float)f);
        Ctm = matrix * Ctm;
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
    /// Moves to next line (T* operator) - uses leading
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
    /// Calculates the advance width for a character, accounting for spacing
    /// </summary>
    public double GetCharacterAdvance(double characterWidth, bool isSpace)
    {
        double advance = characterWidth * FontSize + CharacterSpacing;
        if (isSpace)
            advance += WordSpacing;
        return advance * HorizontalScaling / 100.0;
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
