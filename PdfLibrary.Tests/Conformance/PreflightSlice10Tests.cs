using System;
using System.Linq;
using System.Text;
using ICCSharp.Profile;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Metadata;
using PdfLibrary.Rendering.Icc;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 10 of the preflight: PDF/X-4 version identification (<see cref="PdfxVersionRule"/>) and colour
/// governance (<see cref="PdfxColourRule"/>). Rule-level tests over hand-built documents; the real GOS
/// PDF/X-4 files back the false-positive side (<see cref="GwgGosPassOracleTests"/>).
/// </summary>
public class PreflightSlice10Tests
{
    // The normative PDF/X identification schema namespace (ISO 15930-7, NPES).
    private const string PdfxIdNs = "http://www.npes.org/pdfx/ns/id/";

    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfArray Rect(int x0, int y0, int x1, int y1) =>
        new(new PdfInteger(x0), new PdfInteger(y0), new PdfInteger(x1), new PdfInteger(y1));

    /// <summary>One-page document (612×792 MediaBox); <paramref name="configure"/> tweaks the doc,
    /// catalog and page dict before they are wired up. Stream objects should use numbers ≥ 10.</summary>
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

    /// <summary>An XMP packet carrying pdfxid:GTS_PDFXVersion (omitted when <paramref name="version"/> is null).</summary>
    private static byte[] Xmp(string? version)
    {
        XmpPacket packet = XmpPacket.CreateEmpty();
        if (version is not null)
            packet.SetSimple(PdfxIdNs, "pdfxid", "GTS_PDFXVersion", version);
        return packet.Serialize();
    }

    private static void SetMetadata(PdfDocument doc, PdfDictionary catalog, byte[] xmp)
    {
        doc.AddObject(10, 0, new PdfStream(
            new PdfDictionary { [N("Type")] = N("Metadata"), [N("Subtype")] = N("XML") }, xmp));
        catalog[N("Metadata")] = Ref(10);
    }

    private static void SetPageContent(PdfDocument doc, PdfDictionary page, string content)
    {
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(content)));
        page[N("Contents")] = Ref(11);
    }

    private static void SetOutputIntent(PdfDocument doc, PdfDictionary catalog, byte[] profile)
    {
        doc.AddObject(12, 0, new PdfStream(new PdfDictionary(), profile));
        catalog[N("OutputIntents")] = new PdfArray(new PdfDictionary
        {
            [N("Type")] = N("OutputIntent"),
            [N("S")] = N("GTS_PDFX"),
            [N("DestOutputProfile")] = Ref(12),
        });
    }

    // ── PdfxVersionRule ──────────────────────────────────────────────────────

    [Fact]
    public void Missing_metadata_is_flagged()
    {
        var doc = Doc((_, _, _) => { });
        Finding finding = Assert.Single(new PdfxVersionRule().Check(Ctx(doc)));
        Assert.Equal("pdfx-version", finding.RuleId);
        Assert.Contains("missing", finding.Message);
    }

    [Fact]
    public void Pdfx4_version_passes()
    {
        var doc = Doc((d, c, _) => SetMetadata(d, c, Xmp("PDF/X-4")));
        Assert.Empty(new PdfxVersionRule().Check(Ctx(doc)));
    }

    [Fact] // "PDF/X-4p" (external output-intent profile variant) is a valid X-4 version string.
    public void Pdfx4p_version_passes()
    {
        var doc = Doc((d, c, _) => SetMetadata(d, c, Xmp("PDF/X-4p")));
        Assert.Empty(new PdfxVersionRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Metadata_without_pdfxid_version_is_flagged()
    {
        var doc = Doc((d, c, _) => SetMetadata(d, c, Xmp(version: null)));
        Finding finding = Assert.Single(new PdfxVersionRule().Check(Ctx(doc)));
        Assert.Contains("lacks", finding.Message);
    }

    [Fact]
    public void Wrong_pdfx_flavour_is_flagged()
    {
        var doc = Doc((d, c, _) => SetMetadata(d, c, Xmp("PDF/X-3")));
        Finding finding = Assert.Single(new PdfxVersionRule().Check(Ctx(doc)));
        Assert.Contains("PDF/X-3", finding.Message);
    }

    // ── PdfxColourRule ─────────────────────────────────────────────────────────

    [Fact]
    public void DeviceRgb_with_cmyk_intent_is_flagged()
    {
        var doc = Doc((d, c, p) =>
        {
            SetPageContent(d, p, "1 0 0 rg 0 0 10 10 re f");
            SetOutputIntent(d, c, IccResources.ReadDefaultCmykProfile());
        });
        Finding finding = Assert.Single(new PdfxColourRule().Check(Ctx(doc)));
        Assert.Equal("pdfx-device-colour", finding.RuleId);
        Assert.Contains("DeviceRGB", finding.Message);
    }

    [Fact] // the key false-positive guard: DeviceCMYK is governed by a CMYK output intent.
    public void DeviceCmyk_with_cmyk_intent_passes()
    {
        var doc = Doc((d, c, p) =>
        {
            SetPageContent(d, p, "0 0 0 1 k 0 0 10 10 re f");
            SetOutputIntent(d, c, IccResources.ReadDefaultCmykProfile());
        });
        Assert.Empty(new PdfxColourRule().Check(Ctx(doc)));
    }

    [Fact] // DeviceGray is permitted under any output intent.
    public void DeviceGray_with_cmyk_intent_passes()
    {
        var doc = Doc((d, c, p) =>
        {
            SetPageContent(d, p, "0.5 g 0 0 10 10 re f");
            SetOutputIntent(d, c, IccResources.ReadDefaultCmykProfile());
        });
        Assert.Empty(new PdfxColourRule().Check(Ctx(doc)));
    }

    [Fact]
    public void DeviceRgb_with_rgb_intent_passes()
    {
        var doc = Doc((d, c, p) =>
        {
            SetPageContent(d, p, "1 0 0 rg 0 0 10 10 re f");
            SetOutputIntent(d, c, BuiltInProfiles.Srgb.Bytes.ToArray());
        });
        Assert.Empty(new PdfxColourRule().Check(Ctx(doc)));
    }

    [Fact]
    public void DeviceCmyk_without_output_intent_is_flagged()
    {
        var doc = Doc((d, _, p) => SetPageContent(d, p, "0 0 0 1 k 0 0 10 10 re f"));
        Finding finding = Assert.Single(new PdfxColourRule().Check(Ctx(doc)));
        Assert.Contains("DeviceCMYK", finding.Message);
    }

    // ── end-to-end ─────────────────────────────────────────────────────────────

    [Fact]
    public void Conformant_x4_colour_and_version_produce_no_slice10_findings()
    {
        var doc = Doc((d, c, p) =>
        {
            SetMetadata(d, c, Xmp("PDF/X-4"));
            SetPageContent(d, p, "0 0 0 1 k 0 0 10 10 re f"); // DeviceCMYK under a CMYK intent
            SetOutputIntent(d, c, IccResources.ReadDefaultCmykProfile());
        });

        PreflightResult result = Preflighter.Check(doc, ConformanceProfile.PdfX4);

        Assert.DoesNotContain(result.Findings, f => f.RuleId is "pdfx-version" or "pdfx-device-colour");
    }
}
