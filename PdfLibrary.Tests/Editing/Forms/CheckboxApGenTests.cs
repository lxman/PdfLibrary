using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing.Forms;

/// <summary>
/// Tests that a checkbox widget without a pre-existing /AP appearance gets one generated
/// when Check() is called, and that a checkbox that already has /AP states does not have
/// its appearance overwritten.
/// </summary>
public class CheckboxApGenTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the /AP /N sub-dictionary from the first widget of <paramref name="field"/>.
    /// Returns null if absent.
    /// </summary>
    private static PdfDictionary? GetApNDict(PdfDocument doc, PdfButtonField field)
    {
        if (field.WidgetDicts.Count == 0) return null;
        PdfDictionary widget = field.WidgetDicts[0];

        PdfObject? apRaw = widget.Get(new PdfName("AP"));
        if (FormFieldTree.Resolve(doc, apRaw) is not PdfDictionary ap) return null;

        PdfObject? nRaw = ap.Get(new PdfName("N"));
        return FormFieldTree.Resolve(doc, nRaw) as PdfDictionary;
    }

    /// <summary>
    /// Returns the object number of the on-state appearance entry inside /AP /N on the first widget.
    /// The /AP /N dict must have at least one non-Off key whose value is an indirect ref.
    /// Returns -1 if no such entry exists.
    /// Used to detect whether the on-state appearance reference changed after Check().
    /// </summary>
    private static long ApNObjectNumber(PdfDocument doc, PdfButtonField field)
    {
        PdfDictionary? nDict = GetApNDict(doc, field);
        if (nDict is null) return -1;

        foreach (KeyValuePair<PdfName, PdfObject> kvp in nDict)
        {
            if (kvp.Key.Value != "Off" && kvp.Value is PdfIndirectReference ir)
                return ir.ObjectNumber;
        }
        return -1;
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Checkbox_WithoutAp_GetsAppearanceGenerated_OnCheck()
    {
        byte[] pdf = FormTestDocs.WithCheckboxNoAp("agree");

        string outPath = Path.GetTempFileName();
        try
        {
            using (PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf)))
            {
                PdfDocumentEditor edit = doc.Edit();
                var field = (PdfButtonField)edit.Forms["agree"]!;

                // Before Check() — no AP should exist
                Assert.Null(GetApNDict(doc, field));

                field.Check();

                // (a) IsChecked must be true
                Assert.True(field.IsChecked);

                // (b) /AP /N must now exist with an on-state key and /Off
                PdfDictionary? nDict = GetApNDict(doc, field);
                Assert.NotNull(nDict);

                // Must have an "Off" key
                bool hasOff = false;
                bool hasOnState = false;
                foreach (KeyValuePair<PdfName, PdfObject> kvp in nDict!)
                {
                    if (kvp.Key.Value == "Off") hasOff = true;
                    else hasOnState = true;
                }
                Assert.True(hasOff, "Expected /AP /N to have an /Off entry.");
                Assert.True(hasOnState, "Expected /AP /N to have a non-Off (on-state) entry.");

                edit.Save(outPath);
            }

            // Reload and verify persisted
            using PdfDocument re = PdfDocument.Load(outPath);
            PdfDocumentEditor reEdit = re.Edit();
            var reField = (PdfButtonField)reEdit.Forms["agree"]!;

            Assert.True(reField.IsChecked, "IsChecked must survive save/reload.");

            PdfDictionary? reNDict = GetApNDict(re, reField);
            Assert.NotNull(reNDict);
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    [Fact]
    public void Checkbox_WithExistingAp_IsNotOverwrittenOnCheck()
    {
        // Use the WithCheckboxWithAp fixture which has an explicit /AP /N dict
        byte[] pdf = FormTestDocs.WithCheckboxWithAp("box");

        using PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf));
        PdfDocumentEditor edit = doc.Edit();
        var field = (PdfButtonField)edit.Forms["box"]!;

        // Confirm /AP /N exists before Check()
        PdfDictionary? nDictBefore = GetApNDict(doc, field);
        Assert.NotNull(nDictBefore);

        // Record the /AP /N object number
        long objNumBefore = ApNObjectNumber(doc, field);
        Assert.True(objNumBefore >= 0, "Fixture should have /AP /N with an indirect ref.");

        field.Check();

        // The /AP /N object reference must be unchanged (not replaced by a synthesised one)
        long objNumAfter = ApNObjectNumber(doc, field);
        Assert.Equal(objNumBefore, objNumAfter);
    }

    [Fact]
    public void Checkbox_WithoutAp_UncheckStillWorks()
    {
        // Uncheck() on a no-AP widget should not throw; AP generation is not mandatory for Off
        byte[] pdf = FormTestDocs.WithCheckboxNoAp("agree2");

        using PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf));
        PdfDocumentEditor edit = doc.Edit();
        var field = (PdfButtonField)edit.Forms["agree2"]!;

        // Should not throw
        Exception? ex = Record.Exception(() => field.Uncheck());
        Assert.Null(ex);
        Assert.False(field.IsChecked);
    }
}
