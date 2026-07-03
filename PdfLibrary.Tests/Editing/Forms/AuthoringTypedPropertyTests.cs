using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;

namespace PdfLibrary.Tests.Editing.Forms;

public class AuthoringTypedPropertyTests
{
    private static readonly PdfRect Rect = new(72, 640, 372, 720);

    [Fact]
    public void SetMaxLength_WritesAndClearsMaxLen()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        field.MaxLength = 10;

        using PdfDocumentEditor mid = AuthoringTestHelper.SaveAndReopen(editor);
        var back = Assert.IsType<PdfTextField>(mid.Forms["t"]);
        Assert.Equal(10, back.MaxLength);

        back.MaxLength = null;
        using PdfDocumentEditor final = AuthoringTestHelper.SaveAndReopen(mid);
        Assert.Null(Assert.IsType<PdfTextField>(final.Forms["t"]).MaxLength);
    }

    [Fact]
    public void SetMultiline_PersistsFlag_AndValueStillFills()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        field.IsMultiline = true;
        field.Value = "line one\nline two";

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        var back = Assert.IsType<PdfTextField>(reopened.Forms["t"]);
        Assert.True(back.IsMultiline);
        Assert.Equal("line one\nline two", back.Value);
    }

    [Fact]
    public void SetQuadding_Persists()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        field.Quadding = 2;

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        Assert.Equal(2, Assert.IsType<PdfTextField>(reopened.Forms["t"]).Quadding);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void SetQuadding_OutOfRange_Throws(int bad)
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        Assert.Throws<ArgumentOutOfRangeException>(() => field.Quadding = bad);
    }

    [Fact]
    public void SetMaxLength_Negative_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        Assert.Throws<ArgumentOutOfRangeException>(() => field.MaxLength = -5);
    }

    [Fact]
    public void SetOptions_RewritesOpt_AndDropsStaleSelection()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfChoiceField dd = editor.Forms.AddDropdown(0, "dd", new PdfRect(72, 700, 272, 720),
            new[] { ("a", "A"), ("b", "B") });
        dd.SelectedValues = new[] { "b" };

        dd.Options = new[] { ("a", "A"), ("c", "C") }; // "b" no longer exists

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        var back = Assert.IsType<PdfChoiceField>(reopened.Forms["dd"]);
        Assert.Equal(2, back.Options.Count);
        Assert.Equal(("c", "C"), back.Options[1]);
        Assert.Empty(back.SelectedValues); // stale selection dropped
    }

    [Fact]
    public void SetOptions_KeepsSurvivingSelection()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfChoiceField dd = editor.Forms.AddDropdown(0, "dd", new PdfRect(72, 700, 272, 720),
            new[] { ("a", "A"), ("b", "B") });
        dd.SelectedValues = new[] { "a" };

        dd.Options = new[] { ("a", "Renamed A"), ("c", "C") };

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        var back = Assert.IsType<PdfChoiceField>(reopened.Forms["dd"]);
        Assert.Equal(new[] { "a" }, back.SelectedValues);
        Assert.Equal(new[] { 0 }, back.SelectedIndices);
    }

    [Fact]
    public void SetOptions_Empty_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfChoiceField dd = editor.Forms.AddDropdown(0, "dd", new PdfRect(72, 700, 272, 720),
            new[] { ("a", "A") });
        Assert.Throws<ArgumentException>(() => dd.Options = Array.Empty<(string, string)>());
    }
}
