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
/// Slice 16a — transparency blending colour space (ISO 19005-2, 6.2.10 + 6.2.4.3,
/// <see cref="TransparencyColourRule"/>). A page that paints a transparent object (an ExtGState soft
/// mask / sub-1 alpha / non-normal blend mode, or a Form XObject transparency group) must blend in a
/// defined colour space: with no output intent and no page group <c>/CS</c> the blending space is
/// implicit-device (6.2.10), and any reachable transparency group whose device blending <c>/CS</c> the
/// output intent does not cover fails 6.2.4.3. These CI-safe synthetic docs pin every trigger, every
/// emission branch, and the zero-false-positive pass cases; the parity harness measures real detection.
/// </summary>
public class PreflightSlice16Tests
{
    private enum Oi { None, NoProfile, Rgb, Cmyk }

    private static byte[] RgbProfile => BuiltInProfiles.Srgb.Bytes.ToArray();
    private static byte[] CmykProfile => IccResources.ReadDefaultCmykProfile();

    /// <summary>
    /// Builds a one-page document: object 1 = catalog (with /OutputIntents per <paramref name="oi"/>),
    /// 2 = pages tree, 3 = page (inline /Resources, optional /Group and /Annots), 4 = content stream.
    /// <paramref name="configure"/> may register further indirect objects (Form/pattern/Type3 objects)
    /// on <paramref name="doc"/> and wire them into the page's /Resources.
    /// </summary>
    private static PdfDocument BuildDoc(
        Oi oi = Oi.None,
        Action<PdfDocument, PdfDictionary>? configure = null,
        PdfObject? pageGroup = null,
        PdfArray? annots = null)
    {
        var doc = new PdfDocument();
        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("0 0 10 10 re f")));

        var resources = new PdfDictionary();
        configure?.Invoke(doc, resources);

        var pageDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Page"),
            [new PdfName("Parent")] = new PdfIndirectReference(2, 0),
            [new PdfName("Contents")] = new PdfIndirectReference(4, 0),
            [new PdfName("Resources")] = resources,
        };
        if (pageGroup is not null)
            pageDict[new PdfName("Group")] = pageGroup;
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
        ApplyOutputIntent(doc, catalog, oi);
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(1, 0);
        return doc;
    }

    private static void ApplyOutputIntent(PdfDocument doc, PdfDictionary catalog, Oi oi)
    {
        if (oi == Oi.None)
            return;

        var intent = new PdfDictionary { [new PdfName("S")] = new PdfName("GTS_PDFA1") };
        if (oi != Oi.NoProfile)
        {
            byte[] profile = oi == Oi.Rgb ? RgbProfile : CmykProfile;
            doc.AddObject(9, 0, new PdfStream(new PdfDictionary(), profile));
            intent[new PdfName("DestOutputProfile")] = new PdfIndirectReference(9, 0);
        }
        catalog[new PdfName("OutputIntents")] = new PdfArray(intent);
    }

    /// <summary>Adds one ExtGState (name /GS0) built from <paramref name="entries"/> to the resources.</summary>
    private static void AddExtGState(PdfDictionary resources, params (string Key, PdfObject Value)[] entries)
    {
        var gs = new PdfDictionary();
        foreach ((string key, PdfObject value) in entries)
            gs[new PdfName(key)] = value;
        resources[new PdfName("ExtGState")] = new PdfDictionary { [new PdfName("GS0")] = gs };
    }

    /// <summary>Registers a Form XObject (obj 10) with a /Group /S /Transparency (optional /CS) under /Fm0.</summary>
    private static void AddFormGroup(PdfDocument doc, PdfDictionary resources, PdfObject? groupCs)
    {
        var group = new PdfDictionary
        {
            [new PdfName("S")] = new PdfName("Transparency"),
        };
        if (groupCs is not null)
            group[new PdfName("CS")] = groupCs;

        var formDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Form"),
            [new PdfName("Group")] = group,
        };
        doc.AddObject(10, 0, new PdfStream(formDict, Encoding.ASCII.GetBytes("0 0 5 5 re f")));
        resources[new PdfName("XObject")] = new PdfDictionary { [new PdfName("Fm0")] = new PdfIndirectReference(10, 0) };
    }

    private static ConformanceContext Ctx(PdfDocument doc) => new(doc, ConformanceProfile.PdfA2b);

    private static Finding[] Findings(PdfDocument doc) =>
        new TransparencyColourRule().Check(Ctx(doc)).ToArray();

    private static string[] Clauses(PdfDocument doc) =>
        Findings(doc).Select(f => ParitySnapshot.ClauseKey(f.Clause)!).OrderBy(c => c).ToArray();

    // ── triggers: each transparent object, no OI, no page group → 6.2.10 + 6.2.4.3 (implicit) ─────

    [Fact]
    public void NonStrokingAlpha_below_one_fires_both_clauses()
    {
        PdfDocument doc = BuildDoc(configure: (_, res) => AddExtGState(res, ("ca", new PdfReal(0.5))));
        Assert.Equal(["6.2.10", "6.2.4.3"], Clauses(doc));
    }

    [Fact]
    public void StrokingAlpha_below_one_fires_both_clauses()
    {
        PdfDocument doc = BuildDoc(configure: (_, res) => AddExtGState(res, ("CA", new PdfReal(0.25))));
        Assert.Equal(["6.2.10", "6.2.4.3"], Clauses(doc));
    }

    [Fact]
    public void SoftMask_dictionary_fires_both_clauses()
    {
        PdfDocument doc = BuildDoc(configure: (_, res) =>
            AddExtGState(res, ("SMask", new PdfDictionary { [new PdfName("S")] = new PdfName("Alpha") })));
        Assert.Equal(["6.2.10", "6.2.4.3"], Clauses(doc));
    }

    [Fact]
    public void NonNormal_blend_mode_fires_both_clauses()
    {
        PdfDocument doc = BuildDoc(configure: (_, res) => AddExtGState(res, ("BM", new PdfName("Multiply"))));
        Assert.Equal(["6.2.10", "6.2.4.3"], Clauses(doc));
    }

    [Fact]
    public void Form_transparency_group_fires_both_clauses()
    {
        PdfDocument doc = BuildDoc(configure: (d, res) => AddFormGroup(d, res, groupCs: null));
        Assert.Equal(["6.2.10", "6.2.4.3"], Clauses(doc));
    }

    // ── the transparent object is deliberately narrow: these are NOT triggers ─────────────────────

    [Fact]
    public void SoftMask_none_is_not_a_transparent_object()
    {
        PdfDocument doc = BuildDoc(configure: (_, res) => AddExtGState(res, ("SMask", new PdfName("None"))));
        Assert.Empty(Findings(doc));
    }

    [Fact]
    public void Normal_blend_mode_is_not_a_transparent_object()
    {
        PdfDocument doc = BuildDoc(configure: (_, res) => AddExtGState(res, ("BM", new PdfName("Normal"))));
        Assert.Empty(Findings(doc));
    }

    [Fact]
    public void Image_soft_mask_is_not_a_transparent_object()
    {
        // An image XObject /SMask is intentionally excluded from the trigger set (parity slice 16a).
        PdfDocument doc = BuildDoc(configure: (d, res) =>
        {
            var image = new PdfDictionary
            {
                [new PdfName("Type")] = new PdfName("XObject"),
                [new PdfName("Subtype")] = new PdfName("Image"),
                [new PdfName("Width")] = new PdfInteger(1),
                [new PdfName("Height")] = new PdfInteger(1),
                [new PdfName("BitsPerComponent")] = new PdfInteger(8),
                [new PdfName("ColorSpace")] = new PdfName("DeviceGray"),
                [new PdfName("SMask")] = new PdfIndirectReference(11, 0),
            };
            d.AddObject(10, 0, new PdfStream(image, [0]));
            d.AddObject(11, 0, new PdfStream(new PdfDictionary { [new PdfName("Subtype")] = new PdfName("Image") }, [0]));
            res[new PdfName("XObject")] = new PdfDictionary { [new PdfName("Im0")] = new PdfIndirectReference(10, 0) };
        });
        Assert.Empty(Findings(doc));
    }

    [Fact]
    public void No_transparent_object_with_device_page_group_and_no_oi_passes()
    {
        // A device page group /CS alone is not a transparent object — nothing is painted transparently.
        PdfDocument doc = BuildDoc(pageGroup: PageGroup(new PdfName("DeviceRGB")));
        Assert.Empty(Findings(doc));
    }

    // ── 6.2.10 branch: an output intent OR a page group /CS defuses it ────────────────────────────

    [Fact]
    public void Transparent_with_valid_output_intent_and_no_page_group_passes()
    {
        PdfDocument doc = BuildDoc(Oi.Rgb, (_, res) => AddExtGState(res, ("ca", new PdfReal(0.5))));
        Assert.Empty(Findings(doc));
    }

    [Fact]
    public void Transparent_with_device_independent_page_group_and_no_oi_passes()
    {
        // A device-independent (ICCBased) page group /CS defines the blending space with no output intent.
        PdfDocument doc = BuildDoc(
            configure: (d, res) =>
            {
                AddExtGState(res, ("ca", new PdfReal(0.5)));
                d.AddObject(12, 0, new PdfStream(new PdfDictionary { [new PdfName("N")] = new PdfInteger(3) }, [0]));
            },
            pageGroup: PageGroup(new PdfArray(new PdfName("ICCBased"), new PdfIndirectReference(12, 0))));
        Assert.Empty(Findings(doc));
    }

    [Fact]
    public void Output_intent_without_destination_profile_counts_as_none()
    {
        // A GTS_PDFA1 intent with no /DestOutputProfile is not a valid PDF/A output intent → still fires.
        PdfDocument doc = BuildDoc(Oi.NoProfile, (_, res) => AddExtGState(res, ("ca", new PdfReal(0.5))));
        Assert.Equal(["6.2.10", "6.2.4.3"], Clauses(doc));
    }

    // ── 6.2.4.3 branch: a device blending /CS the output intent does not cover ────────────────────

    [Fact]
    public void Page_group_deviceRGB_with_cmyk_output_intent_fires_6243_only()
    {
        PdfDocument doc = BuildDoc(
            Oi.Cmyk,
            (_, res) => AddExtGState(res, ("ca", new PdfReal(0.5))),
            pageGroup: PageGroup(new PdfName("DeviceRGB")));
        Assert.Equal(["6.2.4.3"], Clauses(doc));
    }

    [Fact]
    public void Page_group_deviceRGB_with_matching_rgb_output_intent_passes()
    {
        PdfDocument doc = BuildDoc(
            Oi.Rgb,
            (_, res) => AddExtGState(res, ("ca", new PdfReal(0.5))),
            pageGroup: PageGroup(new PdfName("DeviceRGB")));
        Assert.Empty(Findings(doc));
    }

    [Fact]
    public void Form_group_deviceRGB_with_cmyk_output_intent_fires_6243_only()
    {
        // OI present (CMYK) defuses 6.2.10; the RGB form-group blend space is uncovered → 6.2.4.3.
        PdfDocument doc = BuildDoc(Oi.Cmyk, (d, res) => AddFormGroup(d, res, groupCs: new PdfName("DeviceRGB")));
        Assert.Equal(["6.2.4.3"], Clauses(doc));
    }

    [Fact]
    public void Form_group_deviceGray_with_any_output_intent_passes()
    {
        // DeviceGray blending is covered by an output intent of any family (here RGB).
        PdfDocument doc = BuildDoc(Oi.Rgb, (d, res) => AddFormGroup(d, res, groupCs: new PdfName("DeviceGray")));
        Assert.Empty(Findings(doc));
    }

    [Fact]
    public void Form_group_deviceGray_with_no_output_intent_fires_both()
    {
        PdfDocument doc = BuildDoc(configure: (d, res) => AddFormGroup(d, res, groupCs: new PdfName("DeviceGray")));
        Assert.Equal(["6.2.10", "6.2.4.3"], Clauses(doc));
    }

    // ── reachability: Type3 glyph resources, tiling patterns, annotation appearances ──────────────

    [Fact]
    public void Transparent_extgstate_inside_type3_font_is_reached()
    {
        PdfDocument doc = BuildDoc(configure: (d, res) =>
        {
            var type3Resources = new PdfDictionary();
            AddExtGState(type3Resources, ("ca", new PdfReal(0.5)));
            var font = new PdfDictionary
            {
                [new PdfName("Type")] = new PdfName("Font"),
                [new PdfName("Subtype")] = new PdfName("Type3"),
                [new PdfName("Resources")] = type3Resources,
            };
            d.AddObject(20, 0, font);
            res[new PdfName("Font")] = new PdfDictionary { [new PdfName("F0")] = new PdfIndirectReference(20, 0) };
        });
        Assert.Equal(["6.2.10", "6.2.4.3"], Clauses(doc));
    }

    [Fact]
    public void Transparent_extgstate_inside_tiling_pattern_is_reached()
    {
        PdfDocument doc = BuildDoc(configure: (d, res) =>
        {
            var patternResources = new PdfDictionary();
            AddExtGState(patternResources, ("ca", new PdfReal(0.5)));
            var patternDict = new PdfDictionary
            {
                [new PdfName("Type")] = new PdfName("Pattern"),
                [new PdfName("PatternType")] = new PdfInteger(1),
                [new PdfName("Resources")] = patternResources,
            };
            d.AddObject(21, 0, new PdfStream(patternDict, Encoding.ASCII.GetBytes("0 0 1 1 re f")));
            res[new PdfName("Pattern")] = new PdfDictionary { [new PdfName("P0")] = new PdfIndirectReference(21, 0) };
        });
        Assert.Equal(["6.2.10", "6.2.4.3"], Clauses(doc));
    }

    [Fact]
    public void Transparency_group_inside_annotation_appearance_is_reached()
    {
        var apForm = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Form"),
            [new PdfName("Group")] = new PdfDictionary
            {
                [new PdfName("S")] = new PdfName("Transparency"),
                [new PdfName("CS")] = new PdfName("DeviceRGB"),
            },
        };
        var annot = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Widget"),
            [new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = new PdfIndirectReference(22, 0) },
        };
        PdfDocument doc = BuildDoc(
            configure: (d, _) =>
            {
                d.AddObject(22, 0, new PdfStream(apForm, Encoding.ASCII.GetBytes("0 0 1 1 re f")));
                d.AddObject(23, 0, annot);
            },
            annots: new PdfArray(new PdfIndirectReference(23, 0)));
        // No OI, no page group, RGB blend space → 6.2.10 (implicit) + 6.2.4.3 (uncovered RGB).
        Assert.Equal(["6.2.10", "6.2.4.3"], Clauses(doc));
    }

    // ── the rule only applies to PDF/A ────────────────────────────────────────────────────────────

    [Fact]
    public void Rule_targets_all_pdfa_profiles_only()
    {
        Assert.Equal(ConformanceProfile.AllPdfA, new TransparencyColourRule().AppliesToProfiles);
    }

    // A page /Group dictionary carrying the given /CS (always a transparency group).
    private static PdfDictionary PageGroup(PdfObject cs) => new()
    {
        [new PdfName("Type")] = new PdfName("Group"),
        [new PdfName("S")] = new PdfName("Transparency"),
        [new PdfName("CS")] = cs,
    };
}
