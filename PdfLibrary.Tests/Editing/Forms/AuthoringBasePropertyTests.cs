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

    [Fact]
    public void SetFlagBit_OnHierarchicalField_PreservesInheritedFfBits()
    {
        // Final-review F1 regression: the terminal field "grp.r1" has NO own /Ff — it inherits
        // Radio+NoToggleToOff from its grandparent. Setting IsRequired must seed the read-modify-
        // write from the EFFECTIVE (inherited) /Ff, not from an absent own /Ff defaulting to 0 —
        // otherwise the materialized own /Ff shadows the inherited Radio bit and the field reads
        // back as a Checkbox.
        byte[] formed = FormTestDocs.WithHierarchicalRadioField("grp", "r1", "Yes");
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(new MemoryStream(formed));

        // Establish the inherited /Ff is read correctly BEFORE any mutation.
        var before = Assert.IsType<PdfButtonField>(editor.Forms["grp.r1"]);
        Assert.Equal(ButtonKind.Radio, before.Kind);

        before.IsRequired = true;

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        var after = Assert.IsType<PdfButtonField>(reopened.Forms["grp.r1"]);
        Assert.Equal(ButtonKind.Radio, after.Kind); // must NOT have degraded to Checkbox
        Assert.True(after.IsRequired);
    }
}
