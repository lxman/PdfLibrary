using PdfLibrary.Builder;
using PdfLibrary.Builder.FormField;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Editing.Forms;

namespace PdfLibrary.Tests;

/// <summary>
/// Regression harness for the PdfRadioGroupBuilder output bug (fixed 2026-06-28).
///
/// FIX (PdfDocumentWriter.cs): the writer now reserves 1 parent + N widget objects
/// for a PdfRadioGroupBuilder, emits the parent as a pure /FT /Btn field dict (with
/// the Radio /Ff bit, /V, and /Kids) into AcroForm /Fields, and emits one
/// /Subtype /Widget annotation per option (with per-option /Rect, /AS, and
/// /AP /N &lt;&lt; /onState /Off &gt;&gt;) into the page /Annots.  The reader then
/// classifies the field as Radio and recovers Options + SelectedOption.
///
/// Tests 1-2 assert the CORRECT post-fix behavior (regression guards); tests 3-4 are
/// the hand-built baselines proving the reader and the set-selected write path were
/// always correct.
/// </summary>
public class RadioGroupBuilderBugRepro(ITestOutputHelper output)
{
    private static byte[] BuildTwoOptionRadio(string? selected = "Option1") =>
        PdfDocumentBuilder.Create()
            .WithAcroForm(f => f.SetNeedAppearances(true))
            .AddPage(p =>
            {
                PdfRadioGroupBuilder g = p.AddRadioGroup("myRadio")
                    .AddOptionInches("Option1", 1.0, 9.0)
                    .AddOptionInches("Option2", 1.0, 8.5);
                if (selected is not null) g.Select(selected);
            })
            .ToByteArray();

    // ── Test 1: builder round-trip reads back as a Radio field ─────────────────

    [Fact]
    public void BuilderRadioGroup_RoundTrip_ReadsAsRadio_WithOptions()
    {
        byte[] pdf = BuildTwoOptionRadio();

        output.WriteLine($"PDF byte length: {pdf.Length}");

        using var ms = new MemoryStream(pdf);
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(ms);

        PdfFormFields forms = editor.Forms;
        output.WriteLine($"Field count: {forms.Count}");

        foreach (PdfFormField f in forms)
        {
            output.WriteLine(
                $"  name={f.FullName,-20} type={f.Type,-12} clrType={f.GetType().Name}");
        }

        PdfFormField? field = forms["myRadio"];
        Assert.NotNull(field);

        // FIXED: /FT /Btn with the Radio flag is now emitted, so the reader returns a
        // PdfButtonField with Kind == Radio.
        Assert.Equal(PdfFormFieldType.Radio, field!.Type);
        var button = Assert.IsType<PdfButtonField>(field);
        Assert.Equal(ButtonKind.Radio, button.Kind);

        // Options come from each widget's /AP /N keys (export values).
        Assert.Equal(new[] { "Option1", "Option2" }, button.Options.ToArray());

        // Selected option is read back from the parent /V.
        Assert.Equal("Option1", button.SelectedOption);

        // One widget per option.
        Assert.Equal(2, button.Widgets.Count);
    }

    // ── Test 2: structural detail — parent + one widget per option ─────────────

    [Fact]
    public void BuilderRadioGroup_EmitsParentPlusWidgets()
    {
        byte[] pdf = BuildTwoOptionRadio();

        string raw = System.Text.Encoding.Latin1.GetString(pdf);
        output.WriteLine("=== RAW PDF EXCERPT (first 2000 chars) ===");
        output.WriteLine(raw[..Math.Min(2000, raw.Length)]);

        // /FT /Btn now present on the parent field dict.
        Assert.Contains("/FT /Btn", raw);

        // /Kids appears twice: the pages tree AND the parent radio field.
        int kidsCount = CountOccurrences(raw, "/Kids");
        output.WriteLine($"/Kids occurrence count: {kidsCount}");
        Assert.Equal(2, kidsCount);

        // /AP appearance dictionaries are emitted on the widgets.
        Assert.Contains("/AP", raw);
        Assert.Contains("/Option1", raw);
        Assert.Contains("/Option2", raw);
        Assert.Contains("/Off", raw);

        // Radio /Ff bit (32768; optionally | 16384 for NoToggleToOff).
        bool hasRadioFf = raw.Contains("/Ff 32768") || raw.Contains("/Ff 49152");
        output.WriteLine($"/Ff with Radio bit present: {hasRadioFf}");
        Assert.True(hasRadioFf, "Expected the Radio /Ff bit to be emitted on the parent field");

        // Per-option rects are used, NOT the zero base rect.
        Assert.DoesNotContain("/Rect [0.00 0.00 0.00 0.00]", raw);

        // Object count grows: catalog, pages, info, font, page, content, acroform,
        // + parent + 2 widgets = 10 objects minimum.
        int objCount = CountOccurrences(raw, " 0 obj\n");
        output.WriteLine($"Total PDF objects: {objCount}");
        Assert.True(objCount >= 10,
            $"Expected at least 10 objects (got {objCount}): parent + 2 widgets must be emitted");
    }

    // ── Test 2b: each option can be selected and persists on re-save ───────────

