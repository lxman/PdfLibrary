using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Editing.Forms;

public class AuthoringTextFieldTests
{
    private static readonly PdfRect Rect = new(72, 700, 372, 720);

    [Fact]
    public void AddTextField_OnPlainDoc_BootstrapsAcroForm()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        editor.Forms.AddTextField(0, "name1", Rect);

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        PdfFormField? field = reopened.Forms["name1"];
        Assert.NotNull(field);
        Assert.IsType<PdfTextField>(field);
        Assert.Single(reopened.Forms);
    }

    [Fact]
    public void AddTextField_ReturnsLiveField_WithWidgetGeometry()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "name1", Rect);

        Assert.Equal("name1", field.FullName);
        Assert.Single(field.Widgets);
        Assert.Equal(0, field.Widgets[0].PageIndex);
        Assert.Equal(72, field.Widgets[0].Rect.Left, 3);
        Assert.Equal(700, field.Widgets[0].Rect.Bottom, 3);
        Assert.Equal(372, field.Widgets[0].Rect.Right, 3);
        Assert.Equal(720, field.Widgets[0].Rect.Top, 3);
    }

    [Fact]
    public void AddTextField_ThenFill_RoundTrips()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "name1", Rect);
        field.Value = "hello authoring";

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        var back = Assert.IsType<PdfTextField>(reopened.Forms["name1"]);
        Assert.Equal("hello authoring", back.Value);
    }

    [Fact]
    public void AddTextField_GeneratesAppearanceStream_AndNoNeedAppearances()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "name1", Rect);

        // /AP /N present on the widget (InternalsVisibleTo lets us read the dict).
        PdfDictionary widget = field.WidgetDicts[0];
        Assert.True(widget.ContainsKey(new PdfName("AP")));

        // Bootstrap never sets /NeedAppearances.
        PdfDictionary acro = FieldAuthor.EnsureAcroForm(field.Doc);
        Assert.False(acro.ContainsKey(new PdfName("NeedAppearances")));
        // Bootstrap wrote the Helvetica default /DA.
        Assert.True(acro.ContainsKey(new PdfName("DA")));
    }

    [Fact]
    public void AddTextField_OnAlreadyFormedDoc_LeavesExistingFieldsAlone()
    {
        byte[] formed = FormTestDocs.WithTextField("existing", "keep me");
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(new MemoryStream(formed));
        editor.Forms.AddTextField(0, "fresh", Rect);

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        Assert.Equal(2, reopened.Forms.Count);
        Assert.Equal("keep me", Assert.IsType<PdfTextField>(reopened.Forms["existing"]).Value);
        Assert.NotNull(reopened.Forms["fresh"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a.b")]
    public void AddTextField_InvalidName_Throws(string bad)
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        Assert.Throws<ArgumentException>(() => editor.Forms.AddTextField(0, bad, Rect));
        Assert.Empty(editor.Forms); // document unmodified
    }

    [Fact]
    public void AddTextField_DuplicateName_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        editor.Forms.AddTextField(0, "dup", Rect);
        Assert.Throws<ArgumentException>(() => editor.Forms.AddTextField(0, "dup", Rect));
        Assert.Single(editor.Forms);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void AddTextField_BadPageIndex_Throws(int pageIndex)
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.Forms.AddTextField(pageIndex, "n", Rect));
        Assert.Empty(editor.Forms);
    }

    [Fact]
    public void AddTextField_OnDynamicXfa_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenDynamicXfaShell();
        Assert.Throws<InvalidOperationException>(() => editor.Forms.AddTextField(0, "n", Rect));
    }

    [Fact]
    public void AddTextField_ParityWithBuilderField()
    {
        // Spec §4: the authored dict recipe must match what the builder produces for an
        // identical field — same effective type and flags when read back through the tree.
        using PdfDocumentEditor authored = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField mine = authored.Forms.AddTextField(0, "f", new PdfRect(72, 700, 372, 720));

        byte[] built = FormTestDocs.WithTextField("f");
        using PdfDocumentEditor builder = PdfDocumentEditor.Open(new MemoryStream(built));
        var theirs = Assert.IsType<PdfTextField>(builder.Forms["f"]);

        Assert.Equal(theirs.Type, mine.Type);
        Assert.Equal(theirs.IsMultiline, mine.IsMultiline);
        Assert.Equal(theirs.IsReadOnly, mine.IsReadOnly);
        Assert.Equal(theirs.IsRequired, mine.IsRequired);
        Assert.Equal(theirs.Quadding, mine.Quadding);
    }

    [Fact]
    public void AddTextField_OnSecondPage_WidgetLandsThere()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainTwoPages();
        PdfTextField field = editor.Forms.AddTextField(1, "p2", Rect);
        Assert.Equal(1, field.Widgets[0].PageIndex);

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        Assert.Equal(1, reopened.Forms["p2"]!.Widgets[0].PageIndex);
    }
}
