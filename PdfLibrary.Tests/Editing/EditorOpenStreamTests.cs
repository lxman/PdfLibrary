using PdfLibrary.Builder;
using PdfLibrary.Editing;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Editing;

public class EditorOpenStreamTests
{
    // Parity with PdfDocumentEditor.Open(string): a consumer holding a PDF in a stream
    // (network download, embedded resource, MemoryStream) must be able to enter edit mode
    // without first calling PdfDocument.Load(stream).Edit().
    [Fact]
    public void Open_FromStream_EntersEditModeOverStreamContent()
    {
        byte[] pdf = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("stream-open", 100, 700))
            .ToByteArray();

        using var ms = new MemoryStream(pdf);
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(ms);

        Assert.Equal(1, editor.Pages.Count);
    }
}