    [Theory]
    [InlineData("Option1")]
    [InlineData("Option2")]
    public void BuilderRadioGroup_SelectEachOption_RoundTrips(string option)
    {
        byte[] pdf = BuildTwoOptionRadio(selected: option);

        using var ms = new MemoryStream(pdf);
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(ms);

        var field = editor.Forms["myRadio"] as PdfButtonField;
        Assert.NotNull(field);
        Assert.Equal(option, field!.SelectedOption);

        // Flip to the other option, save, reload, confirm it persists.
        string other = option == "Option1" ? "Option2" : "Option1";
        byte[] modified = FillAndSave(pdf, forms =>
        {
            var f = (PdfButtonField)forms["myRadio"]!;
            f.SelectedOption = other;
        });

        using var ms2 = new MemoryStream(modified);
        using PdfDocumentEditor editor2 = PdfDocumentEditor.Open(ms2);
        var reread = editor2.Forms["myRadio"] as PdfButtonField;
        Assert.NotNull(reread);
        Assert.Equal(other, reread!.SelectedOption);
    }

    // ── Test 2d: widgets carry real Form-XObject appearance streams ────────────

    [Fact]
    public void BuilderRadioGroup_EmitsRealAppearanceStreams()
    {
        byte[] pdf = BuildTwoOptionRadio();
        string raw = System.Text.Encoding.Latin1.GetString(pdf);

        // Real Form XObject appearance streams are emitted (drawn circles), not the old
        // empty-placeholder sub-dicts that left Chrome drawing squares and Adobe flickering.
        Assert.Contains("/Subtype /Form", raw);
        Assert.Contains("/BBox", raw);

        // The /AP /N on-states reference INDIRECT appearance objects, so no inline empty dicts.
        Assert.DoesNotContain("/N << /Option1 << >>", raw);
        Assert.DoesNotContain("/Option1 << >>", raw);

        // The on-state appearance fills a shape (circle dot) — fill operator present.
        Assert.Contains("\nf\n", raw);

        // No rectangular /BS border on radio widgets — the circle IS the border. A /BS << /S /S >>
        // would make viewers draw a square box around the widget rect (the "purplish square").
        Assert.DoesNotContain("/BS", raw);

        // Round-trip still classifies as Radio with both options + selection intact.
        using var ms = new MemoryStream(pdf);
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(ms);
        var radio = editor.Forms["myRadio"] as PdfButtonField;
        Assert.NotNull(radio);
        Assert.Equal(ButtonKind.Radio, radio!.Kind);
        Assert.Equal(new[] { "Option1", "Option2" }, radio.Options.ToArray());
        Assert.Equal("Option1", radio.SelectedOption);
    }

    // ── Test 2c: radio coexists with another field on the same page ────────────

    [Fact]
    public void BuilderRadioGroup_AlongsideTextField_BothReadCorrectly()
    {
        // A text field BEFORE the radio group: the writer must still align each field's
        // widget object numbers correctly after the radio group expands into 1 parent + N widgets.
        byte[] pdf = PdfDocumentBuilder.Create()
            .WithAcroForm(f => f.SetNeedAppearances(true))
            .AddPage(p =>
            {
                p.AddTextField("name", 72, 700, 300, 20).Value("hello");
                p.AddRadioGroup("choice")
                    .AddOptionInches("A", 1.0, 9.0)
                    .AddOptionInches("B", 1.0, 8.5)
                    .Select("B");
            })
            .ToByteArray();

        using var ms = new MemoryStream(pdf);
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(ms);

        var text = editor.Forms["name"] as PdfTextField;
        Assert.NotNull(text);
        Assert.Equal("hello", text!.Value);

        var radio = editor.Forms["choice"] as PdfButtonField;
        Assert.NotNull(radio);
        Assert.Equal(ButtonKind.Radio, radio!.Kind);
        Assert.Equal(new[] { "A", "B" }, radio.Options.ToArray());
        Assert.Equal("B", radio.SelectedOption);
        Assert.Equal(2, radio.Widgets.Count);
    }

    // ── Test 3: baseline — hand-built correct structure reads fine ─────────────

    [Fact]
    public void HandBuilt_RadioGroup_Baseline_ReadsCorrectly()
    {
        // Prove the reader is correct; only the builder output is broken.
        byte[] pdf = FormTestDocs.WithRadioGroup(
            "myRadio",
            new[] { "Option1", "Option2" },
            selected: "Option1");

        using var ms = new MemoryStream(pdf);
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(ms);

        var field = editor.Forms["myRadio"] as PdfButtonField;

        Assert.NotNull(field);
        Assert.Equal(PdfFormFieldType.Radio, field!.Type);
        Assert.Equal(ButtonKind.Radio, field.Kind);
        Assert.Equal(new[] { "Option1", "Option2" }, field.Options.ToArray());
        Assert.Equal("Option1", field.SelectedOption);
        Assert.Equal(2, field.Widgets.Count);
    }

    // ── Test 4: set selected option on hand-built — confirms write path works ──

    [Fact]
    public void HandBuilt_RadioGroup_SetSelectedOption_RoundTrips()
    {
        byte[] pdf = FormTestDocs.WithRadioGroup(
            "myRadio",
            new[] { "Option1", "Option2" },
            selected: "Option1");

        // Change selected option to Option2, save, reload.
        byte[] modified = FillAndSave(pdf, forms =>
        {
            var f = (PdfButtonField)forms["myRadio"]!;
            f.SelectedOption = "Option2";
        });

        using var ms = new MemoryStream(modified);
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(ms);

        var field = editor.Forms["myRadio"] as PdfButtonField;
        Assert.NotNull(field);
        Assert.Equal("Option2", field!.SelectedOption);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static byte[] FillAndSave(byte[] pdf, Action<PdfFormFields> act)
    {
        string tmp = Path.GetTempFileName();
        try
        {
            using var ms = new MemoryStream(pdf);
            PdfDocument doc = PdfDocument.Load(ms);
            using PdfDocumentEditor edit = doc.Edit();
            act(edit.Forms);
            edit.Save(tmp);
            return File.ReadAllBytes(tmp);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }
}
