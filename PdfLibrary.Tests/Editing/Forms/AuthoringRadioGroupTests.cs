using PdfLibrary.Builder;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;

namespace PdfLibrary.Tests.Editing.Forms;

public class AuthoringRadioGroupTests
{
    private static PdfRadioOptionPlacement Opt(string onState, double y, int page = 0) =>
        new(page, new PdfRect(72, y, 86, y + 14), onState);

    [Fact]
    public void AddRadioGroup_CreatesParentWithPerOptionWidgets()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfButtonField radio = editor.Forms.AddRadioGroup("choice",
            new[] { Opt("A", 700), Opt("B", 680), Opt("C", 660) });

        Assert.Equal(ButtonKind.Radio, radio.Kind);
        Assert.Equal(new[] { "A", "B", "C" }, radio.Options);
        Assert.Equal(3, radio.Widgets.Count);
        Assert.Null(radio.SelectedOption);
    }

    [Fact]
    public void AddRadioGroup_SelectThenSave_RoundTrips()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfButtonField radio = editor.Forms.AddRadioGroup("choice",
            new[] { Opt("A", 700), Opt("B", 680) });
        radio.SelectedOption = "B";

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        var back = Assert.IsType<PdfButtonField>(reopened.Forms["choice"]);
        Assert.Equal("B", back.SelectedOption);
    }

    [Fact]
    public void AddRadioGroup_OptionsAcrossPages_WidgetsLandOnTheirPages()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainTwoPages();
        PdfButtonField radio = editor.Forms.AddRadioGroup("span",
            new[] { Opt("P1", 700, page: 0), Opt("P2", 700, page: 1) });

        Assert.Equal(0, radio.Widgets[0].PageIndex);
        Assert.Equal(1, radio.Widgets[1].PageIndex);
    }

    [Fact]
    public void AddRadioGroup_EmptyOptions_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        Assert.Throws<ArgumentException>(() =>
            editor.Forms.AddRadioGroup("r", Array.Empty<PdfRadioOptionPlacement>()));
        Assert.Equal(0, editor.Forms.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Off")]
    public void AddRadioGroup_BadOnState_Throws(string onState)
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        Assert.Throws<ArgumentException>(() =>
            editor.Forms.AddRadioGroup("r", new[] { Opt(onState, 700) }));
        Assert.Equal(0, editor.Forms.Count);
    }

    [Fact]
    public void AddRadioGroup_DuplicateOnStates_Throws_DocumentUnmodified()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        Assert.Throws<ArgumentException>(() =>
            editor.Forms.AddRadioGroup("r", new[] { Opt("A", 700), Opt("A", 680) }));
        Assert.Equal(0, editor.Forms.Count);
    }

    [Fact]
    public void AddRadioGroup_BadPageIndexInAnyOption_Throws_DocumentUnmodified()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            editor.Forms.AddRadioGroup("r", new[] { Opt("A", 700), Opt("B", 680, page: 7) }));
        Assert.Equal(0, editor.Forms.Count);
    }
}
