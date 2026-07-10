using System.Linq;
using System.Text;
using ICCSharp.Profile;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering.Icc;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 16b — clause 6.2.4.3 (ISO 19005-2, <see cref="DeviceColourRule"/>) extended into the content
/// paths <see cref="DeviceColourAnalysis"/> previously deferred: tiling-pattern content, shadings, Type3
/// glyph procedures, annotation appearance streams and the implicit (unset) DeviceGray fill — plus the
/// PDF/A output-intent <c>/S</c> validity fix (a GTS_PDFX intent does not satisfy PDF/A). Each detection
/// path gets a CI-safe synthetic doc, alongside the zero-false-positive pass cases (a covered device
/// colour, a per-scope Default* remap, a d1 uncoloured glyph, and a page that sets its fill colour).
/// The parity harness measures real detection; these pin every branch deterministically without a corpus.
/// </summary>
public class PreflightSlice16bTests
{
    private enum Oi { None, Rgb, Cmyk }

    private static byte[] RgbProfile => BuiltInProfiles.Srgb.Bytes.ToArray();
    private static byte[] CmykProfile => IccResources.ReadDefaultCmykProfile();

    /// <summary>
    /// Builds a one-page document: object 1 = catalog (optional /OutputIntents), 2 = pages tree,
    /// 3 = page (inline /Resources, optional /Annots), 4 = content stream. <paramref name="configure"/>
    /// registers further indirect objects (patterns, shadings, Type3 fonts, appearance forms) and wires
    /// them into the page's /Resources. <paramref name="oiSubtype"/> null means no output intent.
    /// </summary>
    private static PdfDocument BuildDoc(
        string content = "q Q",
        Action<PdfDocument, PdfDictionary>? configure = null,
        PdfArray? annots = null,
        string? oiSubtype = null,
        Oi oiFamily = Oi.Rgb)
    {
        var doc = new PdfDocument();
        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(content)));

        var resources = new PdfDictionary();
        configure?.Invoke(doc, resources);

        var pageDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Page"),
            [new PdfName("Parent")] = new PdfIndirectReference(2, 0),
            [new PdfName("Contents")] = new PdfIndirectReference(4, 0),
            [new PdfName("Resources")] = resources,
        };
        if (annots is not null)
            pageDict[new PdfName("Annots")] = annots;
        doc.AddObject(3, 0, pageDict);

        doc.AddObject(2, 0, new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Pages"),
            [new PdfName("Kids")] = new PdfArray(new PdfIndirectReference(3, 0)),
            [new PdfName("Count")] = new PdfInteger(1),
        });

        var catalog = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Catalog"),
            [new PdfName("Pages")] = new PdfIndirectReference(2, 0),
        };
        if (oiSubtype is not null)
        {
            byte[] profile = oiFamily == Oi.Cmyk ? CmykProfile : RgbProfile;
            doc.AddObject(9, 0, new PdfStream(new PdfDictionary(), profile));
            catalog[new PdfName("OutputIntents")] = new PdfArray(new PdfDictionary
            {
                [new PdfName("S")] = new PdfName(oiSubtype),
                [new PdfName("DestOutputProfile")] = new PdfIndirectReference(9, 0),
            });
        }
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(1, 0);
        return doc;
    }

    private static ConformanceContext Ctx(PdfDocument doc) => new(doc, ConformanceProfile.PdfA2b);

    private static string[] Clauses(PdfDocument doc) =>
        new DeviceColourRule().Check(Ctx(doc))
            .Select(f => ParitySnapshot.ClauseKey(f.Clause)!)
            .Distinct().OrderBy(c => c).ToArray();

    // A minimal device-independent ICCBased colour space [/ICCBased ref]; the classifier resolves it to
    // None without reading the stream, so the /N-only stub suffices.
    private static PdfArray IccBased(PdfDocument doc, int obj, int n)
    {
        doc.AddObject(obj, 0, new PdfStream(new PdfDictionary { [new PdfName("N")] = new PdfInteger(n) }, [0]));
        return new PdfArray(new PdfName("ICCBased"), new PdfIndirectReference(obj, 0));
    }

    private static PdfStream Pattern(string content, PdfDictionary? patternResources = null)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Pattern"),
            [new PdfName("PatternType")] = new PdfInteger(1),
            [new PdfName("PaintType")] = new PdfInteger(1),
            [new PdfName("BBox")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(10), new PdfInteger(10)),
            [new PdfName("Resources")] = patternResources ?? new PdfDictionary(),
        };
        return new PdfStream(dict, Encoding.ASCII.GetBytes(content));
    }

    // ── tiling patterns ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Tiling_pattern_deviceRGB_fires()
    {
        PdfDocument doc = BuildDoc(configure: (d, res) =>
        {
            d.AddObject(11, 0, Pattern("1 0 0 rg 0 0 5 5 re f"));
            res[new PdfName("Pattern")] = new PdfDictionary { [new PdfName("P0")] = new PdfIndirectReference(11, 0) };
        });
        Assert.Equal(["6.2.4.3"], Clauses(doc));
    }

    [Fact]
    public void Tiling_pattern_deviceCMYK_still_fires_when_only_the_PAGE_defines_DefaultCMYK()
    {
        // The DefaultCMYK remap lives on the page; the pattern scope has none, so the pattern's k counts.
        PdfDocument doc = BuildDoc(configure: (d, res) =>
        {
            d.AddObject(11, 0, Pattern("0 0 0 1 k 0 0 5 5 re f")); // pattern /Resources <<>> — no Default*
            res[new PdfName("Pattern")] = new PdfDictionary { [new PdfName("P0")] = new PdfIndirectReference(11, 0) };
            res[new PdfName("ColorSpace")] = new PdfDictionary { [new PdfName("DefaultCMYK")] = IccBased(d, 30, 4) };
        });
        Assert.Equal(["6.2.4.3"], Clauses(doc));
    }

    [Fact]
    public void Tiling_pattern_device_colour_remapped_by_the_patterns_OWN_Default_does_not_fire()
    {
        // A DefaultRGB in the pattern's own /Resources remaps the pattern's rg — no device colour usage.
        PdfDocument doc = BuildDoc(configure: (d, res) =>
        {
            var patternResources = new PdfDictionary
            {
                [new PdfName("ColorSpace")] = new PdfDictionary { [new PdfName("DefaultRGB")] = IccBased(d, 30, 3) },
            };
            d.AddObject(11, 0, Pattern("1 0 0 rg 0 0 5 5 re f", patternResources));
            res[new PdfName("Pattern")] = new PdfDictionary { [new PdfName("P0")] = new PdfIndirectReference(11, 0) };
        });
        Assert.Empty(Clauses(doc));
    }

    [Fact]
    public void Tiling_pattern_deviceRGB_with_matching_rgb_output_intent_passes()
    {
        PdfDocument doc = BuildDoc(
            oiSubtype: "GTS_PDFA1", oiFamily: Oi.Rgb,
            configure: (d, res) =>
            {
                d.AddObject(11, 0, Pattern("1 0 0 rg 0 0 5 5 re f"));
                res[new PdfName("Pattern")] = new PdfDictionary { [new PdfName("P0")] = new PdfIndirectReference(11, 0) };
            });
        Assert.Empty(Clauses(doc));
    }

    // ── shadings ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Shading_with_device_colour_space_fires()
    {
        PdfDocument doc = BuildDoc(configure: (d, res) =>
        {
            var shading = new PdfDictionary
            {
                [new PdfName("ShadingType")] = new PdfInteger(2),
                [new PdfName("ColorSpace")] = new PdfName("DeviceRGB"),
            };
            d.AddObject(12, 0, shading);
            res[new PdfName("Shading")] = new PdfDictionary { [new PdfName("SH0")] = new PdfIndirectReference(12, 0) };
        });
        Assert.Equal(["6.2.4.3"], Clauses(doc));
    }

    [Fact]
    public void Shading_with_device_independent_colour_space_does_not_fire()
    {
        PdfDocument doc = BuildDoc(configure: (d, res) =>
        {
            var shading = new PdfDictionary
            {
                [new PdfName("ShadingType")] = new PdfInteger(2),
                [new PdfName("ColorSpace")] = IccBased(d, 12, 3),
            };
            d.AddObject(13, 0, shading);
            res[new PdfName("Shading")] = new PdfDictionary { [new PdfName("SH0")] = new PdfIndirectReference(13, 0) };
        });
        Assert.Empty(Clauses(doc));
    }

    // ── Type3 glyph procedures ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Type3_d0_coloured_glyph_counts_device_colour()
    {
        PdfDocument doc = BuildDoc(configure: (d, res) => AddType3(d, res, "1000 0 d0 1 0 0 rg 0 0 750 750 re f"));
        Assert.Equal(["6.2.4.3"], Clauses(doc));
    }

    [Fact]
    public void Type3_d1_uncoloured_glyph_colour_is_ignored()
    {
        // A d1 stencil glyph's colour operators are ignored (ISO 32000-1 9.6.5.1) — no false positive.
        PdfDocument doc = BuildDoc(configure: (d, res) => AddType3(d, res, "1000 0 0 0 750 750 d1 1 0 0 rg 0 0 750 750 re f"));
        Assert.Empty(Clauses(doc));
    }

    private static void AddType3(PdfDocument doc, PdfDictionary resources, string glyphContent)
    {
        doc.AddObject(15, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(glyphContent)));
        var font = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("Type3"),
            [new PdfName("CharProcs")] = new PdfDictionary { [new PdfName("a")] = new PdfIndirectReference(15, 0) },
            [new PdfName("Resources")] = new PdfDictionary(),
        };
        doc.AddObject(14, 0, font);
        resources[new PdfName("Font")] = new PdfDictionary { [new PdfName("F1")] = new PdfIndirectReference(14, 0) };
    }

    // ── annotation appearance streams ──────────────────────────────────────────────────────────────

    [Fact]
    public void Annotation_appearance_device_colour_fires()
    {
        var apForm = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Form"),
            [new PdfName("BBox")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(5), new PdfInteger(5)),
        };
        var annot = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Widget"),
            [new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = new PdfIndirectReference(22, 0) },
        };
        PdfDocument doc = BuildDoc(
            configure: (d, _) =>
            {
                d.AddObject(22, 0, new PdfStream(apForm, Encoding.ASCII.GetBytes("1 0 0 rg 0 0 5 5 re f")));
                d.AddObject(23, 0, annot);
            },
            annots: new PdfArray(new PdfIndirectReference(23, 0)));
        Assert.Equal(["6.2.4.3"], Clauses(doc));
    }

    [Fact]
    public void Annotation_appearance_substate_device_colour_fires()
    {
        // A button /AP /N is a sub-dictionary of on/off states, each an appearance stream — walk them all.
        var apForm = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Form"),
            [new PdfName("BBox")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(5), new PdfInteger(5)),
        };
        var annot = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Widget"),
            [new PdfName("AP")] = new PdfDictionary
            {
                [new PdfName("N")] = new PdfDictionary { [new PdfName("On")] = new PdfIndirectReference(22, 0) },
            },
        };
        PdfDocument doc = BuildDoc(
            configure: (d, _) =>
            {
                d.AddObject(22, 0, new PdfStream(apForm, Encoding.ASCII.GetBytes("1 0 0 rg 0 0 5 5 re f")));
                d.AddObject(23, 0, annot);
            },
            annots: new PdfArray(new PdfIndirectReference(23, 0)));
        Assert.Equal(["6.2.4.3"], Clauses(doc));
    }

    // ── implicit (unset) DeviceGray fill ───────────────────────────────────────────────────────────

    [Fact]
    public void Implicit_default_gray_fill_with_no_output_intent_fires()
    {
        // A path filled before any colour operator uses the initial DeviceGray (ISO 32000-1 8.6.3).
        PdfDocument doc = BuildDoc(content: "0 0 10 10 re f");
        Assert.Equal(["6.2.4.3"], Clauses(doc));
    }

    [Fact]
    public void Page_that_sets_its_fill_colour_before_painting_does_not_fire()
    {
        // Fill colour set to a device-independent ICCBased space before the fill — no implicit gray.
        PdfDocument doc = BuildDoc(
            content: "/CS0 cs 0.5 0.5 0.5 sc 0 0 10 10 re f",
            configure: (d, res) =>
                res[new PdfName("ColorSpace")] = new PdfDictionary { [new PdfName("CS0")] = IccBased(d, 30, 3) });
        Assert.Empty(Clauses(doc));
    }

    [Fact]
    public void Implicit_default_gray_fill_with_an_output_intent_passes()
    {
        // DeviceGray is covered by an output intent of any family (here RGB).
        PdfDocument doc = BuildDoc(content: "0 0 10 10 re f", oiSubtype: "GTS_PDFA1", oiFamily: Oi.Rgb);
        Assert.Empty(Clauses(doc));
    }

    // ── Part 2: output-intent /S validity for a PDF/A target ───────────────────────────────────────

    [Fact]
    public void Gts_pdfx_output_intent_does_not_cover_device_colour_for_a_pdfa_target()
    {
        // A GTS_PDFX (PDF/X) intent does not satisfy PDF/A, so the direct DeviceRGB is uncovered.
        PdfDocument doc = BuildDoc(content: "1 0 0 rg 0 0 10 10 re f", oiSubtype: "GTS_PDFX", oiFamily: Oi.Rgb);
        Assert.Equal(["6.2.4.3"], Clauses(doc));
    }

    [Fact]
    public void Gts_pdfa1_output_intent_covers_matching_device_colour()
    {
        PdfDocument doc = BuildDoc(content: "1 0 0 rg 0 0 10 10 re f", oiSubtype: "GTS_PDFA1", oiFamily: Oi.Rgb);
        Assert.Empty(Clauses(doc));
    }
}
