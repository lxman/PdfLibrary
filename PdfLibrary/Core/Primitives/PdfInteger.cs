namespace PdfLibrary.Core.Primitives;

/// <summary>
/// Represents a PDF integer object (ISO 32000-1:2008 section 7.3.3)
/// Integer objects represent mathematical integers written as decimal numerals.
/// Uses long internally to support large integers found in some PDFs (e.g., timestamps, version numbers).
/// </summary>
internal sealed class PdfInteger(long value, bool sourceOutOfInt32Range = false) : PdfObject
{
    /// <summary>
    /// Gets the full long value of this integer.
    /// Use this when the value might exceed int.MaxValue.
    /// </summary>
    public long LongValue { get; } = value;

    /// <summary>
    /// True when the SOURCE literal this was parsed from was a well-formed integer outside
    /// [int.MinValue, int.MaxValue] — the violation ISO 19005-2 clause 6.1.13 test 1 describes — and
    /// the parser therefore clamped it. Only the content-stream parser clamps; the object parser
    /// keeps the true value in <see cref="LongValue"/>, so a rule checks both.
    ///
    /// <para>Deliberately NOT part of <see cref="Equals(object?)"/> or <see cref="GetHashCode"/>:
    /// the VALUE is what identifies an integer, and a marked 0 must stay interchangeable with an
    /// unmarked one so that nothing downstream — rendering above all — can observe the difference.</para>
    /// </summary>
    public bool SourceOutOfInt32Range { get; } = sourceOutOfInt32Range;

    /// <summary>
    /// Gets the integer value. For backward compatibility, this returns (int)LongValue.
    /// Note: This will truncate values that exceed int.MaxValue.
    /// </summary>
    public int Value => (int)LongValue;

    public override PdfObjectType Type => PdfObjectType.Integer;

    public override string ToPdfString() => LongValue.ToString();

    public override bool Equals(object? obj) => obj is PdfInteger other && other.LongValue == LongValue;

    public override int GetHashCode() => LongValue.GetHashCode();

    // Implicit conversion to int for backward compatibility (truncates if necessary)
    public static implicit operator int(PdfInteger pdfInt) => pdfInt.Value;

    // Implicit conversion to long (always safe)
    public static implicit operator long(PdfInteger pdfInt) => pdfInt.LongValue;

    public static implicit operator PdfInteger(int value) => new(value);
    public static implicit operator PdfInteger(long value) => new(value);
}
