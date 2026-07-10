using PdfLibrary.Builder;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;

namespace PdfLibrary.Tests.Editing.Forms;

public class AuthoringDropdownTests
{
    private static readonly PdfRect Rect = new(72, 700, 272, 720);
    private static readonly (string, string)[] Opts =
        { ("red", "Red"), ("grn", "Green"), ("blu", "Blue") };

    [Fact]
    public void AddDropdown_CreatesComboWithOptions()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfChoiceField dd = editor.Forms.AddDropdown(0, "color", Rect, Opts);

        Assert.True(dd.IsCombo);
        Assert.False(dd.IsMultiSelect);
        Assert.Equal(3, dd.Options.Count);
        Assert.Equal(("red", "Red"), dd.Options[0]);
        Assert.Empty(dd.SelectedValues);
    }

    [Fact]
    public void AddDropdown_SelectThenSave_RoundTrips()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfChoiceField dd = editor.Forms.AddDropdown(0, "color", Rect, Opts);
        dd.SelectedValues = new[] { "grn" };

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        var back = Assert.IsType<PdfChoiceField>(reopened.Forms["color"]);
        Assert.Equal(new[] { "grn" }, back.SelectedValues);
        Assert.Equal(new[] { 1 }, back.SelectedIndices);
    }

    [Fact]
    public void AddDropdown_SameExportAndDisplay_WritesSingleStringOpt()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfChoiceField dd = editor.Forms.AddDropdown(0, "plain", Rect,
            new[] { ("one", "one"), ("two", "two") });

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        var back = Assert.IsType<PdfChoiceField>(reopened.Forms["plain"]);
        Assert.Equal(("one", "one"), back.Options[0]);
        Assert.Equal(("two", "two"), back.Options[1]);
    }

    [Fact]
    public void AddDropdown_EmptyOptions_Throws_DocumentUnmodified()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        Assert.Throws<ArgumentException>(() =>
            editor.Forms.AddDropdown(0, "dd", Rect, Array.Empty<(string, string)>()));
        Assert.Empty(editor.Forms);
    }

    [Fact]
    public void AddDropdown_OnDynamicXfa_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenDynamicXfaShell();
        Assert.Throws<InvalidOperationException>(() => editor.Forms.AddDropdown(0, "dd", Rect, Opts));
    }
}
