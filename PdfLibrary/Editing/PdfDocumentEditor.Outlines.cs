namespace PdfLibrary.Editing;

public sealed partial class PdfDocumentEditor
{
    private PdfOutlineCollection? _outlines;

    /// <summary>The document's outline (bookmark) tree.</summary>
    public PdfOutlineCollection Outlines => _outlines ??= new PdfOutlineCollection(_document);
}
