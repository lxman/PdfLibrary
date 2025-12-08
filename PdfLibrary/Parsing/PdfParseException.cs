namespace PdfLibrary.Parsing;

/// <summary>
/// Exception thrown when PDF parsing fails
/// </summary>
internal class PdfParseException : Exception
{
    public PdfParseException(string message) : base(message)
    {
    }

    public PdfParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}