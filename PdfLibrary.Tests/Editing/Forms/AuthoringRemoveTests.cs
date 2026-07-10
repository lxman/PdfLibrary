using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;

namespace PdfLibrary.Tests.Editing.Forms;

public class AuthoringRemoveTests
{
    private static readonly PdfRect Rect = new(72, 700, 372, 720);

    [Fact]
    public void Remove_ExistingField_GoneAfterRoundTrip()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        editor.Forms.AddTextField(0, "a", Rect);
        editor.Forms.AddTextField(0, "b", new PdfRect(72, 650, 372, 670));

        Assert.True(editor.Forms.Remove("a"));

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        Assert.Single(reopened.Forms);
        Assert.Null(reopened.Forms["a"]);
        Assert.NotNull(reopened.Forms["b"]);
    }

    [Fact]
    public void Remove_MissingField_ReturnsFalse()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        editor.Forms.AddTextField(0, "a", Rect);
        Assert.False(editor.Forms.Remove("nope"));
        Assert.Single(editor.Forms);
    }

    [Fact]
    public void Remove_LastField_PrunesAcroForm()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "only", Rect);
        PdfLibrary.Structure.PdfDocument doc = field.Doc; // capture before removal

        Assert.True(editor.Forms.Remove("only"));

        Assert.Empty(editor.Forms);
        Assert.False(doc.CatalogDictionary!.ContainsKey(new PdfName("AcroForm")));
    }

    [Fact]
    public void Remove_RadioGroup_RemovesParentAndAllWidgets()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainTwoPages();
        editor.Forms.AddRadioGroup("span", new[]
        {
            new PdfRadioOptionPlacement(0, new PdfRect(72, 700, 86, 714), "A"),
            new PdfRadioOptionPlacement(1, new PdfRect(72, 700, 86, 714), "B")
        });

        Assert.True(editor.Forms.Remove("span"));

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        Assert.Empty(reopened.Forms);
    }

    [Fact]
    public void Remove_WidgetLeavesPageAnnots()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "a", Rect);
        PdfLibrary.Structure.PdfDocument doc = field.Doc;

        editor.Forms.Remove("a");

        PdfDictionary page = doc.GetPages()[0].Dictionary;
        // /Annots either absent or empty of the removed widget.
        if (page.Get(new PdfName("Annots")) is PdfArray annots)
            Assert.Empty(annots);
    }

    [Fact]
    public void Remove_SignedSignature_Refuses()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfSignatureField sig = editor.Forms.AddSignatureField(0, "sig1", Rect);
        // Simulate a signed field: /V present (InternalsVisibleTo gives Dict access).
        sig.Dict[new PdfName("V")] = new PdfDictionary();

        Assert.Throws<InvalidOperationException>(() => editor.Forms.Remove("sig1"));
        Assert.Single(editor.Forms); // still there
    }

    [Fact]
    public void Remove_UnsignedSignature_Succeeds()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        editor.Forms.AddSignatureField(0, "sig1", Rect);
        Assert.True(editor.Forms.Remove("sig1"));
        Assert.Empty(editor.Forms);
    }
}
