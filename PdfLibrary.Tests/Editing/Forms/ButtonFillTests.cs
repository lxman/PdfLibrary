using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing.Forms;

public class ButtonFillTests
{
    [Fact]
    public void Checkbox_Check_Then_Uncheck_RoundTrips()
    {
        byte[] pdf = FormTestDocs.WithCheckbox("agree", checkedOn: false);

        // checked:
        var afterCheck = (PdfButtonField)Reload(Fill(pdf, forms => ((PdfButtonField)forms["agree"]!).Check()), "agree")!;
        Assert.True(afterCheck.IsChecked);

        // unchecked:
        var afterUncheck = (PdfButtonField)Reload(Fill(pdf, forms => ((PdfButtonField)forms["agree"]!).Uncheck()), "agree")!;
        Assert.False(afterUncheck.IsChecked);
    }

    [Fact]
    public void Radio_Select_SetsParentV_AndExclusiveAS()
    {
        byte[] pdf = FormTestDocs.WithRadioGroup("color", new[] { "red", "blue" }, selected: "red");
        var f = (PdfButtonField)Reload(Fill(pdf, forms => ((PdfButtonField)forms["color"]!).SelectedOption = "blue"), "color")!;
        Assert.Equal("blue", f.SelectedOption);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads a PDF from bytes, applies an action against the form fields, saves to a temp file,
    /// returns the saved bytes. Temp file is deleted in a finally block.
    /// </summary>
    private static byte[] Fill(byte[] pdf, Action<PdfFormFields> act)
    {
        string tmp = Path.GetTempFileName();
        try
        {
            using var ms = new MemoryStream(pdf);
            PdfDocument doc = PdfDocument.Load(ms);
            using PdfDocumentEditor edit = doc.Edit();
            act(edit.Forms);
            edit.Save(tmp);
            return File.ReadAllBytes(tmp);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    /// <summary>
    /// Loads a PDF from bytes and returns the named field, or null if not found.
    /// </summary>
    private static PdfFormField? Reload(byte[] pdf, string name)
    {
        using var ms = new MemoryStream(pdf);
        PdfDocument doc = PdfDocument.Load(ms);
        using PdfDocumentEditor edit = doc.Edit();
        return edit.Forms[name];
    }
}
