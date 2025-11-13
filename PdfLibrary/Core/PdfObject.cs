namespace PdfLibrary.Core;

/// <summary>
/// Base class for all PDF objects as defined in ISO 32000-1:2008 section 7.3
/// PDF supports 8 basic object types: Boolean, Numeric (Integer/Real), String, Name, Array, Dictionary, Stream, and Null
/// </summary>
public abstract class PdfObject
{
    /// <summary>
    /// Gets the PDF object type
    /// </summary>
    public abstract PdfObjectType Type { get; }

    /// <summary>
    /// Writes this object to the PDF syntax format
    /// </summary>
    public abstract string ToPdfString();

    /// <summary>
    /// Indicates whether this object is an indirect object
    /// </summary>
    public bool IsIndirect { get; internal set; }

    /// <summary>
    /// Object number for indirect objects (positive integer)
    /// </summary>
    public int ObjectNumber { get; internal set; }

    /// <summary>
    /// Generation number for indirect objects (non-negative integer)
    /// </summary>
    public int GenerationNumber { get; internal set; }

    public override string ToString() => ToPdfString();
}

/// <summary>
/// Enumeration of PDF object types
/// </summary>
public enum PdfObjectType
{
    Null,
    Boolean,
    Integer,
    Real,
    String,
    Name,
    Array,
    Dictionary,
    Stream,
    IndirectReference
}
