using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;

namespace PdfLibrary.Tests.Editing.Forms;

public class AuthoringRenameGeometryTests
{
    private static readonly PdfRect Rect = new(72, 700, 372, 720);

    [Fact]
    public void Rename_UpdatesNameAndPersists()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "old", Rect);
        field.Value = "keep";
        field.Rename("shiny");

        Assert.Equal("shiny", field.FullName);
        Assert.Equal("shiny", field.PartialName);

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        Assert.Null(reopened.Forms["old"]);
        var back = Assert.IsType<PdfTextField>(reopened.Forms["shiny"]);
        Assert.Equal("keep", back.Value);
    }

    [Fact]
    public void Rename_Collision_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        editor.Forms.AddTextField(0, "a", Rect);
        PdfTextField b = editor.Forms.AddTextField(0, "b", new PdfRect(72, 650, 372, 670));
        Assert.Throws<ArgumentException>(() => b.Rename("a"));
        Assert.Equal("b", b.FullName); // unchanged
    }

    [Theory]
    [InlineData("")]
    [InlineData("x.y")]
    public void Rename_InvalidName_Throws(string bad)
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "a", Rect);
        Assert.Throws<ArgumentException>(() => field.Rename(bad));
    }

    [Fact]
    public void Rename_ToSameName_IsNoOpNotCollision()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "same", Rect);
        field.Rename("same"); // must not throw
        Assert.Equal("same", field.FullName);
    }

    [Fact]
    public void SetWidgetRect_MovesTextField_AndRegeneratesAppearance()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        field.Value = "resized";

        field.SetWidgetRect(0, new PdfRect(100, 500, 500, 540));

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        PdfFormField back = reopened.Forms["t"]!;
        Assert.Equal(100, back.Widgets[0].Rect.Left, 3);
        Assert.Equal(500, back.Widgets[0].Rect.Bottom, 3);
        Assert.Equal(500, back.Widgets[0].Rect.Right, 3);
        Assert.Equal(540, back.Widgets[0].Rect.Top, 3);

        // Regenerated /AP BBox matches the new size (400 x 40).
        PdfDictionary widget = back.WidgetDicts[0];
        var ap = Assert.IsType<PdfDictionary>(
            FormFieldTree.Resolve(back.Doc, widget.Get(new PdfName("AP"))));
        PdfObject? nRaw = FormFieldTree.Resolve(back.Doc, ap.Get(new PdfName("N")));
        var n = Assert.IsType<PdfStream>(nRaw);
        var bbox = Assert.IsType<PdfArray>(n.Dictionary.Get(new PdfName("BBox")));
        Assert.Equal(400, ((PdfReal)bbox[2]).Value, 3);
        Assert.Equal(40, ((PdfReal)bbox[3]).Value, 3);
    }

    [Fact]
    public void SetWidgetRect_Checkbox_RedrawsMarkAtNewSize()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfButtonField cb = editor.Forms.AddCheckbox(0, "cb", new PdfRect(72, 700, 86, 714));
        cb.Check();

        cb.SetWidgetRect(0, new PdfRect(72, 700, 100, 728)); // 28x28

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        var back = Assert.IsType<PdfButtonField>(reopened.Forms["cb"]);
        Assert.True(back.IsChecked); // state survived the redraw
        Assert.Contains("Yes", back.Options); // on-state name survived
        Assert.Equal(100, back.Widgets[0].Rect.Right, 3);
    }

    [Fact]
    public void SetWidgetRect_RadioWidget_MovesOnlyThatWidget()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfButtonField radio = editor.Forms.AddRadioGroup("r", new[]
        {
            new PdfRadioOptionPlacement(0, new PdfRect(72, 700, 86, 714), "A"),
            new PdfRadioOptionPlacement(0, new PdfRect(72, 680, 86, 694), "B")
        });

        radio.SetWidgetRect(1, new PdfRect(200, 680, 214, 694));

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        PdfFormField back = reopened.Forms["r"]!;
        Assert.Equal(72, back.Widgets[0].Rect.Left, 3);   // untouched
        Assert.Equal(200, back.Widgets[1].Rect.Left, 3);  // moved
        Assert.Equal(new[] { "A", "B" }, ((PdfButtonField)back).Options); // states intact
    }

    [Fact]
    public void SetWidgetRect_BadIndex_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        Assert.Throws<ArgumentOutOfRangeException>(() => field.SetWidgetRect(1, Rect));
        Assert.Throws<ArgumentOutOfRangeException>(() => field.SetWidgetRect(-1, Rect));
    }
}
