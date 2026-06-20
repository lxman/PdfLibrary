namespace PdfLibrary.Editing;

public sealed partial class PdfDocumentEditor
{
    private PdfNamedDestinations? _namedDestinations;

    /// <summary>The document's named destinations.</summary>
    public PdfNamedDestinations NamedDestinations => _namedDestinations ??= new PdfNamedDestinations(_document);
}
