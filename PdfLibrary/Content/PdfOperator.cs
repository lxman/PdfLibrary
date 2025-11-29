using PdfLibrary.Core;

namespace PdfLibrary.Content;

/// <summary>
/// Represents a PDF content stream operator (ISO 32000-1:2008 section 7.8.2)
/// Operators are commands in content streams that manipulate graphics state and draw content
/// </summary>
internal abstract class PdfOperator(string name, List<PdfObject> operands)
{
    /// <summary>
    /// Gets the operator name (e.g., "Tj", "cm", "q")
    /// </summary>
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

    /// <summary>
    /// Gets the operands (arguments) for this operator
    /// </summary>
    public List<PdfObject> Operands { get; } = operands ?? [];

    /// <summary>
    /// Gets the category of this operator
    /// </summary>
    public abstract OperatorCategory Category { get; }

    public override string ToString() => $"{Name} ({Operands.Count} operands)";
}

/// <summary>
/// Categories of PDF operators
/// </summary>
internal enum OperatorCategory
{
    /// <summary>Graphics state operators (q, Q, cm, w, etc.)</summary>
    GraphicsState,

    /// <summary>Path construction operators (m, l, c, re, etc.)</summary>
    PathConstruction,

    /// <summary>Path painting operators (S, s, f, F, B, etc.)</summary>
    PathPainting,

    /// <summary>Text objects (BT, ET)</summary>
    TextObject,

    /// <summary>Text state operators (Tc, Tw, Tz, TL, etc.)</summary>
    TextState,

    /// <summary>Text positioning operators (Td, TD, Tm, T*)</summary>
    TextPositioning,

    /// <summary>Text showing operators (Tj, TJ, ', ")</summary>
    TextShowing,

    /// <summary>Color operators (CS, cs, SC, sc, etc.)</summary>
    Color,

    /// <summary>Shading operators (sh)</summary>
    Shading,

    /// <summary>Inline images (BI, ID, EI)</summary>
    InlineImage,

    /// <summary>XObject operators (Do)</summary>
    XObject,

    /// <summary>Marked content operators (BMC, BDC, EMC, etc.)</summary>
    MarkedContent,

    /// <summary>Compatibility operators (BX, EX)</summary>
    Compatibility,

    /// <summary>Unknown or unrecognized operator</summary>
    Unknown
}
