using PdfLibrary.Editing.Forms;

namespace PdfLibrary.Editing;

public sealed partial class PdfDocumentEditor
{
    private PdfFormFields? _forms;

    /// <summary>The document's AcroForm fields.</summary>
    public PdfFormFields Forms => _forms ??= new PdfFormFields(_document);
}
