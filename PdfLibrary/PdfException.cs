namespace PdfLibrary;

/// <summary>
/// Base class for all exceptions raised by PdfLibrary. Catch this to handle any
/// PDF-specific failure (malformed input, security/decryption, etc.) without
/// also catching unrelated runtime exceptions.
/// </summary>
public abstract class PdfException : Exception
{
    /// <summary>Initializes a new instance with the specified message.</summary>
    protected PdfException(string message) : base(message) { }

    /// <summary>Initializes a new instance with the specified message and inner exception.</summary>
    protected PdfException(string message, Exception innerException) : base(message, innerException) { }
}
