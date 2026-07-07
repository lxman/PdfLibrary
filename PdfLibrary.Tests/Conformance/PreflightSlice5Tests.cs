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
/// a stream) does not count, mirroring a real veraPDF corpus fixture (see
/// <c>_TempSlice5.cs</c> smoke-test history) where a /FontFile3 reference pointed at a bare
/// <c>null</c> object.
/// </summary>
public class PreflightSlice5Tests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>An in-memory document with a single indirect font dictionary (object 1).</summary>
    private static PdfDocument DocWithFont(Action<PdfDictionary, PdfDocument> configure)
    {
        var doc = new PdfDocument();
        var font = new PdfDictionary { [new PdfName("Type")] = new PdfName("Font") };
        configure(font, doc);
        doc.AddObject(1, 0, font);
        return doc;
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
}
