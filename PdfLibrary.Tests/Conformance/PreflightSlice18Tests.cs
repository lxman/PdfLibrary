using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 18 — dictionary-level font rules (<c>font-dictionary</c>), shared by PDF/A-2 (ISO 19005-2,
/// 6.2.11) and PDF/UA-1 (ISO 14289-1, 7.21). Four groups, each pinned per fail branch and its passing
/// counterpart against synthetic font dictionaries (no corpus needed):
/// <list type="bullet">
///   <item>6.2.11.6 / 7.21.6 — simple TrueType character encodings;</item>
///   <item>6.2.11.3.1 / 7.21.3.1 — Type0 descendant CIDSystemInfo vs an embedded CMap;</item>
///   <item>6.2.11.3.2 / 7.21.3.2 — CIDFontType2 /CIDToGIDMap;</item>
///   <item>6.2.11.3.3 / 7.21.3.3 (tests 1–3) — Type0 /Encoding embedded-or-predefined, plus an embedded
///     CMap's /WMode dict-vs-body consistency and its /UseCMap referencing only predefined CMaps.</item>
/// </list>
/// The parity harness measures real detection over the veraPDF corpus; these lock every branch
/// deterministically and prove the profile-aware clause mapping.
/// </summary>
public class PreflightSlice18Tests
{
    // ── document + font-dictionary builders ──────────────────────────────────────────────────────────

    /// <summary>A one-page in-memory document whose page /Resources /Font references the given font
    /// dictionary (object 1), so the rendering-tree walk (<see cref="ConformanceContext.ReferencedFonts"/>)
    /// reaches it.</summary>
    private static PdfDocument DocWithFont(PdfDictionary font)
    {
        var doc = new PdfDocument();
        doc.AddObject(1, 0, font);
        doc.AddObject(22, 0, new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Page"),
            [new PdfName("Parent")] = new PdfIndirectReference(21, 0),
            [new PdfName("Resources")] = new PdfDictionary
            {
                [new PdfName("Font")] = new PdfDictionary { [new PdfName("F0")] = new PdfIndirectReference(1, 0) },
            },
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
        return doc;
    }

    /// <summary>An empty one-page document with no fonts at all.</summary>
    private static PdfDocument DocWithNoFonts()
    {
        var doc = new PdfDocument();
        doc.AddObject(22, 0, new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Page"),
            [new PdfName("Parent")] = new PdfIndirectReference(21, 0),
            [new PdfName("Resources")] = new PdfDictionary(),
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
        return doc;
    }

    private const int Symbolic = 4;      // FontDescriptor /Flags bit 3
    private const int Nonsymbolic = 32;  // FontDescriptor /Flags bit 6

    private static PdfDictionary TrueTypeFont(int? flags, PdfObject? encoding)
    {
        var font = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("TrueType"),
            [new PdfName("BaseFont")] = new PdfName("ABCDEF+TestFont"),
        };
        if (flags is not null)
            font[new PdfName("FontDescriptor")] = new PdfDictionary
            {
                [new PdfName("Type")] = new PdfName("FontDescriptor"),
                [new PdfName("Flags")] = new PdfInteger(flags.Value),
            };
        if (encoding is not null)
            font[new PdfName("Encoding")] = encoding;
        return font;
    }

    private static PdfDictionary Type0Font(PdfObject encoding, PdfDictionary cidFont) => new()
    {
        [new PdfName("Type")] = new PdfName("Font"),
        [new PdfName("Subtype")] = new PdfName("Type0"),
        [new PdfName("BaseFont")] = new PdfName("ABCDEF+TestCid"),
        [new PdfName("Encoding")] = encoding,
        [new PdfName("DescendantFonts")] = new PdfArray(cidFont),
    };

    private static PdfDictionary CidFont(string subtype, PdfObject? cidToGid = null, PdfDictionary? cidSystemInfo = null)
    {
        var cid = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName(subtype),
            [new PdfName("BaseFont")] = new PdfName("ABCDEF+TestCid"),
        };
        if (cidToGid is not null) cid[new PdfName("CIDToGIDMap")] = cidToGid;
        if (cidSystemInfo is not null) cid[new PdfName("CIDSystemInfo")] = cidSystemInfo;
        return cid;
    }

    private static PdfDictionary Csi(string registry, string ordering, int supplement) => new()
    {
        [new PdfName("Registry")] = new PdfString(Encoding.Latin1.GetBytes(registry)),
        [new PdfName("Ordering")] = new PdfString(Encoding.Latin1.GetBytes(ordering)),
        [new PdfName("Supplement")] = new PdfInteger(supplement),
    };

    private static PdfStream EmbeddedCMap(PdfDictionary cidSystemInfo) =>
        new(new PdfDictionary { [new PdfName("CIDSystemInfo")] = cidSystemInfo }, [0]);

    private static PdfObject EncodingDict(string? baseEncoding, PdfArray? differences = null)
    {
        var dict = new PdfDictionary { [new PdfName("Type")] = new PdfName("Encoding") };
        if (baseEncoding is not null) dict[new PdfName("BaseEncoding")] = new PdfName(baseEncoding);
        if (differences is not null) dict[new PdfName("Differences")] = differences;
        return dict;
    }

    private static Finding[] Run(PdfDocument doc, ConformanceProfile profile = ConformanceProfile.PdfA2b) =>
        new FontDictionaryRule().Check(new ConformanceContext(doc, profile)).ToArray();

    private static string? Clause(Finding f) => ParitySnapshot.ClauseKey(f.Clause);

    // ── Group 1 — 6.2.11.6 simple TrueType character encodings ───────────────────────────────────────

    [Fact]
    public void Nonsymbolic_truetype_without_encoding_fails()
    {
        Finding f = Assert.Single(Run(DocWithFont(TrueTypeFont(Nonsymbolic, encoding: null))));
        Assert.Equal("6.2.11.6", Clause(f));
        Assert.Contains("does not define an /Encoding", f.Message);
    }

    [Fact]
    public void Nonsymbolic_truetype_with_winansi_name_passes()
    {
        Assert.Empty(Run(DocWithFont(TrueTypeFont(Nonsymbolic, new PdfName("WinAnsiEncoding")))));
    }

    [Fact]
    public void Symbolic_truetype_with_encoding_fails()
    {
        Finding f = Assert.Single(Run(DocWithFont(TrueTypeFont(Symbolic, new PdfName("WinAnsiEncoding")))));
        Assert.Equal("6.2.11.6", Clause(f));
        Assert.Contains("must not define an /Encoding", f.Message);
    }

    [Fact]
    public void Symbolic_truetype_without_encoding_passes()
    {
        Assert.Empty(Run(DocWithFont(TrueTypeFont(Symbolic, encoding: null))));
    }

    [Fact]
    public void Nonsymbolic_truetype_encoding_dict_without_base_encoding_fails()
    {
        Finding f = Assert.Single(Run(DocWithFont(TrueTypeFont(Nonsymbolic, EncodingDict(baseEncoding: null)))));
        Assert.Equal("6.2.11.6", Clause(f));
        Assert.Contains("no /BaseEncoding", f.Message);
    }

    [Fact]
    public void Nonsymbolic_truetype_encoding_dict_with_winansi_base_passes()
    {
        Assert.Empty(Run(DocWithFont(TrueTypeFont(Nonsymbolic, EncodingDict("WinAnsiEncoding")))));
    }

    [Fact]
    public void Nonsymbolic_truetype_encoding_dict_with_bad_base_encoding_fails()
    {
        Finding f = Assert.Single(Run(DocWithFont(TrueTypeFont(Nonsymbolic, EncodingDict("Custom")))));
        Assert.Equal("6.2.11.6", Clause(f));
        Assert.Contains("MacRomanEncoding", f.Message);
    }

    [Fact]
    public void Nonsymbolic_truetype_differences_with_unknown_glyph_fails()
    {
        // "grav" is not in the Adobe Glyph List (the valid name is "grave"), so it is reported.
        PdfArray diffs = new(new PdfInteger(96), new PdfName("grav"));
        Finding f = Assert.Single(Run(DocWithFont(TrueTypeFont(Nonsymbolic, EncodingDict("WinAnsiEncoding", diffs)))));
        Assert.Equal("6.2.11.6", Clause(f));
        Assert.Contains("Adobe Glyph List", f.Message);
    }

    [Fact]
    public void Nonsymbolic_truetype_differences_with_agl_and_algorithmic_names_passes()
    {
        // AGL name + uniXXXX + uXXXXXX + .notdef are all recognised, so no finding.
        PdfArray diffs = new(
            new PdfInteger(96), new PdfName("grave"),
            new PdfInteger(97), new PdfName("uni00C0"),
            new PdfInteger(98), new PdfName("u1F600"),
            new PdfInteger(99), new PdfName(".notdef"));
        Assert.Empty(Run(DocWithFont(TrueTypeFont(Nonsymbolic, EncodingDict("WinAnsiEncoding", diffs)))));
    }

    // ── Group 2 — 6.2.11.3.1 CIDSystemInfo compatibility (embedded CMap) ──────────────────────────────

    [Fact]
    public void Type0_embedded_cmap_registry_mismatch_fails()
    {
        PdfDictionary font = Type0Font(
            EmbeddedCMap(Csi("Adobe", "Korea1", 2)),
            CidFont("CIDFontType0", cidSystemInfo: Csi("adobe", "Korea1", 2)));
        Finding f = Assert.Single(Run(DocWithFont(font)));
        Assert.Equal("6.2.11.3.1", Clause(f));
        Assert.Contains("Registry", f.Message);
    }

    [Fact]
    public void Type0_embedded_cmap_ordering_mismatch_fails()
    {
        PdfDictionary font = Type0Font(
            EmbeddedCMap(Csi("Adobe", "Korea1", 2)),
            CidFont("CIDFontType0", cidSystemInfo: Csi("Adobe", "China1", 2)));
        Finding f = Assert.Single(Run(DocWithFont(font)));
        Assert.Equal("6.2.11.3.1", Clause(f));
        Assert.Contains("Ordering", f.Message);
    }

    [Fact]
    public void Type0_embedded_cmap_supplement_greater_fails()
    {
        PdfDictionary font = Type0Font(
            EmbeddedCMap(Csi("Adobe", "Korea1", 2)),
            CidFont("CIDFontType0", cidSystemInfo: Csi("Adobe", "Korea1", 3)));
        Finding f = Assert.Single(Run(DocWithFont(font)));
        Assert.Equal("6.2.11.3.1", Clause(f));
        Assert.Contains("Supplement", f.Message);
    }

    [Fact]
    public void Type0_embedded_cmap_supplement_less_passes()
    {
        // Regression guard: a CIDFont /Supplement BELOW the CMap's is conformant (this is the shape of
        // veraPDF's own 6-2-11-3-1-t01-pass-d fixture). Only the greater-than direction fires; flipping the
        // comparison would false-positive on that fixture. See the direction note in FontDictionaryRule.
        PdfDictionary font = Type0Font(
            EmbeddedCMap(Csi("Adobe", "Korea1", 3)),
            CidFont("CIDFontType0", cidSystemInfo: Csi("Adobe", "Korea1", 2)));
        Assert.Empty(Run(DocWithFont(font)));
    }

    [Fact]
    public void Type0_embedded_cmap_matching_cidsysteminfo_passes()
    {
        PdfDictionary font = Type0Font(
            EmbeddedCMap(Csi("Adobe", "Korea1", 2)),
            CidFont("CIDFontType0", cidSystemInfo: Csi("Adobe", "Korea1", 2)));
        Assert.Empty(Run(DocWithFont(font)));
    }

    [Fact]
    public void Type0_identity_name_with_mismatched_ordering_passes()
    {
        // A predefined Identity-H CMap imposes no CIDSystemInfo constraint — the check is skipped.
        PdfDictionary font = Type0Font(
            new PdfName("Identity-H"),
            CidFont("CIDFontType0", cidSystemInfo: Csi("Adobe", "Japan1", 2)));
        Assert.Empty(Run(DocWithFont(font)));
    }

    // ── Group 3 — 6.2.11.3.2 CIDToGIDMap (CIDFontType2) ──────────────────────────────────────────────

    [Fact]
    public void Type0_cidfonttype2_without_cidtogidmap_fails()
    {
        PdfDictionary font = Type0Font(new PdfName("Identity-H"), CidFont("CIDFontType2"));
        Finding f = Assert.Single(Run(DocWithFont(font)));
        Assert.Equal("6.2.11.3.2", Clause(f));
        Assert.Contains("does not define /CIDToGIDMap", f.Message);
    }

    [Fact]
    public void Type0_cidfonttype2_with_non_identity_name_fails()
    {
        PdfDictionary font = Type0Font(new PdfName("Identity-H"),
            CidFont("CIDFontType2", cidToGid: new PdfName("NoIdentity")));
        Finding f = Assert.Single(Run(DocWithFont(font)));
        Assert.Equal("6.2.11.3.2", Clause(f));
        Assert.Contains("Identity", f.Message);
    }

    [Fact]
    public void Type0_cidfonttype2_with_identity_name_passes()
    {
        PdfDictionary font = Type0Font(new PdfName("Identity-H"),
            CidFont("CIDFontType2", cidToGid: new PdfName("Identity")));
        Assert.Empty(Run(DocWithFont(font)));
    }

    [Fact]
    public void Type0_cidfonttype2_with_stream_map_passes()
    {
        PdfDictionary font = Type0Font(new PdfName("Identity-H"),
            CidFont("CIDFontType2", cidToGid: new PdfStream(new PdfDictionary(), [0, 0])));
        Assert.Empty(Run(DocWithFont(font)));
    }

    [Fact]
    public void Type0_cidfonttype0_without_cidtogidmap_passes()
    {
        // CIDFontType0 has no CIDToGIDMap requirement — the rule must not apply there.
        PdfDictionary font = Type0Font(new PdfName("Identity-H"), CidFont("CIDFontType0"));
        Assert.Empty(Run(DocWithFont(font)));
    }

    // ── Group 4 — 6.2.11.3.3 CMap embedded-or-predefined ─────────────────────────────────────────────

    [Fact]
    public void Type0_non_predefined_cmap_name_fails()
    {
        PdfDictionary font = Type0Font(new PdfName("Adobe-Korea1-2"), CidFont("CIDFontType0"));
        Finding f = Assert.Single(Run(DocWithFont(font)));
        Assert.Equal("6.2.11.3.3", Clause(f));
        Assert.Contains("predefined CMap", f.Message);
    }

    [Fact]
    public void Type0_identity_h_name_passes()
    {
        PdfDictionary font = Type0Font(new PdfName("Identity-H"), CidFont("CIDFontType0"));
        Assert.Empty(Run(DocWithFont(font)));
    }

    [Fact]
    public void Type0_embedded_cmap_stream_passes()
    {
        PdfDictionary font = Type0Font(EmbeddedCMap(Csi("Adobe", "Japan1", 4)), CidFont("CIDFontType0"));
        Assert.Empty(Run(DocWithFont(font)));
    }

    // ── Group 4 tests 2 & 3 — CMap WMode / UseCMap (6.2.11.3.3 / 7.21.3.3) — slice 3 ──────────────────

    // An embedded CMap stream: dict /WMode = dictWMode; body contains "/WMode <bodyWMode> def".
    private static PdfStream CMapWithWMode(int dictWMode, int bodyWMode)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("CMap"),
            [new PdfName("CMapName")] = new PdfName("Custom-CMap"),
            [new PdfName("WMode")] = new PdfInteger(dictWMode),
        };
        return new PdfStream(dict, Encoding.ASCII.GetBytes($"begincmap /WMode {bodyWMode} def endcmap"));
    }

    // An embedded CMap stream whose /UseCMap references another CMap (a stream named referencedName,
    // or a predefined name when referencedName matches one).
    private static PdfStream CMapUsing(PdfObject useCMap)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("CMap"),
            [new PdfName("CMapName")] = new PdfName("Custom-CMap"),
            [new PdfName("UseCMap")] = useCMap,
        };
        return new PdfStream(dict, Encoding.ASCII.GetBytes("begincmap endcmap"));
    }

    private static PdfStream NamedCMap(string cmapName) =>
        new(new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("CMap"),
            [new PdfName("CMapName")] = new PdfName(cmapName),
        }, Encoding.ASCII.GetBytes("begincmap endcmap"));

    [Fact]
    public void Type0_cmap_wmode_mismatch_fails()
    {
        PdfDictionary font = Type0Font(CMapWithWMode(dictWMode: 1, bodyWMode: 0), CidFont("CIDFontType0"));
        Finding f = Assert.Single(Run(DocWithFont(font)));
        Assert.Equal("6.2.11.3.3", Clause(f));
        Assert.Contains("WMode", f.Message);
    }

    [Fact]
    public void Type0_cmap_wmode_consistent_passes()
    {
        PdfDictionary font = Type0Font(CMapWithWMode(dictWMode: 0, bodyWMode: 0), CidFont("CIDFontType0"));
        Assert.Empty(Run(DocWithFont(font)));
    }

    [Fact]
    public void Type0_cmap_wmode_mismatch_is_profile_aware()
    {
        PdfDictionary font = Type0Font(CMapWithWMode(dictWMode: 1, bodyWMode: 0), CidFont("CIDFontType0"));
        Finding f = Assert.Single(Run(DocWithFont(font), ConformanceProfile.PdfUA1));
        Assert.Equal("7.21.3.3", Clause(f));
        Assert.Contains("ISO 14289-1", f.Clause);
    }

    [Fact]
    public void Type0_cmap_uses_nonpredefined_cmap_fails()
    {
        PdfDictionary font = Type0Font(CMapUsing(NamedCMap("Custom-Other")), CidFont("CIDFontType0"));
        Finding f = Assert.Single(Run(DocWithFont(font)));
        Assert.Equal("6.2.11.3.3", Clause(f));
        Assert.Contains("UseCMap", f.Message);
    }

    [Fact]
    public void Type0_cmap_uses_predefined_cmap_passes()
    {
        // /UseCMap /Identity-H (a predefined name) is allowed.
        PdfDictionary font = Type0Font(CMapUsing(new PdfName("Identity-H")), CidFont("CIDFontType0"));
        Assert.Empty(Run(DocWithFont(font)));
    }

    [Fact]
    public void Type0_cmap_without_wmode_or_usecmap_is_clean()
    {
        // A bare embedded CMap (no dict /WMode, no /UseCMap) triggers neither check.
        var cmap = new PdfStream(new PdfDictionary { [new PdfName("Type")] = new PdfName("CMap") },
            Encoding.ASCII.GetBytes("begincmap endcmap"));
        Assert.Empty(Run(DocWithFont(Type0Font(cmap, CidFont("CIDFontType0")))));
    }

    [Fact]
    public void Type0_cmap_wmode_in_a_comment_does_not_false_fire()
    {
        // FP-safety: a conformant CMap (dict /WMode 0, real body "/WMode 0 def") whose PostScript header
        // comment happens to mention a different "/WMode 1 def" must NOT be read as a mismatch — the body
        // scan must ignore %-comments, matching veraPDF's CMap tokenizer.
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("CMap"),
            [new PdfName("CMapName")] = new PdfName("Custom-CMap"),
            [new PdfName("WMode")] = new PdfInteger(0),
        };
        var cmap = new PdfStream(dict, Encoding.ASCII.GetBytes(
            "%%Title: sets /WMode 1 def in the vertical variant\nbegincmap /WMode 0 def endcmap"));
        Assert.Empty(Run(DocWithFont(Type0Font(cmap, CidFont("CIDFontType0")))));
    }

    [Fact]
    public void Type0_cmap_wmode_dict_only_body_absent_is_clean()
    {
        // FP-safety: dict carries /WMode but the body declares none — the comparison needs both sides, so
        // an absent body /WMode skips (never defaults to 0 and fires).
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("CMap"),
            [new PdfName("CMapName")] = new PdfName("Custom-CMap"),
            [new PdfName("WMode")] = new PdfInteger(1),
        };
        var cmap = new PdfStream(dict, Encoding.ASCII.GetBytes("begincmap endcmap"));
        Assert.Empty(Run(DocWithFont(Type0Font(cmap, CidFont("CIDFontType0")))));
    }

    // ── general ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Document_with_no_fonts_is_clean()
    {
        Assert.Empty(Run(DocWithNoFonts()));
    }

    [Fact]
    public void Profile_aware_clause_emits_7_21_under_pdfua1()
    {
        // The same non-symbolic-TrueType-without-encoding failure cites ISO 14289-1 clause 7.21.6 under UA.
        Finding f = Assert.Single(Run(DocWithFont(TrueTypeFont(Nonsymbolic, encoding: null)),
            ConformanceProfile.PdfUA1));
        Assert.Equal("7.21.6", Clause(f));
        Assert.Contains("ISO 14289-1", f.Clause);
    }

    [Fact]
    public void Applies_to_all_pdfa_and_ua_but_not_pdfx4()
    {
        ConformanceProfile applies = new FontDictionaryRule().AppliesToProfiles;
        Assert.Equal(ConformanceProfile.AllPdfA | ConformanceProfile.PdfUA1, applies);
        Assert.Equal(ConformanceProfile.None, applies & ConformanceProfile.PdfX4);
    }
}
