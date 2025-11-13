using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Content.Operators;

/// <summary>
/// q - Save graphics state
/// </summary>
public class SaveGraphicsStateOperator() : PdfOperator("q", [])
{
    public override OperatorCategory Category => OperatorCategory.GraphicsState;
}

/// <summary>
/// Q - Restore graphics state
/// </summary>
public class RestoreGraphicsStateOperator() : PdfOperator("Q", [])
{
    public override OperatorCategory Category => OperatorCategory.GraphicsState;
}

/// <summary>
/// cm - Concatenate matrix to current transformation matrix (CTM)
/// </summary>
public class ConcatenateMatrixOperator(double a, double b, double c, double d, double e, double f)
    : PdfOperator("cm", [
        new PdfReal(a), new PdfReal(b), new PdfReal(c),
        new PdfReal(d), new PdfReal(e), new PdfReal(f)
    ])
{
    public double A { get; } = a;
    public double B { get; } = b;
    public double C { get; } = c;
    public double D { get; } = d;
    public double E { get; } = e;
    public double F { get; } = f;

    public override OperatorCategory Category => OperatorCategory.GraphicsState;
}

/// <summary>
/// w - Set line width
/// </summary>
public class SetLineWidthOperator(double width) : PdfOperator("w", [new PdfReal(width)])
{
    public double Width { get; } = width;

    public override OperatorCategory Category => OperatorCategory.GraphicsState;
}

/// <summary>
/// J - Set line cap style
/// </summary>
public class SetLineCapOperator(int style) : PdfOperator("J", [new PdfInteger(style)])
{
    public int Style { get; } = style;

    public override OperatorCategory Category => OperatorCategory.GraphicsState;
}

/// <summary>
/// j - Set line join style
/// </summary>
public class SetLineJoinOperator(int style) : PdfOperator("j", [new PdfInteger(style)])
{
    public int Style { get; } = style;

    public override OperatorCategory Category => OperatorCategory.GraphicsState;
}

/// <summary>
/// M - Set miter limit
/// </summary>
public class SetMiterLimitOperator(double limit) : PdfOperator("M", [new PdfReal(limit)])
{
    public double Limit { get; } = limit;

    public override OperatorCategory Category => OperatorCategory.GraphicsState;
}

/// <summary>
/// d - Set line dash pattern
/// </summary>
public class SetDashPatternOperator(PdfArray dashArray, double dashPhase)
    : PdfOperator("d", [dashArray, new PdfReal(dashPhase)])
{
    public PdfArray DashArray { get; } = dashArray;
    public double DashPhase { get; } = dashPhase;

    public override OperatorCategory Category => OperatorCategory.GraphicsState;
}

/// <summary>
/// gs - Set graphics state from parameter dictionary
/// </summary>
public class SetGraphicsStateOperator(PdfName dictName) : PdfOperator("gs", [dictName])
{
    public string DictName { get; } = dictName.Value;

    public override OperatorCategory Category => OperatorCategory.GraphicsState;
}

/// <summary>
/// ri - Set color rendering intent
/// </summary>
public class SetRenderingIntentOperator(PdfName intent) : PdfOperator("ri", [intent])
{
    public string Intent { get; } = intent.Value;

    public override OperatorCategory Category => OperatorCategory.GraphicsState;
}

/// <summary>
/// i - Set flatness tolerance
/// </summary>
public class SetFlatnessOperator(double flatness) : PdfOperator("i", [new PdfReal(flatness)])
{
    public double Flatness { get; } = flatness;

    public override OperatorCategory Category => OperatorCategory.GraphicsState;
}
