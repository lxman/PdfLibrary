using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 5 of the preflight: font embedding (<c>font-embedded</c>). Every font used for rendering
/// must have its font program embedded, except Type3 (glyphs are content streams) and Type0 (the
/// composite wrapper, whose embedding is verified on its descendant CIDFont as a separate font
/// object). "Embedded" requires the /FontDescriptor's FontFile/FontFile2/FontFile3 key to resolve
/// to an actual stream object — a dangling indirect reference (key present, target missing or not
/// a stream) does not count, mirroring the real veraPDF corpus fixture
/// <c>6-2-11-4-1-t01-fail-a.pdf</c> whose /FontFile3 reference points at a bare <c>null</c> object.
/// </summary>
public class PreflightSlice5Tests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>An in-memory document with a single indirect font dictionary (object 1), referenced from a
    /// page's /Resources /Font so the rendering-tree walk (<see cref="ConformanceContext.ReferencedFonts"/>)
    /// reaches it.</summary>
    private static PdfDocument DocWithFont(Action<PdfDictionary, PdfDocument> configure)
    {
        var doc = new PdfDocument();
        var font = new PdfDictionary { [new PdfName("Type")] = new PdfName("Font") };
        configure(font, doc);
        doc.AddObject(1, 0, font);
        WirePageWithFonts(doc, new PdfDictionary { [new PdfName("F0")] = new PdfIndirectReference(1, 0) });
        return doc;
    }

    /// <summary>Adds a catalog/pages/page (objects 20–22) whose page /Resources /Font is
    /// <paramref name="fontDict"/>, making those fonts reachable for rendering.</summary>
    private static void WirePageWithFonts(PdfDocument doc, PdfDictionary fontDict)
    {
        doc.AddObject(22, 0, new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Page"),
            [new PdfName("Parent")] = new PdfIndirectReference(21, 0),
            [new PdfName("Resources")] = new PdfDictionary { [new PdfName("Font")] = fontDict },
        });
        doc.AddObject(21, 0, new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Pages"),
            [new PdfName("Kids")] = new PdfArray(new PdfIndirectReference(22, 0)),
            [new PdfName("Count")] = new PdfInteger(1),
        });
        doc.AddObject(20, 0, new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Catalog"),
            [new PdfName("Pages")] = new PdfIndirectReference(21, 0),
        });
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(20, 0);
    }

    /// <summary>
    /// A /FontDescriptor whose <paramref name="fontFileKey"/> is an indirect reference to a real
    /// (indirect) stream object added to <paramref name="doc"/> — a genuinely embedded font program.
    /// </summary>
    private static PdfDictionary EmbeddedDescriptor(PdfDocument doc, string fontFileKey, int streamObjectNumber = 2)
    {
        doc.AddObject(streamObjectNumber, 0, new PdfStream(new PdfDictionary(), [1, 2, 3]));
        return new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("FontDescriptor"),
            [new PdfName(fontFileKey)] = new PdfIndirectReference(streamObjectNumber, 0),
        };
    }

    /// <summary>A /FontDescriptor with no FontFile* key at all (not embedded).</summary>
    private static PdfDictionary BareDescriptor() =>
        new() { [new PdfName("Type")] = new PdfName("FontDescriptor") };

    /// <summary>
    /// A /FontDescriptor whose <paramref name="fontFileKey"/> is an indirect reference that resolves
    /// to <see cref="PdfNull"/> rather than a stream — the key is present, but nothing is actually
    /// embedded. This is the exact shape of the veraPDF corpus fixture
    /// <c>6-2-11-4-1-t01-fail-a.pdf</c>, whose /FontFile3 points at a bare "null" object.
    /// </summary>
    private static PdfDictionary DanglingDescriptor(PdfDocument doc, string fontFileKey, int nullObjectNumber = 2)
    {
        doc.AddObject(nullObjectNumber, 0, PdfNull.Instance);
        return new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("FontDescriptor"),
            [new PdfName(fontFileKey)] = new PdfIndirectReference(nullObjectNumber, 0),
        };
    }

    private static ConformanceContext Ctx(PdfDocument doc, ConformanceProfile profile = ConformanceProfile.PdfA2b) =>
        new(doc, profile);

    // ── embedded: no finding ─────────────────────────────────────────────────

    [Fact]
    public void Type1WithFontFile_passes()
    {
        PdfDocument doc = DocWithFont((f, d) =>
        {
            f[new PdfName("Subtype")] = new PdfName("Type1");
            f[new PdfName("FontDescriptor")] = EmbeddedDescriptor(d, "FontFile");
        });
        Assert.Empty(new FontEmbeddingRule().Check(Ctx(doc)));
    }

    [Fact]
    public void TrueTypeWithFontFile2_passes()
    {
        PdfDocument doc = DocWithFont((f, d) =>
        {
            f[new PdfName("Subtype")] = new PdfName("TrueType");
            f[new PdfName("FontDescriptor")] = EmbeddedDescriptor(d, "FontFile2");
        });
        Assert.Empty(new FontEmbeddingRule().Check(Ctx(doc)));
    }

    [Fact]
    public void CidFontType2WithFontFile2_passes()
    {
        PdfDocument doc = DocWithFont((f, d) =>
        {
            f[new PdfName("Subtype")] = new PdfName("CIDFontType2");
            f[new PdfName("FontDescriptor")] = EmbeddedDescriptor(d, "FontFile2");
        });
        Assert.Empty(new FontEmbeddingRule().Check(Ctx(doc)));
    }

    [Fact]
    public void CidFontType0WithFontFile3_passes()
    {
        PdfDocument doc = DocWithFont((f, d) =>
        {
            f[new PdfName("Subtype")] = new PdfName("CIDFontType0");
            f[new PdfName("FontDescriptor")] = EmbeddedDescriptor(d, "FontFile3");
        });
        Assert.Empty(new FontEmbeddingRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Type1WithIndirectFontDescriptor_passes()
    {
        // The /FontDescriptor is itself an indirect object — exercises the resolve branch.
        var doc = new PdfDocument();
        doc.AddObject(2, 0, new PdfStream(new PdfDictionary(), [1, 2, 3])); // font program
        doc.AddObject(3, 0, new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("FontDescriptor"),
            [new PdfName("FontFile")] = new PdfIndirectReference(2, 0),
        });
        doc.AddObject(1, 0, new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("Type1"),
            [new PdfName("FontDescriptor")] = new PdfIndirectReference(3, 0),
        });
        WirePageWithFonts(doc, new PdfDictionary { [new PdfName("F0")] = new PdfIndirectReference(1, 0) });

        Assert.Empty(new FontEmbeddingRule().Check(Ctx(doc)));
    }

    // ── exempt subtypes: no finding ──────────────────────────────────────────

    [Fact]
    public void Type3_noDescriptor_isExempt()
    {
        PdfDocument doc = DocWithFont((f, _) => f[new PdfName("Subtype")] = new PdfName("Type3"));
        Assert.Empty(new FontEmbeddingRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Type0_noDescriptor_isExemptWrapper()
    {
        PdfDocument doc = DocWithFont((f, _) => f[new PdfName("Subtype")] = new PdfName("Type0"));
        Assert.Empty(new FontEmbeddingRule().Check(Ctx(doc)));
    }

    // ── not embedded: one Error ──────────────────────────────────────────────

    [Fact]
    public void Type1WithDescriptorButNoFontFile_isError()
    {
        PdfDocument doc = DocWithFont((f, _) =>
        {
            f[new PdfName("Subtype")] = new PdfName("Type1");
            f[new PdfName("FontDescriptor")] = BareDescriptor();
        });
        Finding finding = Assert.Single(new FontEmbeddingRule().Check(Ctx(doc)));

        Assert.Equal("font-embedded", finding.RuleId);
        Assert.Equal(FindingSeverity.Error, finding.Severity);
    }

    [Fact]
    public void Type1WithNoFontDescriptor_isError()
    {
        PdfDocument doc = DocWithFont((f, _) => f[new PdfName("Subtype")] = new PdfName("Type1"));
        Finding finding = Assert.Single(new FontEmbeddingRule().Check(Ctx(doc)));

        Assert.Equal("font-embedded", finding.RuleId);
        Assert.Equal(FindingSeverity.Error, finding.Severity);
    }

    [Fact]
    public void MMType1WithoutFontFile_isError()
    {
        PdfDocument doc = DocWithFont((f, _) => f[new PdfName("Subtype")] = new PdfName("MMType1"));
        Finding finding = Assert.Single(new FontEmbeddingRule().Check(Ctx(doc)));

        Assert.Equal("font-embedded", finding.RuleId);
        Assert.Equal(FindingSeverity.Error, finding.Severity);
    }

    [Fact]
    public void CidFontType2WithoutFontFile_isError()
    {
        PdfDocument doc = DocWithFont((f, _) => f[new PdfName("Subtype")] = new PdfName("CIDFontType2"));
        Finding finding = Assert.Single(new FontEmbeddingRule().Check(Ctx(doc)));

        Assert.Equal("font-embedded", finding.RuleId);
        Assert.Equal(FindingSeverity.Error, finding.Severity);
    }

    [Fact]
    public void FontFileKeyPresentButDanglingReference_isError()
    {
        // Regression test for the exact shape of veraPDF corpus fixture 6-2-11-4-1-t01-fail-a.pdf:
        // /FontDescriptor has a /FontFile3 key, but it points at a "null" object rather than a
        // stream, so the font is still not actually embedded.
        PdfDocument doc = DocWithFont((f, d) =>
        {
            f[new PdfName("Subtype")] = new PdfName("Type1");
            f[new PdfName("FontDescriptor")] = DanglingDescriptor(d, "FontFile3");
        });
        Finding finding = Assert.Single(new FontEmbeddingRule().Check(Ctx(doc)));

        Assert.Equal("font-embedded", finding.RuleId);
        Assert.Equal(FindingSeverity.Error, finding.Severity);
    }

    // ── multiple fonts: only the non-embedded one is flagged ─────────────────

    [Fact]
    public void TwoFonts_oneEmbeddedOneNot_exactlyOneError()
    {
        var doc = new PdfDocument();
        var embeddedDescriptor = EmbeddedDescriptor(doc, "FontFile2", streamObjectNumber: 3);
        var embedded = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("TrueType"),
            [new PdfName("FontDescriptor")] = embeddedDescriptor,
        };
        var notEmbedded = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("Type1"),
        };
        doc.AddObject(1, 0, embedded);
        doc.AddObject(2, 0, notEmbedded);
        WirePageWithFonts(doc, new PdfDictionary
        {
            [new PdfName("F0")] = new PdfIndirectReference(1, 0),
            [new PdfName("F1")] = new PdfIndirectReference(2, 0),
        });

        Finding finding = Assert.Single(new FontEmbeddingRule().Check(Ctx(doc)));
        Assert.Equal("font-embedded", finding.RuleId);
    }

    // ── applies across profiles (PdfX4 included in AppliesToProfiles) ────────

    [Fact]
    public void NotEmbedded_underPdfX4_isError()
    {
        PdfDocument doc = DocWithFont((f, _) => f[new PdfName("Subtype")] = new PdfName("Type1"));
        Finding finding = Assert.Single(new FontEmbeddingRule().Check(Ctx(doc, ConformanceProfile.PdfX4)));

        Assert.Equal("font-embedded", finding.RuleId);
        Assert.Equal(FindingSeverity.Error, finding.Severity);
    }

    // ── rendering-tree reference paths (whole-branch review regressions) ──────

    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfDictionary NonEmbeddedType1() => new()
    {
        [N("Type")] = N("Font"), [N("Subtype")] = N("Type1"),
    };

    [Fact] // fix: /Resources inherited from a grandparent /Pages node must still be walked
    public void Font_inherited_from_grandparent_resources_is_checked()
    {
        var doc = new PdfDocument();
        doc.AddObject(1, 0, NonEmbeddedType1());
        doc.AddObject(30, 0, new PdfDictionary { [N("Type")] = N("Page"), [N("Parent")] = Ref(31) });
        doc.AddObject(31, 0, new PdfDictionary // parent — no /Resources
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(30)), [N("Count")] = new PdfInteger(1),
            [N("Parent")] = Ref(32),
        });
        doc.AddObject(32, 0, new PdfDictionary // grandparent — carries the inherited /Resources
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(31)), [N("Count")] = new PdfInteger(1),
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = Ref(1) } },
        });
        doc.AddObject(20, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(32) });
        doc.Trailer.Dictionary[N("Root")] = Ref(20);

        Assert.Single(new FontEmbeddingRule().Check(Ctx(doc)));
    }

    [Fact] // fix: a font referenced only via /ExtGState /Font must be walked
    public void Font_referenced_via_extgstate_is_checked()
    {
        var doc = new PdfDocument();
        doc.AddObject(1, 0, NonEmbeddedType1());
        var extGState = new PdfDictionary { [N("Font")] = new PdfArray(Ref(1), new PdfInteger(12)) };
        WirePageWithResources(doc, new PdfDictionary
        {
            [N("ExtGState")] = new PdfDictionary { [N("GS0")] = extGState },
        });
        Assert.Single(new FontEmbeddingRule().Check(Ctx(doc)));
    }

    [Fact] // fix: under NeedAppearances the viewer renders AcroForm /DR fonts — they must be checked
    public void Font_in_acroform_DR_under_needAppearances_is_checked()
    {
        var doc = AcroFormDrDoc(needAppearances: true);
        Assert.Single(new FontEmbeddingRule().Check(Ctx(doc, ConformanceProfile.PdfX4)));
    }

    [Fact] // no regression: without NeedAppearances the /DR pool is not necessarily rendered
    public void Font_in_acroform_DR_without_needAppearances_is_not_checked()
    {
        var doc = AcroFormDrDoc(needAppearances: false);
        Assert.Empty(new FontEmbeddingRule().Check(Ctx(doc, ConformanceProfile.PdfX4)));
    }

    /// <summary>Adds a catalog/pages/page (20–22) whose page /Resources is exactly <paramref name="resources"/>.</summary>
    private static void WirePageWithResources(PdfDocument doc, PdfDictionary resources)
    {
        doc.AddObject(22, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(21), [N("Resources")] = resources,
        });
        doc.AddObject(21, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(22)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(20, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(21) });
        doc.Trailer.Dictionary[N("Root")] = Ref(20);
    }

    private static PdfDocument AcroFormDrDoc(bool needAppearances)
    {
        var doc = new PdfDocument();
        doc.AddObject(1, 0, NonEmbeddedType1());
        doc.AddObject(22, 0, new PdfDictionary { [N("Type")] = N("Page"), [N("Parent")] = Ref(21) });
        doc.AddObject(21, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(22)), [N("Count")] = new PdfInteger(1),
        });
        var acroForm = new PdfDictionary
        {
            [N("DR")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = Ref(1) } },
        };
        if (needAppearances) acroForm[N("NeedAppearances")] = PdfBoolean.FromValue(true);
        doc.AddObject(20, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"), [N("Pages")] = Ref(21), [N("AcroForm")] = acroForm,
        });
        doc.Trailer.Dictionary[N("Root")] = Ref(20);
        return doc;
    }
}
