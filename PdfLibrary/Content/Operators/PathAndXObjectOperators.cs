using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Content.Operators;

/// <summary>
/// m - Begin new subpath
/// </summary>
internal class MoveToOperator(double x, double y) : PdfOperator("m", [new PdfReal(x), new PdfReal(y)])
{
    public double X { get; } = x;
    public double Y { get; } = y;

    public override OperatorCategory Category => OperatorCategory.PathConstruction;
}

/// <summary>
/// l - Append straight line segment
/// </summary>
internal class LineToOperator(double x, double y) : PdfOperator("l", [new PdfReal(x), new PdfReal(y)])
{
    public double X { get; } = x;
    public double Y { get; } = y;

    public override OperatorCategory Category => OperatorCategory.PathConstruction;
}

/// <summary>
/// c - Append cubic Bézier curve
/// </summary>
internal class CurveToOperator(double x1, double y1, double x2, double y2, double x3, double y3)
    : PdfOperator("c", [
        new PdfReal(x1), new PdfReal(y1),
        new PdfReal(x2), new PdfReal(y2),
        new PdfReal(x3), new PdfReal(y3)
    ])
{
    public double X1 { get; } = x1;
    public double Y1 { get; } = y1;
    public double X2 { get; } = x2;
    public double Y2 { get; } = y2;
    public double X3 { get; } = x3;
    public double Y3 { get; } = y3;

    public override OperatorCategory Category => OperatorCategory.PathConstruction;
}

/// <summary>
/// re - Append rectangle
/// </summary>
internal class RectangleOperator(double x, double y, double width, double height)
    : PdfOperator("re", [
        new PdfReal(x), new PdfReal(y),
        new PdfReal(width), new PdfReal(height)
    ])
{
    public double X { get; } = x;
    public double Y { get; } = y;
    public double Width { get; } = width;
    public double Height { get; } = height;

    public override OperatorCategory Category => OperatorCategory.PathConstruction;
}

/// <summary>
/// h - Close current subpath
/// </summary>
internal class ClosePathOperator() : PdfOperator("h", [])
{
    public override OperatorCategory Category => OperatorCategory.PathConstruction;
}

/// <summary>
/// S - Stroke path
/// </summary>
internal class StrokeOperator() : PdfOperator("S", [])
{
    public override OperatorCategory Category => OperatorCategory.PathPainting;
}

/// <summary>
/// s - Close and stroke path
/// </summary>
internal class CloseAndStrokeOperator() : PdfOperator("s", [])
{
    public override OperatorCategory Category => OperatorCategory.PathPainting;
}

/// <summary>
/// f - Fill path using nonzero winding number rule
/// </summary>
internal class FillOperator() : PdfOperator("f", [])
{
    public override OperatorCategory Category => OperatorCategory.PathPainting;
}

/// <summary>
/// f* - Fill path using even-odd rule
/// </summary>
internal class FillEvenOddOperator() : PdfOperator("f*", [])
{
    public override OperatorCategory Category => OperatorCategory.PathPainting;
}

/// <summary>
/// B - Fill and stroke path using nonzero winding number rule
/// </summary>
internal class FillAndStrokeOperator() : PdfOperator("B", [])
{
    public override OperatorCategory Category => OperatorCategory.PathPainting;
}

/// <summary>
/// B* - Fill and stroke path using even-odd rule
/// </summary>
internal class FillAndStrokeEvenOddOperator() : PdfOperator("B*", [])
{
    public override OperatorCategory Category => OperatorCategory.PathPainting;
}

/// <summary>
/// b - Close, fill, and stroke path using nonzero winding number rule
/// </summary>
internal class CloseAndFillAndStrokeOperator() : PdfOperator("b", [])
{
    public override OperatorCategory Category => OperatorCategory.PathPainting;
}

/// <summary>
/// b* - Close, fill, and stroke path using even-odd rule
/// </summary>
internal class CloseAndFillAndStrokeEvenOddOperator() : PdfOperator("b*", [])
{
    public override OperatorCategory Category => OperatorCategory.PathPainting;
}

/// <summary>
/// n - End path without filling or stroking (clipping path)
/// </summary>
internal class EndPathOperator() : PdfOperator("n", [])
{
    public override OperatorCategory Category => OperatorCategory.PathPainting;
}

/// <summary>
/// W - Set clipping path using nonzero winding number rule
/// </summary>
internal class ClipOperator() : PdfOperator("W", [])
{
    public override OperatorCategory Category => OperatorCategory.PathPainting;
}

/// <summary>
/// W* - Set clipping path using even-odd rule
/// </summary>
internal class ClipEvenOddOperator() : PdfOperator("W*", [])
{
    public override OperatorCategory Category => OperatorCategory.PathPainting;
}

/// <summary>
/// Do - Invoke named XObject
/// </summary>
internal class InvokeXObjectOperator(PdfName name) : PdfOperator("Do", [name])
{
    public string XObjectName { get; } = name.Value;

    public override OperatorCategory Category => OperatorCategory.XObject;
}

/// <summary>
/// Generic operator for unrecognized or simple operators
/// </summary>
internal class GenericOperator(string name, List<PdfObject> operands) : PdfOperator(name, operands)
{
    public override OperatorCategory Category => OperatorCategory.Unknown;
}
