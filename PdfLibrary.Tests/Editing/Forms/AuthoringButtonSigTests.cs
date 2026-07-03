using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;

namespace PdfLibrary.Tests.Editing.Forms;

public class AuthoringButtonSigTests
{
    private static readonly PdfRect CheckRect = new(72, 700, 86, 714);
    private static readonly PdfRect SigRect = new(72, 600, 272, 660);

    [Fact]
    public void AddCheckbox_CreatesUnchecked_WithYesOffAppearances()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfButtonField cb = editor.Forms.AddCheckbox(0, "cb1", CheckRect);

        Assert.Equal(ButtonKind.Checkbox, cb.Kind);
        Assert.False(cb.IsChecked);
        Assert.Contains("Yes", cb.Options);

        // The widget has /AP /N with both the on-state and /Off.
        PdfDictionary widget = cb.WidgetDicts[0];
        var ap = Assert.IsType<PdfDictionary>(widget.Get(new PdfName("AP")));
        var n = Assert.IsType<PdfDictionary>(ap.Get(new PdfName("N")));
        Assert.True(n.ContainsKey(new PdfName("Yes")));
        Assert.True(n.ContainsKey(new PdfName("Off")));
    }

    [Fact]
    public void AddCheckbox_CheckThenSave_RoundTrips()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfButtonField cb = editor.Forms.AddCheckbox(0, "cb1", CheckRect);
        cb.Check();

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        var back = Assert.IsType<PdfButtonField>(reopened.Forms["cb1"]);
        Assert.True(back.IsChecked);
    }

    [Fact]
    public void AddCheckbox_UncheckedSurvivesRoundTrip()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        editor.Forms.AddCheckbox(0, "cb1", CheckRect);

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        Assert.False(Assert.IsType<PdfButtonField>(reopened.Forms["cb1"]).IsChecked);
    }

    [Fact]
    public void AddSignatureField_CreatesUnsignedPlaceholder()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfSignatureField sig = editor.Forms.AddSignatureField(0, "sig1", SigRect);
        Assert.False(sig.IsSigned);

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        var back = Assert.IsType<PdfSignatureField>(reopened.Forms["sig1"]);
        Assert.False(back.IsSigned);
        Assert.Equal(72, back.Widgets[0].Rect.Left, 3);
    }

    [Fact]
    public void AddCheckbox_DuplicateName_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        editor.Forms.AddCheckbox(0, "dup", CheckRect);
        Assert.Throws<ArgumentException>(() => editor.Forms.AddCheckbox(0, "dup", CheckRect));
    }

    [Fact]
    public void AddCheckbox_OnDynamicXfa_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenDynamicXfaShell();
        Assert.Throws<InvalidOperationException>(() => editor.Forms.AddCheckbox(0, "cb", CheckRect));
        Assert.Throws<InvalidOperationException>(() => editor.Forms.AddSignatureField(0, "sig", SigRect));
    }
}
