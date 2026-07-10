using System;
using System.Linq;
using System.Text;
using ICCSharp.Profile;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering.Icc;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 11 of the preflight: PDF/X-4 transparency + spot/DeviceN colour depth (ISO 15930-7).
/// Covers the transparency-group blend space (<see cref="PdfxTransparencyColourRule"/>), the blend-mode
/// whitelist (<see cref="PdfxBlendModeRule"/>), NChannel /Colorants (<see cref="PdfxNChannelColorantsRule"/>),
/// Separation consistency (<see cref="PdfxSeparationConsistencyRule"/>), and the Separation/DeviceN
/// alternate-space governance folded into <see cref="PdfxColourRule"/> via <see cref="DeviceColourAnalysis"/>.
/// The real GOS PDF/X-4 files back the false-positive side (<see cref="GwgGosPassOracleTests"/>).
/// </summary>
public class PreflightSlice11Tests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfArray Rect(int x0, int y0, int x1, int y1) =>
        new(new PdfInteger(x0), new PdfInteger(y0), new PdfInteger(x1), new PdfInteger(y1));

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

    private static void SetCmykOutputIntent(PdfDocument doc, PdfDictionary catalog)
    {
        doc.AddObject(12, 0, new PdfStream(new PdfDictionary(), IccResources.ReadDefaultCmykProfile()));
        catalog[N("OutputIntents")] = new PdfArray(new PdfDictionary
        {
            [N("Type")] = N("OutputIntent"),
            [N("S")] = N("GTS_PDFX"),
            [N("DestOutputProfile")] = Ref(12),
        });
    }

    /// <summary>A minimal FunctionType-2 tint transform — its content is irrelevant to the alternate-space
    /// classifier, but a real object keeps the colour space well-formed.</summary>
    private static PdfDictionary TintFn() => new()
    {
        [N("FunctionType")] = new PdfInteger(2),
        [N("Domain")] = new PdfArray(new PdfInteger(0), new PdfInteger(1)),
        [N("C0")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(0), new PdfInteger(0)),
        [N("C1")] = new PdfArray(new PdfInteger(0), new PdfInteger(1), new PdfInteger(0), new PdfInteger(0)),
        [N("N")] = new PdfInteger(1),
    };

    // ── Separation/DeviceN alternate-space governance (via PdfxColourRule) ─────

    [Fact]
    public void Separation_with_rgb_alternate_under_cmyk_intent_is_flagged()
    {
        var doc = Doc((d, c, p) =>
        {
            SetCmykOutputIntent(d, c);
            var sep = new PdfArray(N("Separation"), N("Spot"), N("DeviceRGB"), TintFn());
            p[N("Resources")] = new PdfDictionary { [N("ColorSpace")] = new PdfDictionary { [N("Sep0")] = sep } };
            d.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("/Sep0 cs 0.5 scn")));
            p[N("Contents")] = Ref(11);
        });
        Finding finding = Assert.Single(new PdfxColourRule().Check(Ctx(doc)));
        Assert.Contains("DeviceRGB", finding.Message);
    }

    [Fact]
    public void Separation_with_cmyk_alternate_under_cmyk_intent_passes()
    {
        var doc = Doc((d, c, p) =>
        {
            SetCmykOutputIntent(d, c);
            var sep = new PdfArray(N("Separation"), N("Spot"), N("DeviceCMYK"), TintFn());
            p[N("Resources")] = new PdfDictionary { [N("ColorSpace")] = new PdfDictionary { [N("Sep0")] = sep } };
            d.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("/Sep0 cs 0.5 scn")));
            p[N("Contents")] = Ref(11);
        });
        Assert.Empty(new PdfxColourRule().Check(Ctx(doc)));
    }

    // ── PdfxTransparencyColourRule ─────────────────────────────────────────────

    private static PdfDictionary Group(string? cs)
    {
        var g = new PdfDictionary { [N("S")] = N("Transparency") };
        if (cs is not null) g[N("CS")] = N(cs);
        return g;
    }

    [Fact]
    public void Page_transparency_group_devicergb_under_cmyk_intent_is_flagged()
    {
        var doc = Doc((d, c, p) => { SetCmykOutputIntent(d, c); p[N("Group")] = Group("DeviceRGB"); });
        Finding finding = Assert.Single(new PdfxTransparencyColourRule().Check(Ctx(doc)));
        Assert.Equal("pdfx-transparency-colour", finding.RuleId);
        Assert.Contains("DeviceRGB", finding.Message);
    }

    [Fact]
    public void Page_transparency_group_devicecmyk_under_cmyk_intent_passes()
    {
        var doc = Doc((d, c, p) => { SetCmykOutputIntent(d, c); p[N("Group")] = Group("DeviceCMYK"); });
        Assert.Empty(new PdfxTransparencyColourRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Transparency_group_without_cs_passes()
    {
        var doc = Doc((d, c, p) => { SetCmykOutputIntent(d, c); p[N("Group")] = Group(null); });
        Assert.Empty(new PdfxTransparencyColourRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Form_xobject_transparency_group_devicergb_is_flagged()
    {
        var doc = Doc((d, c, p) =>
        {
            SetCmykOutputIntent(d, c);
            d.AddObject(20, 0, new PdfStream(new PdfDictionary
            {
                [N("Type")] = N("XObject"),
                [N("Subtype")] = N("Form"),
                [N("Group")] = Group("DeviceRGB"),
            }, Encoding.ASCII.GetBytes(" ")));
        });
        Assert.Single(new PdfxTransparencyColourRule().Check(Ctx(doc)));
    }

    // ── PdfxBlendModeRule ──────────────────────────────────────────────────────

    [Fact]
    public void Nonstandard_blend_mode_is_flagged()
    {
        var doc = Doc((d, _, _) => d.AddObject(30, 0, new PdfDictionary
        {
            [N("Type")] = N("ExtGState"),
            [N("BM")] = N("FunkyGlow"),
        }));
        Finding finding = Assert.Single(new PdfxBlendModeRule().Check(Ctx(doc)));
        Assert.Contains("FunkyGlow", finding.Message);
    }

    [Fact]
    public void Standard_blend_mode_passes()
    {
        var doc = Doc((d, _, _) => d.AddObject(30, 0, new PdfDictionary
        {
            [N("Type")] = N("ExtGState"),
            [N("BM")] = N("Multiply"),
        }));
        Assert.Empty(new PdfxBlendModeRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Blend_mode_array_flags_the_nonstandard_entry()
    {
        var doc = Doc((d, _, _) => d.AddObject(30, 0, new PdfDictionary
        {
            [N("BM")] = new PdfArray(N("Bogus"), N("Normal")),
        }));
        Finding finding = Assert.Single(new PdfxBlendModeRule().Check(Ctx(doc)));
        Assert.Contains("Bogus", finding.Message);
    }

    // ── PdfxNChannelColorantsRule ──────────────────────────────────────────────

    private static PdfArray DeviceN(PdfArray names, string alternate, PdfDictionary attributes) =>
        new(N("DeviceN"), names, N(alternate), TintFn(), attributes);

    [Fact]
    public void Nchannel_spot_colorant_missing_from_colorants_is_flagged()
    {
        var doc = Doc((d, _, _) =>
        {
            var attrs = new PdfDictionary
            {
                [N("Subtype")] = N("NChannel"),
                [N("Colorants")] = new PdfDictionary(), // empty — Spot1 is absent
            };
            d.AddObject(40, 0, DeviceN(new PdfArray(N("Spot1")), "DeviceCMYK", attrs));
        });
        Finding finding = Assert.Single(new PdfxNChannelColorantsRule().Check(Ctx(doc)));
        Assert.Contains("Spot1", finding.Message);
    }

    [Fact]
    public void Nchannel_with_process_colorant_only_passes()
    {
        // Cyan is a process colorant and needs no /Colorants entry.
        var doc = Doc((d, _, _) =>
        {
            var attrs = new PdfDictionary { [N("Subtype")] = N("NChannel"), [N("Colorants")] = new PdfDictionary() };
            d.AddObject(40, 0, DeviceN(new PdfArray(N("Cyan")), "DeviceCMYK", attrs));
        });
        Assert.Empty(new PdfxNChannelColorantsRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Nchannel_spot_colorant_present_passes()
    {
        var doc = Doc((d, _, _) =>
        {
            var colorants = new PdfDictionary
            {
                [N("Spot1")] = new PdfArray(N("Separation"), N("Spot1"), N("DeviceCMYK"), TintFn()),
            };
            var attrs = new PdfDictionary { [N("Subtype")] = N("NChannel"), [N("Colorants")] = colorants };
            d.AddObject(40, 0, DeviceN(new PdfArray(N("Spot1")), "DeviceCMYK", attrs));
        });
        Assert.Empty(new PdfxNChannelColorantsRule().Check(Ctx(doc)));
    }

    // ── PdfxSeparationConsistencyRule ──────────────────────────────────────────

    [Fact]
    public void Same_colorant_with_different_alternates_is_flagged()
    {
        var doc = Doc((d, _, _) =>
        {
            d.AddObject(50, 0, new PdfArray(N("Separation"), N("Spot"), N("DeviceCMYK"), TintFn()));
            d.AddObject(51, 0, new PdfArray(N("Separation"), N("Spot"), N("DeviceRGB"), TintFn()));
        });
        Finding finding = Assert.Single(new PdfxSeparationConsistencyRule().Check(Ctx(doc)));
        Assert.Contains("Spot", finding.Message);
    }

    [Fact]
    public void Same_colorant_with_identical_definitions_passes()
    {
        var doc = Doc((d, _, _) =>
        {
            d.AddObject(50, 0, new PdfArray(N("Separation"), N("Spot"), N("DeviceCMYK"), TintFn()));
            d.AddObject(51, 0, new PdfArray(N("Separation"), N("Spot"), N("DeviceCMYK"), TintFn()));
        });
        Assert.Empty(new PdfxSeparationConsistencyRule().Check(Ctx(doc)));
    }

    [Fact] // integer-vs-real formatting must not read as an inconsistency (canonicalised)
    public void Same_colorant_with_numerically_equal_tint_passes()
    {
        var doc = Doc((d, _, _) =>
        {
            var tintInt = new PdfDictionary
            {
                [N("FunctionType")] = new PdfInteger(2),
                [N("Domain")] = new PdfArray(new PdfInteger(0), new PdfInteger(1)),
                [N("N")] = new PdfInteger(1),
            };
            var tintReal = new PdfDictionary
            {
                [N("FunctionType")] = new PdfInteger(2),
                [N("Domain")] = new PdfArray(new PdfReal(0.0), new PdfReal(1.0)),
                [N("N")] = new PdfReal(1.0),
            };
            d.AddObject(50, 0, new PdfArray(N("Separation"), N("Spot"), N("DeviceCMYK"), tintInt));
            d.AddObject(51, 0, new PdfArray(N("Separation"), N("Spot"), N("DeviceCMYK"), tintReal));
        });
        Assert.Empty(new PdfxSeparationConsistencyRule().Check(Ctx(doc)));
    }

    [Fact] // /None and /All are universal colorants and exempt from consistency
    public void All_colorant_with_different_definitions_is_not_flagged()
    {
        var doc = Doc((d, _, _) =>
        {
            d.AddObject(50, 0, new PdfArray(N("Separation"), N("All"), N("DeviceCMYK"), TintFn()));
            d.AddObject(51, 0, new PdfArray(N("Separation"), N("All"), N("DeviceRGB"), TintFn()));
        });
        Assert.Empty(new PdfxSeparationConsistencyRule().Check(Ctx(doc)));
    }
}
