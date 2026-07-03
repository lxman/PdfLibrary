using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;

namespace PdfLibrary.Tests.Editing.Forms;

public class AuthoringBasePropertyTests
{
    private static readonly PdfRect Rect = new(72, 700, 372, 720);

    [Fact]
    public void SetReadOnlyAndRequired_PersistAsFlagBits()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        field.IsReadOnly = true;
        field.IsRequired = true;

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        PdfFormField back = reopened.Forms["t"]!;
        Assert.True(back.IsReadOnly);
        Assert.True(back.IsRequired);
    }

    [Fact]
    public void ClearReadOnly_ClearsOnlyThatBit()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        field.IsReadOnly = true;
        field.IsRequired = true;
        field.IsReadOnly = false;

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        PdfFormField back = reopened.Forms["t"]!;
        Assert.False(back.IsReadOnly);
        Assert.True(back.IsRequired);
    }

    [Fact]
    public void SetFont_RewritesDa_AndPersists()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        field.Value = "styled";
        field.FontName = "Cour";
        field.FontSize = 14;

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        PdfFormField back = reopened.Forms["t"]!;
        Assert.Equal("Cour", back.FontName);
        Assert.Equal(14, back.FontSize, 3);
        Assert.Equal("styled", Assert.IsType<PdfTextField>(back).Value);
    }

    [Fact]
    public void SetFontName_Unknown_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        Assert.Throws<ArgumentException>(() => field.FontName = "ComicSans");
        Assert.Equal("Helv", field.FontName); // unchanged
    }

    [Fact]
    public void SetFontSize_Negative_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        Assert.Throws<ArgumentOutOfRangeException>(() => field.FontSize = -1);
    }

    [Fact]
    public void ReadingAFormedDoc_DoesNotWriteFlagOrDaEntries()
    {
        // Regression: the read path must use the internal setters — reading a field whose
        // flags come from an inherited /Ff must NOT materialize /Ff or /DA on the field dict.
        byte[] formed = FormTestDocs.WithTextField("plain");
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(new MemoryStream(formed));
        PdfFormField field = editor.Forms["plain"]!;
        _ = field.IsReadOnly;
        _ = field.FontName;
        // The builder-produced field dict has no /Ff; enumeration must not add one.
        Assert.False(field.Dict.ContainsKey(new PdfName("Ff")));
    }
}
