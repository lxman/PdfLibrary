namespace PdfLibrary.Core.Primitives;

/// <summary>
/// Represents a PDF boolean object (ISO 32000-1:2008 section 7.3.2)
/// Boolean objects represent logical true/false values using keywords true and false
/// </summary>
public sealed class PdfBoolean : PdfObject
{
    /// <summary>
    /// Singleton instance for true
    /// </summary>
    public static readonly PdfBoolean True = new(true);

    /// <summary>
    /// Singleton instance for false
    /// </summary>
    public static readonly PdfBoolean False = new(false);

    public bool Value { get; }

    private PdfBoolean(bool value) => Value = value;

    public override PdfObjectType Type => PdfObjectType.Boolean;

    public override string ToPdfString() => Value ? "true" : "false";

    /// <summary>
    /// Gets a PdfBoolean instance for the specified value
    /// </summary>
    public static PdfBoolean FromValue(bool value) => value ? True : False;

    public override bool Equals(object? obj) => obj is PdfBoolean other && other.Value == Value;

    public override int GetHashCode() => Value.GetHashCode();

    public static implicit operator bool(PdfBoolean pdfBool) => pdfBool.Value;
    public static implicit operator PdfBoolean(bool value) => FromValue(value);
}
