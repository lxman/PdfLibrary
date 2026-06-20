namespace PdfLibrary.Editing;

public sealed partial class PdfDocumentEditor
{
    private PdfPageLabels? _pageLabels;

    /// <summary>The document's page-label (numbering) ranges.</summary>
    public PdfPageLabels PageLabels => _pageLabels ??= new PdfPageLabels(_document);
}
