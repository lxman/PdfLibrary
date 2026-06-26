using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing.Forms;

public class TextAppearanceTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Decodes the /AP /N stream of the first widget of the given field to a string.
    /// </summary>
    private static string ApStreamText(PdfDocument doc, PdfTextField field)
    {
        // Widgets is always populated; for a single-widget field Widgets[0] is the widget dict.
        // The field dict may itself be the widget (no-Kids case) — in both cases field.Widgets[0] works.
        PdfDictionary widget = field.Widgets[0];

        // /AP /N → stream
        PdfObject? apRaw = widget.Get(new PdfLibrary.Core.Primitives.PdfName("AP"));
        var ap = FormFieldTree.Resolve(doc, apRaw) as PdfLibrary.Core.Primitives.PdfDictionary;
        Assert.NotNull(ap);

        PdfObject? nRaw = ap!.Get(new PdfLibrary.Core.Primitives.PdfName("N"));
        PdfObject? nObj = FormFieldTree.Resolve(doc, nRaw);
        Assert.NotNull(nObj);

        // nObj should be a PdfIndirectReference resolved stream
        var stream = nObj as PdfLibrary.Core.Primitives.PdfStream;
        if (stream is null && nObj is PdfLibrary.Core.Primitives.PdfIndirectReference ir)
            stream = doc.GetObject(ir.ObjectNumber) as PdfLibrary.Core.Primitives.PdfStream;

        Assert.NotNull(stream);
        return Encoding.ASCII.GetString(stream!.GetDecodedData());
    }

    /// <summary>
    /// Reads /AcroForm /NeedAppearances from the document.
    /// Returns true if set to true, false otherwise (including missing).
    /// </summary>
    private static bool NeedAppearances(PdfDocument doc)
    {
        PdfDictionary? catalog = doc.CatalogDictionary;
        if (catalog is null) return false;

        PdfObject? acroRaw = catalog.Get(new PdfLibrary.Core.Primitives.PdfName("AcroForm"));
        var acro = FormFieldTree.Resolve(doc, acroRaw) as PdfLibrary.Core.Primitives.PdfDictionary;
        if (acro is null) return false;

        PdfObject? naRaw = acro.Get(new PdfLibrary.Core.Primitives.PdfName("NeedAppearances"));
        if (naRaw is PdfLibrary.Core.Primitives.PdfBoolean b) return (bool)b;
        return false;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SetText_GeneratesAppearance_AndRoundTrips()
    {
        byte[] pdf = FormTestDocs.WithTextField("name");
        string outPath = Path.GetTempFileName();
        try
        {
            using (PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf)))
            {
                PdfDocumentEditor edit = doc.Edit();
                ((PdfTextField)edit.Forms["name"]!).Value = "Hello";
                edit.Save(outPath);
            }

            using PdfDocument re = PdfDocument.Load(outPath);
            var f = (PdfTextField)re.Edit().Forms["name"]!;
            Assert.Equal("Hello", f.Value);
            string ap = ApStreamText(re, f);
            Assert.Contains("/Tx BMC", ap);
            Assert.Contains("(Hello)", ap);
            Assert.False(NeedAppearances(re));
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    [Fact]
    public void Quadding_Right_ShiftsOrigin_RightOfLeft()
    {
        // Build a left-aligned copy (/Q=0)
        byte[] pdfLeft = FormTestDocs.WithTextField("field");

        // Build a right-aligned copy (/Q=2) — we set it on the field dict directly in memory
        byte[] pdfRight = FormTestDocs.WithTextField("field");

        double txLeft = GetTmX(pdfLeft, "field", q: 0);
        double txRight = GetTmX(pdfRight, "field", q: 2);

        // The right-aligned origin should be to the right of the left-aligned origin
        // for a non-empty value (the text isn't the full width of the field).
        Assert.True(txRight > txLeft,
            $"Expected right-aligned Tm x ({txRight}) > left-aligned Tm x ({txLeft})");
    }

    [Fact]
    public void AutoSize_ZeroDa_ProducesNonZeroTfSize()
    {
        // WithTextField uses /DA = "/Helv 12 Tf 0 g" by the builder, so we patch the /DA
        // to have size 0 to exercise auto-size.
        byte[] pdf = FormTestDocs.WithTextField("txt");

        using PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf));
        PdfDocumentEditor edit = doc.Edit();
        var field = (PdfTextField)edit.Forms["txt"]!;

        // Force /DA size to 0 by overwriting it on the field dict
        field.Dict[new PdfLibrary.Core.Primitives.PdfName("DA")] =
            PdfLibrary.Core.Primitives.PdfString.FromText("/Helv 0 Tf 0 g");

        field.Value = "AutoSize";

        string ap = ApStreamText(doc, field);
        double size = ExtractTfSize(ap);
        Assert.True(size > 0, $"Expected auto-size > 0 but got {size}");
        Assert.True(size <= 12, $"Expected auto-size <= 12 but got {size}");
    }

    [Fact]
    public void InvariantCulture_NumbersUseDecimalPoint()
    {
        // Change the current culture to one that uses comma as decimal separator.
        // The AP stream must still use dot as decimal.
        CultureInfo savedCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            byte[] pdf = FormTestDocs.WithTextField("inv");
            using PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf));
            PdfDocumentEditor edit = doc.Edit();
            var field = (PdfTextField)edit.Forms["inv"]!;
            // Force a fractional font size to exercise number formatting
            field.Dict[new PdfLibrary.Core.Primitives.PdfName("DA")] =
                PdfLibrary.Core.Primitives.PdfString.FromText("/Helv 10.5 Tf 0 g");
            field.Value = "Test";

            string ap = ApStreamText(doc, field);
            // The Tm line should contain dot-separated decimals, not commas
            Assert.DoesNotContain(",", ap, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Fills a fresh copy of the fixture with value "Hi", setting /Q to the given quadding,
    /// then extracts the Tm x-origin from the AP content stream.
    /// </summary>
    private static double GetTmX(byte[] pdf, string fieldName, int q)
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf));
        PdfDocumentEditor edit = doc.Edit();
        var field = (PdfTextField)edit.Forms[fieldName]!;

        // Set the desired quadding directly
        field.Dict[new PdfLibrary.Core.Primitives.PdfName("Q")] =
            new PdfLibrary.Core.Primitives.PdfInteger(q);

        field.Value = "Hi";

        string ap = ApStreamText(doc, field);
        return ExtractTmX(ap);
    }

    /// <summary>Parses the "1 0 0 1 TX TY Tm" line and returns TX.</summary>
    private static double ExtractTmX(string apContent)
    {
        // Pattern: "1 0 0 1 <tx> <ty> Tm"
        Match m = Regex.Match(apContent, @"1 0 0 1 ([\d.]+) ([\d.]+) Tm");
        Assert.True(m.Success, $"Could not find Tm in AP stream:\n{apContent}");
        return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>Parses the "/<name> <size> Tf" line and returns the size.</summary>
    private static double ExtractTfSize(string apContent)
    {
        Match m = Regex.Match(apContent, @"/\w+ ([\d.]+) Tf");
        Assert.True(m.Success, $"Could not find Tf in AP stream:\n{apContent}");
        return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }
}
