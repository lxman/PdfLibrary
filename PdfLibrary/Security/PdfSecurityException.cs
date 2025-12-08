namespace PdfLibrary.Security;

/// <summary>
/// Exception thrown when PDF security operations fail.
/// </summary>
internal class PdfSecurityException : Exception
{
    public PdfSecurityException(string message) : base(message) { }
    public PdfSecurityException(string message, Exception inner) : base(message, inner) { }
}