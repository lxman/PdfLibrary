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
