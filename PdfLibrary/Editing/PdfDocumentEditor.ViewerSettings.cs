namespace PdfLibrary.Editing;

public sealed partial class PdfDocumentEditor
{
    private PdfViewerSettings? _viewerSettings;

    /// <summary>The document's viewer settings (PageMode, PageLayout, OpenAction, ViewerPreferences).</summary>
    public PdfViewerSettings ViewerSettings => _viewerSettings ??= new PdfViewerSettings(_document);
}
