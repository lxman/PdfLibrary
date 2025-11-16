using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Content.Operators;

// ====================
// Grayscale Color Operators
// ====================

/// <summary>
/// g - Set gray level for nonstroking (fill) operations (0.0 = black, 1.0 = white)
/// </summary>
public class SetFillGrayOperator(double gray) : PdfOperator("g", [new PdfReal(gray)])
{
    public double Gray { get; } = gray;
    public override OperatorCategory Category => OperatorCategory.Color;
}

/// <summary>
/// G - Set gray level for stroking operations
/// </summary>
public class SetStrokeGrayOperator(double gray) : PdfOperator("G", [new PdfReal(gray)])
{
    public double Gray { get; } = gray;
    public override OperatorCategory Category => OperatorCategory.Color;
}

// ====================
// RGB Color Operators
// ====================

/// <summary>
/// rg - Set RGB color for nonstroking operations
/// </summary>
public class SetFillRgbOperator(double r, double g, double b)
    : PdfOperator("rg", [new PdfReal(r), new PdfReal(g), new PdfReal(b)])
{
    public double R { get; } = r;
    public double G { get; } = g;
    public double B { get; } = b;
    public override OperatorCategory Category => OperatorCategory.Color;
}

/// <summary>
/// RG - Set RGB color for stroking operations
/// </summary>
public class SetStrokeRgbOperator(double r, double g, double b)
    : PdfOperator("RG", [new PdfReal(r), new PdfReal(g), new PdfReal(b)])
{
    public double R { get; } = r;
    public double G { get; } = g;
    public double B { get; } = b;
    public override OperatorCategory Category => OperatorCategory.Color;
}

// ====================
// CMYK Color Operators
// ====================

/// <summary>
/// k - Set CMYK color for nonstroking operations
/// </summary>
public class SetFillCmykOperator(double c, double m, double y, double k)
    : PdfOperator("k", [new PdfReal(c), new PdfReal(m), new PdfReal(y), new PdfReal(k)])
{
    public double C { get; } = c;
    public double M { get; } = m;
    public double Y { get; } = y;
    public double K { get; } = k;
    public override OperatorCategory Category => OperatorCategory.Color;
}

/// <summary>
/// K - Set CMYK color for stroking operations
/// </summary>
public class SetStrokeCmykOperator(double c, double m, double y, double k)
    : PdfOperator("K", [new PdfReal(c), new PdfReal(m), new PdfReal(y), new PdfReal(k)])
{
    public double C { get; } = c;
    public double M { get; } = m;
    public double Y { get; } = y;
    public double K { get; } = k;
    public override OperatorCategory Category => OperatorCategory.Color;
}

// ====================
// Color Space Operators
// ====================

/// <summary>
/// cs - Set color space for nonstroking operations
/// </summary>
public class SetFillColorSpaceOperator(PdfName colorSpace) : PdfOperator("cs", [colorSpace])
{
    public string ColorSpace { get; } = colorSpace.Value;
    public override OperatorCategory Category => OperatorCategory.Color;
}

/// <summary>
/// CS - Set color space for stroking operations
/// </summary>
public class SetStrokeColorSpaceOperator(PdfName colorSpace) : PdfOperator("CS", [colorSpace])
{
    public string ColorSpace { get; } = colorSpace.Value;
    public override OperatorCategory Category => OperatorCategory.Color;
}

// ====================
// Generic Color Setting Operators
// ====================

/// <summary>
/// sc - Set color for nonstroking operations (generic, depends on current color space)
/// </summary>
public class SetFillColorOperator(List<PdfObject> components) : PdfOperator("sc", components)
{
    public List<double> Components { get; } = components
        .Select(obj => obj switch
        {
            PdfInteger i => (double)i.Value,
            PdfReal r => r.Value,
            _ => 0.0
        })
        .ToList();

    public override OperatorCategory Category => OperatorCategory.Color;
}

/// <summary>
/// SC - Set color for stroking operations (generic, depends on current color space)
/// </summary>
public class SetStrokeColorOperator(List<PdfObject> components) : PdfOperator("SC", components)
{
    public List<double> Components { get; } = components
        .Select(obj => obj switch
        {
            PdfInteger i => (double)i.Value,
            PdfReal r => r.Value,
            _ => 0.0
        })
        .ToList();

    public override OperatorCategory Category => OperatorCategory.Color;
}

/// <summary>
/// scn - Set color for nonstroking operations (supports Pattern, Separation, DeviceN)
/// </summary>
public class SetFillColorExtendedOperator(List<PdfObject> operands) : PdfOperator("scn", operands)
{
    public List<double> Components { get; } = operands
        .OfType<PdfReal>()
        .Select(r => r.Value)
        .Concat(operands.OfType<PdfInteger>().Select(i => (double)i.Value))
        .ToList();

    public string? PatternName { get; } = operands.OfType<PdfName>().FirstOrDefault()?.Value;

    public override OperatorCategory Category => OperatorCategory.Color;
}

/// <summary>
/// SCN - Set color for stroking operations (supports Pattern, Separation, DeviceN)
/// </summary>
public class SetStrokeColorExtendedOperator(List<PdfObject> operands) : PdfOperator("SCN", operands)
{
    public List<double> Components { get; } = operands
        .OfType<PdfReal>()
        .Select(r => r.Value)
        .Concat(operands.OfType<PdfInteger>().Select(i => (double)i.Value))
        .ToList();

    public string? PatternName { get; } = operands.OfType<PdfName>().FirstOrDefault()?.Value;

    public override OperatorCategory Category => OperatorCategory.Color;
}
