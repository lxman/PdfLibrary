using PdfLibrary.Builder;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;
using Xunit;

namespace PdfLibrary.Tests.Editing.Forms;

public class FieldWidgetTests
{
    [Fact]
    public void Widgets_ExposeRectPageAndOnState()
    {
        // Build a single-page form with a text field at a known rect and a checkbox with an on-state.
        using PdfDocumentEditor editor = FormTestDocs.SingleTextAndCheckbox(
            textRect: new PdfRect(100, 700, 300, 720),
            checkRect: new PdfRect(100, 660, 116, 676),
            checkOnState: "Yes");

        PdfFormField text = editor.Forms["text1"]!;
        PdfFieldWidget tw = Assert.Single(text.Widgets);
        Assert.Equal(0, tw.PageIndex);
        Assert.Equal(100.0, tw.Rect.Left, 1);
        Assert.Equal(700.0, tw.Rect.Bottom, 1);
        Assert.Equal(300.0, tw.Rect.Right, 1);
        Assert.Equal(720.0, tw.Rect.Top, 1);
        Assert.Null(tw.OnStateName);   // text field: no on-state
        Assert.Same(text, tw.Field);

        PdfFormField check = editor.Forms["check1"]!;
        PdfFieldWidget cw = Assert.Single(check.Widgets);
        Assert.Equal(0, cw.PageIndex);
        Assert.Equal("Yes", cw.OnStateName);  // checkbox on-state
        Assert.Same(check, cw.Field);
    }
}
