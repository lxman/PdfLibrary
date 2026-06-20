using System.Text;
using System.Text.RegularExpressions;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing.Forms;

public class ChoiceFillTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Decodes the /AP /N stream of the first widget of the given choice field to a string.
    /// </summary>
    private static string ApStreamText(PdfDocument doc, PdfChoiceField field)
    {
        var widget = field.Widgets[0];

        var apRaw = widget.Get(new PdfLibrary.Core.Primitives.PdfName("AP"));
        var ap = FormFieldTree.Resolve(doc, apRaw) as PdfLibrary.Core.Primitives.PdfDictionary;
        Assert.NotNull(ap);

        var nRaw = ap!.Get(new PdfLibrary.Core.Primitives.PdfName("N"));
        var nObj = FormFieldTree.Resolve(doc, nRaw);
        Assert.NotNull(nObj);

        var stream = nObj as PdfLibrary.Core.Primitives.PdfStream;
        if (stream is null && nObj is PdfLibrary.Core.Primitives.PdfIndirectReference ir)
            stream = doc.GetObject(ir.ObjectNumber) as PdfLibrary.Core.Primitives.PdfStream;

        Assert.NotNull(stream);
        return Encoding.ASCII.GetString(stream!.GetDecodedData());
    }

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

    /// <summary>
    /// Loads a PDF from bytes and returns the AP stream text for the named choice field.
    /// </summary>
    private static string ReloadApStream(byte[] pdf, string name)
    {
        using var ms = new MemoryStream(pdf);
        PdfDocument doc = PdfDocument.Load(ms);
        using PdfDocumentEditor edit = doc.Edit();
        var field = (PdfChoiceField)edit.Forms[name]!;
        return ApStreamText(doc, field);
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Combo_SetValue_GeneratesTextLikeAppearance()
    {
        byte[] pdf = FormTestDocs.WithChoice(
            "city",
            [("NYC", "New York"), ("LA", "Los Angeles")],
            combo: true);

        byte[] filled = Fill(pdf, forms =>
        {
            var f = (PdfChoiceField)forms["city"]!;
            f.SelectedValues = ["LA"];
        });

        // Round-trip value
        var reloaded = (PdfChoiceField)Reload(filled, "city")!;
        Assert.Equal(["LA"], reloaded.SelectedValues);

        // AP content contains display text "Los Angeles" in the /Tx BMC block
        string ap = ReloadApStream(filled, "city");
        Assert.Contains("/Tx BMC", ap);
        Assert.Contains("Los Angeles", ap);
    }

    [Fact]
    public void List_SetIndex_HighlightsRow_AndSetsI()
    {
        byte[] pdf = FormTestDocs.WithChoice(
            "c",
            [("a", "Apple"), ("b", "Banana"), ("c", "Cherry")],
            combo: false);

        byte[] filled = Fill(pdf, forms =>
        {
            var f = (PdfChoiceField)forms["c"]!;
            f.SelectedIndices = [1];
        });

        // Round-trip index and value
        var reloaded = (PdfChoiceField)Reload(filled, "c")!;
        Assert.Equal([1], reloaded.SelectedIndices);
        Assert.Equal(["b"], reloaded.SelectedValues);

        // AP content has a highlight rect (gray fill) followed by row text
        string ap = ReloadApStream(filled, "c");
        Assert.Contains("/Tx BMC", ap);
        // Highlighted row: gray rect operator sequence (0.6 0.6 0.6 rg → re → f)
        Assert.Matches(new Regex(@"0\.6 0\.6 0\.6 rg"), ap);
        Assert.Contains(" re", ap);
        // fill operator 'f' appears after the rect
        Assert.Matches(new Regex(@"re\r?\nf"), ap);
        // All option display texts appear
        Assert.Contains("Apple", ap);
        Assert.Contains("Banana", ap);
        Assert.Contains("Cherry", ap);
    }

    [Fact]
    public void MultiValue_OnSingleSelect_Throws()
    {
        byte[] pdf = FormTestDocs.WithChoice(
            "s",
            [("a", "Alpha"), ("b", "Beta")],
            combo: true);

        using var ms = new MemoryStream(pdf);
        PdfDocument doc = PdfDocument.Load(ms);
        using PdfDocumentEditor edit = doc.Edit();
        var f = (PdfChoiceField)edit.Forms["s"]!;

        Assert.Throws<InvalidOperationException>(() =>
        {
            f.SelectedValues = ["a", "b"];
        });
    }
}
