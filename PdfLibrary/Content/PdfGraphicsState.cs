using System.Numerics;

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
            Flatness = Flatness
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
}
