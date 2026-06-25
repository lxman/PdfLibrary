using PdfLibrary.Builder;
using PdfLibrary.Editing;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Editing;

public class ViewerSettingsNullClearTests
{
    // Regression: a bool? viewer preference setter silently ignored null, so once set it could
    // never be cleared. Setting null must remove the preference (matching PageMode/PageLayout).
    [Fact]
    public void SettingViewerPreferenceToNull_ClearsIt()
    {
        byte[] pdf = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("x", 100, 700))
            .ToByteArray();
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf));
        using PdfDocumentEditor editor = doc.Edit();

        editor.ViewerSettings.HideToolbar = true;
        Assert.True(editor.ViewerSettings.HideToolbar);

        editor.ViewerSettings.HideToolbar = null;

        Assert.Null(editor.ViewerSettings.HideToolbar);
    }
}
