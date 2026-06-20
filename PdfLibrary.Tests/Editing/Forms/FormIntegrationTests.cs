using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing.Forms;

public class FormIntegrationTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string ApStreamText(PdfDocument doc, PdfTextField field)
    {
        var widget = field.Widgets[0];
        var apRaw = widget.Get(new PdfLibrary.Core.Primitives.PdfName("AP"));
        var ap = FormFieldTree.Resolve(doc, apRaw) as PdfLibrary.Core.Primitives.PdfDictionary;
        Assert.NotNull(ap);

        var nRaw = ap!.Get(new PdfLibrary.Core.Primitives.PdfName("N"));
        var nObj = FormFieldTree.Resolve(doc, nRaw);
        Assert.NotNull(nObj);

        var stream = nObj as PdfLibrary.Core.Primitives.PdfStream;
        if (stream is null && nObj is PdfLibrary.Core.Primitives.PdfIndirectReference ir)
            stream = doc.GetObject(ir.ObjectNumber) as PdfLibrary.Core.Primitives.PdfStream;

        Assert.NotNull(stream);
        return Encoding.ASCII.GetString(stream!.GetDecodedData());
    }

    private static string ApStreamText(PdfDocument doc, PdfChoiceField field)
    {
        var widget = field.Widgets[0];
        var apRaw = widget.Get(new PdfLibrary.Core.Primitives.PdfName("AP"));
        var ap = FormFieldTree.Resolve(doc, apRaw) as PdfLibrary.Core.Primitives.PdfDictionary;
        Assert.NotNull(ap);

        var nRaw = ap!.Get(new PdfLibrary.Core.Primitives.PdfName("N"));
        var nObj = FormFieldTree.Resolve(doc, nRaw);
        Assert.NotNull(nObj);

        var stream = nObj as PdfLibrary.Core.Primitives.PdfStream;
        if (stream is null && nObj is PdfLibrary.Core.Primitives.PdfIndirectReference ir)
            stream = doc.GetObject(ir.ObjectNumber) as PdfLibrary.Core.Primitives.PdfStream;

        Assert.NotNull(stream);
        return Encoding.ASCII.GetString(stream!.GetDecodedData());
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void NonAsciiTextValue_RoundTrips()
    {
        const string nonAsciiValue = "café 日本語";

        byte[] pdf = FormTestDocs.WithTextField("name");
        string outPath = Path.GetTempFileName();
        try
        {
            using (PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf)))
            {
                using PdfDocumentEditor edit = doc.Edit();
                ((PdfTextField)edit.Forms["name"]!).Value = nonAsciiValue;
                edit.Save(outPath);
            }

            using PdfDocument reloaded = PdfDocument.Load(outPath);
            using PdfDocumentEditor reEdit = reloaded.Edit();
            string actual = ((PdfTextField)reEdit.Forms["name"]!).Value;
            Assert.Equal(nonAsciiValue, actual);
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    [Fact]
    public void AppearanceFormatting_IsCultureInvariant()
    {
        CultureInfo savedCulture = CultureInfo.CurrentCulture;

        // ── text field: centered (/Q=2 so Tm x is fractional) ────────────────
        string apDeDE_text;
        string apInvariant_text;

        try
        {
            // Run under de-DE (comma decimal separator)
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            apDeDE_text = FillTextAndGetAp(q: 2, value: "Hi");
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
        }

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            apInvariant_text = FillTextAndGetAp(q: 2, value: "Hi");
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
        }

        // Both AP streams must be identical byte-for-byte
        Assert.Equal(apInvariant_text, apDeDE_text);

        // Must contain a dot-decimal number
        Assert.Matches(new Regex(@"\d+\.\d+"), apDeDE_text);

        // Must NOT contain a comma in a numeric operand position
        // A numeric operand with a comma would look like "1,5" or "0,6" in the stream
        Assert.DoesNotMatch(new Regex(@"\d,\d"), apDeDE_text);

        // ── list-box choice field: highlight-rect coordinate path ─────────────
        string apDeDE_choice;
        string apInvariant_choice;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            apDeDE_choice = FillChoiceAndGetAp(selectedIndex: 1);
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
        }

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            apInvariant_choice = FillChoiceAndGetAp(selectedIndex: 1);
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
        }

        Assert.Equal(apInvariant_choice, apDeDE_choice);

        Assert.Matches(new Regex(@"\d+\.\d+"), apDeDE_choice);
        Assert.DoesNotMatch(new Regex(@"\d,\d"), apDeDE_choice);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string FillTextAndGetAp(int q, string value)
    {
        byte[] pdf = FormTestDocs.WithTextField("f");
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf));
        using PdfDocumentEditor edit = doc.Edit();
        var field = (PdfTextField)edit.Forms["f"]!;

        // Set centered quadding so the Tm x origin is fractional
        field.Dict[new PdfLibrary.Core.Primitives.PdfName("Q")] =
            new PdfLibrary.Core.Primitives.PdfInteger(q);

        field.Value = value;
        return ApStreamText(doc, field);
    }

    private static string FillChoiceAndGetAp(int selectedIndex)
    {
        byte[] pdf = FormTestDocs.WithChoice(
            "ch",
            [("a", "Apple"), ("b", "Banana"), ("c", "Cherry")],
            combo: false);

        using var ms = new MemoryStream(pdf);
        PdfDocument doc = PdfDocument.Load(ms);
        using PdfDocumentEditor edit = doc.Edit();
        var field = (PdfChoiceField)edit.Forms["ch"]!;
        field.SelectedIndices = [selectedIndex];
        return ApStreamText(doc, field);
    }
}
