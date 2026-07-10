using System.Text;
using ICCSharp.Profile;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering.Icc;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 6 of the preflight: device colour spaces (<c>device-colour</c>, ISO 19005-2, 6.2.4.3). A
/// device colour space (DeviceGray/RGB/CMYK) may only be used when the file carries a PDF/A output
/// intent with a matching destination profile family — DeviceRGB needs an RGB output intent,
/// DeviceCMYK a CMYK one, DeviceGray any output intent at all. Detection walks page content streams
/// and Form XObjects recursively (<see cref="DeviceColourAnalysis"/>), and also covers inline images
/// and image XObjects.
/// </summary>
public class PreflightSlice6Tests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    /// <summary>
    /// Builds a minimal in-memory, well-formed document with one page reachable via
    /// <c>Document.GetPages()</c>: object 1 = catalog (with /Pages and, when
    /// <paramref name="outputIntentProfile"/> is given, /OutputIntents), object 2 = pages tree,
    /// object 3 = page (/Contents ref, /Resources when <paramref name="configureResources"/> is
    /// given), object 4 = the page's content stream (<paramref name="contentBytes"/>).
    /// <paramref name="configureResources"/> may add further indirect objects (e.g. Form/Image
    /// XObjects) to <paramref name="doc"/> and register them in the page's /Resources dictionary.
    /// </summary>
    private static PdfDocument BuildDoc(
        byte[] contentBytes,
        Action<PdfDocument, PdfDictionary>? configureResources = null,
        byte[]? outputIntentProfile = null)
    {
        var doc = new PdfDocument();

        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), contentBytes));

        var pageDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Page"),
            [new PdfName("Parent")] = new PdfIndirectReference(2, 0),
            [new PdfName("Contents")] = new PdfIndirectReference(4, 0),
        };

        if (configureResources is not null)
        {
            var resources = new PdfDictionary();
            configureResources(doc, resources);
            pageDict[new PdfName("Resources")] = resources;
        }

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

        if (outputIntentProfile is not null)
        {
            doc.AddObject(9, 0, new PdfStream(new PdfDictionary(), outputIntentProfile));
            catalog[new PdfName("OutputIntents")] = new PdfArray(new PdfDictionary
            {
                [new PdfName("S")] = new PdfName("GTS_PDFA1"),
                [new PdfName("DestOutputProfile")] = new PdfIndirectReference(9, 0),
            });
        }

        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(1, 0);
        return doc;
    }

    /// <summary>Registers a Form XObject (indirect object 10) under resource name <paramref name="name"/>,
    /// whose content stream is <paramref name="formContentBytes"/> and which has no /Resources of its
    /// own (exercising the "inherit parent resources" branch of the walker).</summary>
    private static void AddFormXObject(PdfDocument doc, PdfDictionary resources, string name, byte[] formContentBytes)
    {
        var formDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Form"),
        };
        doc.AddObject(10, 0, new PdfStream(formDict, formContentBytes));

        resources[new PdfName("XObject")] = new PdfDictionary
        {
            [new PdfName(name)] = new PdfIndirectReference(10, 0),
        };
    }

    /// <summary>Registers an Image XObject (indirect object 11) under resource name <paramref name="name"/>
    /// with the given /ColorSpace.</summary>
    private static void AddImageXObject(PdfDocument doc, PdfDictionary resources, string name, string colorSpace)
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(1),
            [new PdfName("Height")] = new PdfInteger(1),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
            [new PdfName("ColorSpace")] = new PdfName(colorSpace),
        };
        doc.AddObject(11, 0, new PdfStream(imageDict, [0]));

        resources[new PdfName("XObject")] = new PdfDictionary
        {
            [new PdfName(name)] = new PdfIndirectReference(11, 0),
        };
    }

    private static ConformanceContext Ctx(PdfDocument doc) => new(doc, ConformanceProfile.PdfA2b);

    private static byte[] RgbProfile => BuiltInProfiles.Srgb.Bytes.ToArray();
    private static byte[] CmykProfile => IccResources.ReadDefaultCmykProfile();

    // ── DeviceRGB (path colour operator "rg") ────────────────────────────────

    [Fact]
    public void DeviceRgb_noOutputIntent_isError()
    {
        PdfDocument doc = BuildDoc(Ascii("1 0 0 rg 0 0 100 100 re f"));
        Finding finding = Assert.Single(new DeviceColourRule().Check(Ctx(doc)));

        Assert.Equal("device-colour", finding.RuleId);
        Assert.Equal(FindingSeverity.Error, finding.Severity);
        Assert.Contains("DeviceRGB", finding.Message);
    }

    [Fact]
    public void DeviceRgb_withRgbOutputIntent_passes()
    {
        PdfDocument doc = BuildDoc(Ascii("1 0 0 rg 0 0 100 100 re f"), outputIntentProfile: RgbProfile);
        Assert.Empty(new DeviceColourRule().Check(Ctx(doc)));
    }

    [Fact]
    public void DeviceRgb_withCmykOutputIntent_isError_wrongFamily()
    {
        PdfDocument doc = BuildDoc(Ascii("1 0 0 rg 0 0 100 100 re f"), outputIntentProfile: CmykProfile);
        Finding finding = Assert.Single(new DeviceColourRule().Check(Ctx(doc)));

        Assert.Equal("device-colour", finding.RuleId);
        Assert.Contains("DeviceRGB", finding.Message);
    }

    // ── DeviceCMYK (path colour operator "k") ────────────────────────────────

    [Fact]
    public void DeviceCmyk_withCmykOutputIntent_passes()
    {
        PdfDocument doc = BuildDoc(Ascii("0 0 0 1 k 0 0 100 100 re f"), outputIntentProfile: CmykProfile);
        Assert.Empty(new DeviceColourRule().Check(Ctx(doc)));
    }

    [Fact]
    public void DeviceCmyk_noOutputIntent_isError()
    {
        PdfDocument doc = BuildDoc(Ascii("0 0 0 1 k 0 0 100 100 re f"));
        Finding finding = Assert.Single(new DeviceColourRule().Check(Ctx(doc)));

        Assert.Equal("device-colour", finding.RuleId);
        Assert.Contains("DeviceCMYK", finding.Message);
    }

    // ── DeviceGray (path colour operator "g") ────────────────────────────────

    [Fact]
    public void DeviceGray_withAnyOutputIntent_passes()
    {
        // Gray is satisfied by an output intent of any family (here, RGB).
        PdfDocument doc = BuildDoc(Ascii("0.5 g 0 0 100 100 re f"), outputIntentProfile: RgbProfile);
        Assert.Empty(new DeviceColourRule().Check(Ctx(doc)));
    }

    [Fact]
    public void DeviceGray_noOutputIntent_isError()
    {
        PdfDocument doc = BuildDoc(Ascii("0.5 g 0 0 100 100 re f"));
        Finding finding = Assert.Single(new DeviceColourRule().Check(Ctx(doc)));

        Assert.Equal("device-colour", finding.RuleId);
        Assert.Contains("DeviceGray", finding.Message);
    }

    // ── no device colour used at all ─────────────────────────────────────────

    [Fact]
    public void NoDeviceColourUsed_noOutputIntent_passes()
    {
        // Content that paints nothing uses no colour at all. (A bare "re f" would fill in the implicit
        // initial DeviceGray — detected as device colour since slice 16b, see PreflightSlice16bTests.)
        PdfDocument doc = BuildDoc(Ascii("q Q"));
        Assert.Empty(new DeviceColourRule().Check(Ctx(doc)));
    }

    [Fact]
    public void NoDeviceColourUsed_withOutputIntent_passes()
    {
        PdfDocument doc = BuildDoc(Ascii("q Q"), outputIntentProfile: RgbProfile);
        Assert.Empty(new DeviceColourRule().Check(Ctx(doc)));
    }

    // ── explicit colour-space-name operator ("cs"/"CS") ──────────────────────

    [Fact]
    public void DeviceGrayViaColorSpaceOperator_noOutputIntent_isError()
    {
        PdfDocument doc = BuildDoc(Ascii("/DeviceGray cs"));
        Finding finding = Assert.Single(new DeviceColourRule().Check(Ctx(doc)));

        Assert.Contains("DeviceGray", finding.Message);
    }

    // ── device colour inside a Form XObject (recursion) ──────────────────────

    [Fact]
    public void DeviceRgbInsideFormXObject_isDetected()
    {
        PdfDocument doc = BuildDoc(Ascii("/Fm0 Do"),
            configureResources: (d, res) => AddFormXObject(d, res, "Fm0", Ascii("1 0 0 rg 0 0 10 10 re f")));

        Finding finding = Assert.Single(new DeviceColourRule().Check(Ctx(doc)));
        Assert.Contains("DeviceRGB", finding.Message);
    }

    [Fact]
    public void DeviceRgbInsideFormXObject_withRgbOutputIntent_passes()
    {
        PdfDocument doc = BuildDoc(Ascii("/Fm0 Do"),
            configureResources: (d, res) => AddFormXObject(d, res, "Fm0", Ascii("1 0 0 rg 0 0 10 10 re f")),
            outputIntentProfile: RgbProfile);

        Assert.Empty(new DeviceColourRule().Check(Ctx(doc)));
    }

    // ── device colour via an image XObject ───────────────────────────────────

    [Fact]
    public void DeviceCmykImageXObject_isDetected()
    {
        PdfDocument doc = BuildDoc(Ascii("/Im0 Do"),
            configureResources: (d, res) => AddImageXObject(d, res, "Im0", "DeviceCMYK"));

        Finding finding = Assert.Single(new DeviceColourRule().Check(Ctx(doc)));
        Assert.Contains("DeviceCMYK", finding.Message);
    }

    [Fact]
    public void DeviceCmykImageXObject_withCmykOutputIntent_passes()
    {
        PdfDocument doc = BuildDoc(Ascii("/Im0 Do"),
            configureResources: (d, res) => AddImageXObject(d, res, "Im0", "DeviceCMYK"),
            outputIntentProfile: CmykProfile);

        Assert.Empty(new DeviceColourRule().Check(Ctx(doc)));
    }

    // ── device colour via an inline image ────────────────────────────────────

    [Fact]
    public void DeviceRgbInlineImage_isDetected()
    {
        // BI ... ID <raw data> EI, with the abbreviated /CS /RGB colour-space key.
        byte[] content = Ascii("BI /W 1 /H 1 /BPC 8 /CS /RGB ID ")
            .Concat(new byte[] { 1, 2, 3 })
            .Concat(Ascii(" EI"))
            .ToArray();
        PdfDocument doc = BuildDoc(content);

        Finding finding = Assert.Single(new DeviceColourRule().Check(Ctx(doc)));
        Assert.Contains("DeviceRGB", finding.Message);
    }

    [Fact]
    public void DeviceRgbInlineImage_withRgbOutputIntent_passes()
    {
        byte[] content = Ascii("BI /W 1 /H 1 /BPC 8 /CS /RGB ID ")
            .Concat(new byte[] { 1, 2, 3 })
            .Concat(Ascii(" EI"))
            .ToArray();
        PdfDocument doc = BuildDoc(content, outputIntentProfile: RgbProfile);

        Assert.Empty(new DeviceColourRule().Check(Ctx(doc)));
    }

    // ── applies to all PDF/A profiles ─────────────────────────────────────────

    [Fact]
    public void AppliesToProfiles_isAllPdfA()
    {
        Assert.Equal(ConformanceProfile.AllPdfA, new DeviceColourRule().AppliesToProfiles);
    }

    // ── Default* remapping: a device operator in a scope that defines the matching Default* entry is
    //    remapped to a (device-independent) space, so it is not device colour usage ──────────────────

    [Fact]
    public void DeviceRgb_remappedByDefaultRGB_isNotFlagged()
    {
        // 'rg' with a /DefaultRGB resource entry — remapped, so no output intent is required.
        PdfDocument doc = BuildDoc(
            Ascii("1 0 0 rg 0 0 100 100 re f"),
            configureResources: (_, res) => res[new PdfName("ColorSpace")] = new PdfDictionary
            {
                [new PdfName("DefaultRGB")] = new PdfArray(new PdfName("ICCBased"), new PdfIndirectReference(11, 0)),
            });

        Assert.Empty(new DeviceColourRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)));
    }

    [Fact]
    public void DeviceRgb_withoutDefaultRGB_isStillFlagged()
    {
        // Same content, but a /DefaultGray entry does not remap RGB — the 'rg' still counts.
        PdfDocument doc = BuildDoc(
            Ascii("1 0 0 rg 0 0 100 100 re f"),
            configureResources: (_, res) => res[new PdfName("ColorSpace")] = new PdfDictionary
            {
                [new PdfName("DefaultGray")] = new PdfArray(new PdfName("ICCBased"), new PdfIndirectReference(11, 0)),
            });

        Finding finding = Assert.Single(new DeviceColourRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)));
        Assert.Equal("device-colour", finding.RuleId);
    }

    [Fact]
    public void InlineImageMask_isNotDeviceColour()
    {
        // A stencil mask (/IM true) omits /CS and is painted in the current colour; it must not be
        // read as DeviceGray (the inline-image operator defaults an absent CS to DeviceGray).
        byte[] content = Ascii("BI /IM true /W 8 /H 1 /BPC 1 ID ")
            .Concat(new byte[] { 0xFF })
            .Concat(Ascii(" EI"))
            .ToArray();
        PdfDocument doc = BuildDoc(content); // no output intent

        Assert.Empty(new DeviceColourRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)));
    }

    [Fact]
    public void FormWithOwnDefaultRGB_remapsItsOwnScope()
    {
        // The Form XObject carries its OWN /Resources defining /DefaultRGB, so the 'rg' inside it is
        // remapped independently of the page scope — no output intent required.
        PdfDocument doc = BuildDoc(
            Ascii("/Fm0 Do"),
            configureResources: (d, res) =>
            {
                var formDict = new PdfDictionary
                {
                    [new PdfName("Type")] = new PdfName("XObject"),
                    [new PdfName("Subtype")] = new PdfName("Form"),
                    [new PdfName("Resources")] = new PdfDictionary
                    {
                        [new PdfName("ColorSpace")] = new PdfDictionary
                        {
                            [new PdfName("DefaultRGB")] =
                                new PdfArray(new PdfName("ICCBased"), new PdfIndirectReference(11, 0)),
                        },
                    },
                };
                d.AddObject(10, 0, new PdfStream(formDict, Ascii("1 0 0 rg 0 0 10 10 re f")));
                res[new PdfName("XObject")] = new PdfDictionary
                {
                    [new PdfName("Fm0")] = new PdfIndirectReference(10, 0),
                };
            });

        Assert.Empty(new DeviceColourRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)));
    }
}
