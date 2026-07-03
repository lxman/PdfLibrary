using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Editing.Forms;

/// <summary>
/// Final-review F2: all authoring entry points on PdfFormField must refuse to mutate a dynamic
/// XFA form, matching the guard already applied to PdfFormFields.Add*/Remove and Forms.Flatten.
/// </summary>
public class AuthoringXfaMutationGuardTests
{
    private const string FieldName = "txt1";

    /// <summary>
    /// A dynamic-XFA shell (mirrors AuthoringTestHelper.OpenDynamicXfaShell) with one text field
    /// dict inserted directly into /AcroForm /Fields via internals. The field has no widget/Rect
    /// on any page, so it does not flip FormFlattener.IsDynamicXfa's determination (which only
    /// cares whether any field has a positioned widget).
    /// </summary>
    private static PdfDocumentEditor OpenDynamicXfaShellWithField()
    {
        byte[] simple = PdfDocumentBuilder.Create().AddPage(_ => { }).ToByteArray();
        var doc = PdfDocument.Load(new MemoryStream(simple));

        var fieldDict = new PdfDictionary
        {
            [new PdfName("FT")] = new PdfName("Tx"),
            [new PdfName("T")] = PdfString.FromText(FieldName)
        };
        PdfIndirectReference fieldRef = doc.RegisterObject(fieldDict);

        var fields = new PdfArray { fieldRef };
        var acro = new PdfDictionary
        {
            [new PdfName("Fields")] = fields,
            [new PdfName("XFA")] = PdfString.FromText(
                "<xdp:xdp xmlns:xdp=\"http://ns.adobe.com/xdp/\"><template/></xdp:xdp>")
        };
        doc.CatalogDictionary![new PdfName("AcroForm")] = acro;
        return doc.Edit();
    }

    [Fact]
    public void Fixture_StillReadsAsDynamicXfa()
    {
        using PdfDocumentEditor editor = OpenDynamicXfaShellWithField();
        Assert.True(editor.Forms.IsDynamicXfa,
            "a widget-less/rect-less skeleton field must not flip the dynamic-XFA determination");
        Assert.NotNull(editor.Forms[FieldName]);
    }

    [Fact]
    public void Rename_OnDynamicXfa_Throws_AndLeavesDictUnchanged()
    {
        using PdfDocumentEditor editor = OpenDynamicXfaShellWithField();
        PdfFormField field = editor.Forms[FieldName]!;
        PdfObject? tBefore = field.Dict.Get(new PdfName("T"));

        Assert.Throws<InvalidOperationException>(() => field.Rename("renamed"));

        Assert.Equal(tBefore, field.Dict.Get(new PdfName("T")));
        Assert.Equal(FieldName, field.FullName);
    }

    [Fact]
    public void SetWidgetRect_OnDynamicXfa_Throws_AndLeavesDictUnchanged()
    {
        using PdfDocumentEditor editor = OpenDynamicXfaShellWithField();
        PdfFormField field = editor.Forms[FieldName]!;
        PdfObject? rectBefore = field.Dict.Get(new PdfName("Rect"));

        Assert.Throws<InvalidOperationException>(() => field.SetWidgetRect(0, new PdfRect(0, 0, 10, 10)));

        Assert.Equal(rectBefore, field.Dict.Get(new PdfName("Rect")));
    }

    [Fact]
    public void SetIsReadOnly_OnDynamicXfa_Throws_AndLeavesStateUnchanged()
    {
        using PdfDocumentEditor editor = OpenDynamicXfaShellWithField();
        PdfFormField field = editor.Forms[FieldName]!;
        bool before = field.IsReadOnly;

        Assert.Throws<InvalidOperationException>(() => field.IsReadOnly = true);

        Assert.Equal(before, field.IsReadOnly);
        Assert.False(field.Dict.ContainsKey(new PdfName("Ff")));
    }

    [Fact]
    public void SetFontName_OnDynamicXfa_Throws_AndLeavesStateUnchanged()
    {
        using PdfDocumentEditor editor = OpenDynamicXfaShellWithField();
        PdfFormField field = editor.Forms[FieldName]!;
        string before = field.FontName;
        PdfObject? daBefore = field.Dict.Get(new PdfName("DA"));

        Assert.Throws<InvalidOperationException>(() => field.FontName = "Cour");

        Assert.Equal(before, field.FontName);
        Assert.Equal(daBefore, field.Dict.Get(new PdfName("DA")));
    }

    [Fact]
    public void SetMaxLength_OnDynamicXfa_Throws_AndLeavesStateUnchanged()
    {
        using PdfDocumentEditor editor = OpenDynamicXfaShellWithField();
        var field = Assert.IsType<PdfTextField>(editor.Forms[FieldName]);
        int? before = field.MaxLength;

        Assert.Throws<InvalidOperationException>(() => field.MaxLength = 5);

        Assert.Equal(before, field.MaxLength);
        Assert.False(field.Dict.ContainsKey(new PdfName("MaxLen")));
    }
}
