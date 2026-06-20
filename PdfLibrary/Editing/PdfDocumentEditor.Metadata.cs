namespace PdfLibrary.Editing;

public sealed partial class PdfDocumentEditor
{
    private PdfMetadata? _metadata;

    /// <summary>
    /// Provides typed access to the document's Info dictionary and XMP metadata stream.
    /// The instance is created lazily on first access.
    /// </summary>
    public PdfMetadata Metadata => _metadata ??= new PdfMetadata(_document);
}
