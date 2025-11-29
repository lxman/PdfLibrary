using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Content.Operators;

/// <summary>
/// BT - Begin text object
/// </summary>
internal class BeginTextOperator() : PdfOperator("BT", [])
{
    public override OperatorCategory Category => OperatorCategory.TextObject;
}

/// <summary>
/// ET - End text object
/// </summary>
internal class EndTextOperator() : PdfOperator("ET", [])
{
    public override OperatorCategory Category => OperatorCategory.TextObject;
}

/// <summary>
/// Tj - Show text string
/// </summary>
internal class ShowTextOperator(PdfString text) : PdfOperator("Tj", [text])
{
    public PdfString Text { get; } = text;

    public override OperatorCategory Category => OperatorCategory.TextShowing;
}

/// <summary>
/// TJ - Show text with individual glyph positioning
/// </summary>
internal class ShowTextWithPositioningOperator(PdfArray array) : PdfOperator("TJ", [array])
{
    public PdfArray Array { get; } = array;

    public override OperatorCategory Category => OperatorCategory.TextShowing;
}

/// <summary>
/// ' - Move to next line and show text
/// </summary>
internal class MoveToNextLineAndShowTextOperator(PdfString text) : PdfOperator("'", [text])
{
    public PdfString Text { get; } = text;

    public override OperatorCategory Category => OperatorCategory.TextShowing;
}

/// <summary>
/// " - Set word and character spacing, move to next line, and show text
/// </summary>
internal class SetSpacingMoveAndShowTextOperator(PdfReal wordSpacing, PdfReal charSpacing, PdfString text)
    : PdfOperator("\"", [wordSpacing, charSpacing, text])
{
    public double WordSpacing { get; } = wordSpacing.Value;
    public double CharSpacing { get; } = charSpacing.Value;
    public PdfString Text { get; } = text;

    public override OperatorCategory Category => OperatorCategory.TextShowing;
}

/// <summary>
/// Td - Move text position
/// </summary>
internal class MoveTextPositionOperator(double tx, double ty) : PdfOperator("Td", [new PdfReal(tx), new PdfReal(ty)])
{
    public double Tx { get; } = tx;
    public double Ty { get; } = ty;

    public override OperatorCategory Category => OperatorCategory.TextPositioning;
}

/// <summary>
/// TD - Move text position and set leading
/// </summary>
internal class MoveTextPositionAndSetLeadingOperator(double tx, double ty)
    : PdfOperator("TD", [new PdfReal(tx), new PdfReal(ty)])
{
    public double Tx { get; } = tx;
    public double Ty { get; } = ty;

    public override OperatorCategory Category => OperatorCategory.TextPositioning;
}

/// <summary>
/// Tm - Set text matrix
/// </summary>
internal class SetTextMatrixOperator(double a, double b, double c, double d, double e, double f)
    : PdfOperator("Tm", [
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

    public override OperatorCategory Category => OperatorCategory.TextPositioning;
}

/// <summary>
/// T* - Move to start of next line
/// </summary>
internal class MoveToNextLineOperator() : PdfOperator("T*", [])
{
    public override OperatorCategory Category => OperatorCategory.TextPositioning;
}

/// <summary>
/// Tf - Set text font and size
/// </summary>
internal class SetTextFontOperator(PdfName font, double size) : PdfOperator("Tf", [font, new PdfReal(size)])
{
    public string Font { get; } = font.Value;
    public double Size { get; } = size;

    public override OperatorCategory Category => OperatorCategory.TextState;
}

/// <summary>
/// Tc - Set character spacing
/// </summary>
internal class SetCharSpacingOperator(double spacing) : PdfOperator("Tc", [new PdfReal(spacing)])
{
    public double Spacing { get; } = spacing;

    public override OperatorCategory Category => OperatorCategory.TextState;
}

/// <summary>
/// Tw - Set word spacing
/// </summary>
internal class SetWordSpacingOperator(double spacing) : PdfOperator("Tw", [new PdfReal(spacing)])
{
    public double Spacing { get; } = spacing;

    public override OperatorCategory Category => OperatorCategory.TextState;
}

/// <summary>
/// Tz - Set horizontal text scaling
/// </summary>
internal class SetHorizontalScalingOperator(double scale) : PdfOperator("Tz", [new PdfReal(scale)])
{
    public double Scale { get; } = scale;

    public override OperatorCategory Category => OperatorCategory.TextState;
}

/// <summary>
/// TL - Set text leading
/// </summary>
internal class SetTextLeadingOperator(double leading) : PdfOperator("TL", [new PdfReal(leading)])
{
    public double Leading { get; } = leading;

    public override OperatorCategory Category => OperatorCategory.TextState;
}

/// <summary>
/// Tr - Set text rendering mode
/// </summary>
internal class SetTextRenderingModeOperator(int mode) : PdfOperator("Tr", [new PdfInteger(mode)])
{
    public int Mode { get; } = mode;

    public override OperatorCategory Category => OperatorCategory.TextState;
}

/// <summary>
/// Ts - Set text rise
/// </summary>
internal class SetTextRiseOperator(double rise) : PdfOperator("Ts", [new PdfReal(rise)])
{
    public double Rise { get; } = rise;

    public override OperatorCategory Category => OperatorCategory.TextState;
}
