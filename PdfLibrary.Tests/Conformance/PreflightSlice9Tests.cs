using System;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 9 of the preflight: the PDF/X-4 structural core (ISO 15930-7) — GTS_PDFX output intent
/// (<see cref="PdfxOutputIntentRule"/>), TrimBox/ArtBox page geometry (<see cref="PageBoxesRule"/>), and
/// the /Trapped flag (<see cref="TrappedRule"/>). Rule-level tests over hand-built documents; the real
/// GOS PDF/X-4 files back the false-positive side (<see cref="GwgGosPassOracleTests"/>).
/// </summary>
public class PreflightSlice9Tests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfArray Rect(int x0, int y0, int x1, int y1) =>
        new(new PdfInteger(x0), new PdfInteger(y0), new PdfInteger(x1), new PdfInteger(y1));

    /// <summary>One-page document (page has a 612×792 MediaBox); <paramref name="configure"/> tweaks the
    /// doc, catalog and page dict before they are wired up.</summary>
    private static PdfDocument Doc(Action<PdfDocument, PdfDictionary, PdfDictionary> configure)
    {
        var doc = new PdfDocument();
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
        };
        var catalog = new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) };
        configure(doc, catalog, page);
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(3)),
            [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    private static ConformanceContext Ctx(PdfDocument doc) => new(doc, ConformanceProfile.PdfX4);

    private static PdfDictionary OutputIntent(string s) => new() { [N("Type")] = N("OutputIntent"), [N("S")] = N(s) };

    // ── PdfxOutputIntentRule ─────────────────────────────────────────────────

    [Fact]
    public void Missing_pdfx_output_intent_is_flagged()
    {
        var doc = Doc((_, _, _) => { });
        Assert.Single(new PdfxOutputIntentRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Single_pdfx_output_intent_with_profile_passes()
    {
        var doc = Doc((d, c, _) =>
        {
            d.AddObject(10, 0, new PdfStream(new PdfDictionary(), new byte[] { 1 }));
            var intent = OutputIntent("GTS_PDFX");
            intent[N("DestOutputProfile")] = Ref(10);
            c[N("OutputIntents")] = new PdfArray(intent);
        });
        Assert.Empty(new PdfxOutputIntentRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Pdfx_output_intent_without_profile_or_condition_is_flagged()
    {
        var doc = Doc((_, c, _) => c[N("OutputIntents")] = new PdfArray(OutputIntent("GTS_PDFX")));
        Assert.Single(new PdfxOutputIntentRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Multiple_pdfx_output_intents_are_flagged()
    {
        var doc = Doc((d, c, _) =>
        {
            d.AddObject(10, 0, new PdfStream(new PdfDictionary(), new byte[] { 1 }));
            PdfDictionary Intent()
            {
                var i = OutputIntent("GTS_PDFX");
                i[N("DestOutputProfile")] = Ref(10);
                return i;
            }
            c[N("OutputIntents")] = new PdfArray(Intent(), Intent());
        });
        Assert.Contains(new PdfxOutputIntentRule().Check(Ctx(doc)), f => f.Message.Contains("exactly one"));
    }

    // ── PageBoxesRule ────────────────────────────────────────────────────────

    [Fact]
    public void Page_without_trim_or_art_box_is_flagged()
    {
        var doc = Doc((_, _, _) => { });
        Assert.Single(new PageBoxesRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Page_with_trim_box_within_media_box_passes()
    {
        var doc = Doc((_, _, p) => p[N("TrimBox")] = Rect(10, 10, 602, 782));
        Assert.Empty(new PageBoxesRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Page_with_both_trim_and_art_box_is_flagged()
    {
        var doc = Doc((_, _, p) =>
        {
            p[N("TrimBox")] = Rect(10, 10, 602, 782);
            p[N("ArtBox")] = Rect(20, 20, 592, 772);
        });
        Assert.Contains(new PageBoxesRule().Check(Ctx(doc)), f => f.Message.Contains("both"));
    }

    [Fact]
    public void Page_with_trim_box_beyond_media_box_is_flagged()
    {
        var doc = Doc((_, _, p) => p[N("TrimBox")] = Rect(10, 10, 700, 782)); // 700 > MediaBox 612
        Assert.Contains(new PageBoxesRule().Check(Ctx(doc)), f => f.Message.Contains("beyond"));
    }

    // ── TrappedRule ──────────────────────────────────────────────────────────

    [Fact]
    public void Absent_trapped_is_flagged()
    {
        var doc = Doc((_, _, _) => { });
        Assert.Single(new TrappedRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Trapped_unknown_is_flagged()
    {
        var doc = Doc((d, _, _) =>
        {
            d.AddObject(9, 0, new PdfDictionary { [N("Trapped")] = N("Unknown") });
            d.Trailer.Dictionary[N("Info")] = Ref(9);
        });
        Assert.Single(new TrappedRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Trapped_true_passes()
    {
        var doc = Doc((d, _, _) =>
        {
            d.AddObject(9, 0, new PdfDictionary { [N("Trapped")] = N("True") });
            d.Trailer.Dictionary[N("Info")] = Ref(9);
        });
        Assert.Empty(new TrappedRule().Check(Ctx(doc)));
    }

    [Fact] // fix: a GTS_PDFX intent with a condition id but no embedded profile is not enough for X-4
    public void Pdfx_output_intent_with_condition_id_but_no_profile_is_flagged()
    {
        var doc = Doc((_, c, _) =>
        {
            var intent = OutputIntent("GTS_PDFX");
            intent[N("OutputConditionIdentifier")] = new PdfString(System.Text.Encoding.ASCII.GetBytes("Custom"));
            c[N("OutputIntents")] = new PdfArray(intent);
        });
        Assert.Single(new PdfxOutputIntentRule().Check(Ctx(doc)));
    }

    [Fact] // fix: a box array with more than 4 elements must not throw out of the rule
    public void Page_box_with_extra_elements_does_not_throw()
    {
        var doc = Doc((_, _, p) => p[N("TrimBox")] = new PdfArray(
            new PdfInteger(10), new PdfInteger(10), new PdfInteger(602), new PdfInteger(782), new PdfInteger(0)));
        Assert.Empty(new PageBoxesRule().Check(Ctx(doc))); // first four coords are within MediaBox; no exception
    }

    // ── end-to-end ───────────────────────────────────────────────────────────

    [Fact]
    public void Minimal_conformant_x4_document_conforms()
    {
        var doc = Doc((d, c, p) =>
        {
            d.AddObject(10, 0, new PdfStream(new PdfDictionary(), new byte[] { 1 }));
            var intent = OutputIntent("GTS_PDFX");
            intent[N("DestOutputProfile")] = Ref(10);
            c[N("OutputIntents")] = new PdfArray(intent);
            p[N("TrimBox")] = Rect(10, 10, 602, 782);
            d.AddObject(9, 0, new PdfDictionary { [N("Trapped")] = N("False") });
            d.Trailer.Dictionary[N("Info")] = Ref(9);
            // /ID so the shared file-id rule is satisfied.
            d.Trailer.Dictionary[N("ID")] = new PdfArray(
                new PdfString(new byte[] { 1, 2, 3, 4 }), new PdfString(new byte[] { 1, 2, 3, 4 }));
        });

        PreflightResult result = Preflighter.Check(doc, ConformanceProfile.PdfX4);

        // No X-4-specific rule should fire (font-embedded may, since this doc has no fonts at all it won't).
        Assert.DoesNotContain(result.Findings, f => f.RuleId is "pdfx-output-intent" or "pdfx-page-boxes" or "pdfx-trapped");
    }
}
