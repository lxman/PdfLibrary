using System.IO;
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 19 — font-program rules (<c>font-program</c>), shared by PDF/A-2 (ISO 19005-2, 6.2.11) and
/// PDF/UA-1 (ISO 14289-1, 7.21). These lock the shipped checks against a REAL embedded font program
/// (the CC0 <c>PublicPixel.ttf</c> — every glyph advances exactly 1000 units in PDF space, so a declared
/// width of 1000 is consistent and anything else is not), never a faked <see cref="Rules"/> metric:
/// <list type="bullet">
///   <item>font metrics (6.2.11.5 / 7.21.5) — declared vs embedded advance width, TrueType + Type0;</item>
///   <item>.notdef glyph (6.2.11.8 / 7.21.8) — a shown code mapping to glyph 0, for Type0 and (via the
///     tri-state <c>ResolveSimpleGlyph</c> resolver) simple TrueType / embedded-charset CFF fonts;</item>
///   <item>glyph-present (6.2.11.4.1 t2 / 7.21.4.1 t2) — a shown simple-font code whose glyph is absent from
///     the embedded program, emitted from the same resolution as .notdef;</item>
///   <item>the resolver's FP-safe skips — a symbolic font (declared <c>/Flags</c> bit 3 or a Windows-Symbol
///     cmap) routes to <c>Unknown</c> (no finding) so an AGL-Unicode lookup gap is never a false .notdef.</item>
/// </list>
/// Real per-font-type detection breadth (classic Type1, predefined-charset CFF, CIDFontType0, the tolerance)
/// is measured by the veraPDF parity harness; these pin the profile-aware clause mapping, the messages, and
/// the FP-safe skips. There is no committed simple-CFF fixture in this repo (see the note by the CFF tests);
/// CFF-branch coverage rides on <c>PdfUaReferenceOracleTests</c> and the corpus parity harness.
/// </summary>
public class PreflightSlice19Tests
{
    // Public-domain (CC0) test font; see Resources/PublicPixel.LICENSE.txt. Every glyph advances 1000 units.
    private static byte[] FontBytes() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

    private const int ProgramWidth = 1000; // PublicPixel advance in PDF 1000-per-em space

    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    /// <summary>
    /// One-page document: object 1 is the font (referenced by the page /Resources), object 11 is the page
    /// content stream that shows <paramref name="showBytes"/>, so the used-glyph walk reaches the font.
    /// </summary>
    private static PdfDocument DocWith(PdfDictionary font, byte[] showBytes, params (int, PdfObject)[] extra)
    {
        var doc = new PdfDocument();
        doc.AddObject(1, 0, font);
        foreach ((int num, PdfObject obj) in extra)
            doc.AddObject(num, 0, obj);

        var content = new List<byte>();
        content.AddRange(Encoding.ASCII.GetBytes("BT /F0 12 Tf "));
        content.AddRange(showBytes);
        content.AddRange(Encoding.ASCII.GetBytes(" Tj ET"));
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), content.ToArray()));

        doc.AddObject(22, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(21),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary
            {
                [N("Font")] = new PdfDictionary { [N("F0")] = Ref(1) },
            },
        });
        doc.AddObject(21, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(22)),
            [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(20, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"),
            [N("Pages")] = Ref(21),
        });
        doc.Trailer.Dictionary[N("Root")] = Ref(20);
        return doc;
    }

    private static PdfStream FontFile() => new(new PdfDictionary(), FontBytes());

    /// <summary>A simple TrueType font embedding PublicPixel, showing 'A' (code 65) with the given width.</summary>
    private static PdfDocument TrueTypeDoc(int declaredWidth, bool embed = true)
    {
        var descriptor = new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEF+PublicPixel"),
            [N("Flags")] = new PdfInteger(32), // nonsymbolic
        };
        if (embed)
            descriptor[N("FontFile2")] = Ref(3);

        var font = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("ABCDEF+PublicPixel"),
            [N("FirstChar")] = new PdfInteger('A'),
            [N("LastChar")] = new PdfInteger('A'),
            [N("Widths")] = new PdfArray(new PdfInteger(declaredWidth)),
            [N("Encoding")] = N("WinAnsiEncoding"),
            [N("FontDescriptor")] = Ref(2),
        };
        return DocWith(font, Encoding.ASCII.GetBytes("(A)"), (2, descriptor), (3, FontFile()));
    }

    /// <summary>A Type0/CIDFontType2 embedding PublicPixel with an Identity CMap, showing the two-byte code.</summary>
    private static PdfDocument Type0Doc(int codeHigh, int codeLow, string encoding = "Identity-H", int declaredWidth = ProgramWidth)
    {
        var descriptor = new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEF+PublicPixel"),
            [N("Flags")] = new PdfInteger(4),
            [N("FontFile2")] = Ref(3),
        };
        var cidFont = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType2"),
            [N("BaseFont")] = N("ABCDEF+PublicPixel"),
            [N("CIDToGIDMap")] = N("Identity"),
            [N("DW")] = new PdfInteger(declaredWidth),
            [N("CIDSystemInfo")] = new PdfDictionary
            {
                [N("Registry")] = new PdfString(Encoding.Latin1.GetBytes("Adobe")),
                [N("Ordering")] = new PdfString(Encoding.Latin1.GetBytes("Identity")),
                [N("Supplement")] = new PdfInteger(0),
            },
            [N("FontDescriptor")] = Ref(2),
        };
        var font = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("ABCDEF+PublicPixel"),
            [N("Encoding")] = N(encoding),
            [N("DescendantFonts")] = new PdfArray(Ref(4)),
        };
        byte[] show = Encoding.ASCII.GetBytes($"<{codeHigh:X2}{codeLow:X2}>");
        return DocWith(font, show, (2, descriptor), (3, FontFile()), (4, cidFont));
    }

    private static Finding[] Run(PdfDocument doc, ConformanceProfile profile = ConformanceProfile.PdfA2b) =>
        new FontProgramRule().Check(new ConformanceContext(doc, profile)).ToArray();

    private static string? Clause(Finding f) => ParitySnapshot.ClauseKey(f.Clause);

    // ── metrics (6.2.11.5 / 7.21.5) ───────────────────────────────────────────────────────────────────

    [Fact]
    public void TrueType_consistent_width_passes()
    {
        Assert.Empty(Run(TrueTypeDoc(ProgramWidth)));
    }

    [Fact]
    public void TrueType_inconsistent_width_fails_metrics()
    {
        Finding f = Assert.Single(Run(TrueTypeDoc(declaredWidth: 500)));
        Assert.Equal("6.2.11.5", Clause(f));
        Assert.Contains("advance width", f.Message);
    }

    [Fact]
    public void TrueType_width_within_tolerance_passes()
    {
        // 1000 declared vs 1000 program is exact; a 5-unit slip stays under the 10-unit tolerance.
        Assert.Empty(Run(TrueTypeDoc(ProgramWidth - 5)));
    }

    [Fact]
    public void Metrics_finding_is_profile_aware_under_pdfua1()
    {
        Finding f = Assert.Single(Run(TrueTypeDoc(declaredWidth: 500), ConformanceProfile.PdfUA1));
        Assert.Equal("7.21.5", Clause(f));
        Assert.Contains("ISO 14289-1", f.Clause);
    }

    [Fact]
    public void Type0_consistent_width_passes()
    {
        // Show CID 65 (a real glyph) with the correct width — no metrics finding, no .notdef.
        Assert.Empty(Run(Type0Doc(0x00, 0x41)));
    }

    [Fact]
    public void Type0_inconsistent_width_fails_metrics()
    {
        Finding f = Assert.Single(Run(Type0Doc(0x00, 0x41, declaredWidth: 400)));
        Assert.Equal("6.2.11.5", Clause(f));
    }

    // ── .notdef (6.2.11.8 / 7.21.8) ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Type0_notdef_code_fails()
    {
        // CID 0 is .notdef; showing it renders glyph 0. Width is not compared for a missing glyph.
        Finding f = Assert.Single(Run(Type0Doc(0x00, 0x00)));
        Assert.Equal("6.2.11.8", Clause(f));
        Assert.Contains(".notdef", f.Message);
    }

    [Fact]
    public void Notdef_finding_is_profile_aware_under_pdfua1()
    {
        Finding f = Assert.Single(Run(Type0Doc(0x00, 0x00), ConformanceProfile.PdfUA1));
        Assert.Equal("7.21.8", Clause(f));
        Assert.Contains("ISO 14289-1", f.Clause);
    }

    // ── simple-font .notdef / glyph-present (slice 1) ─────────────────────────────────────────────────

    // A WinAnsi code remapped via /Differences to a glyph whose Unicode PublicPixel lacks, so the
    // program resolves it to no glyph. "ff" (the "ff" ligature, U+FB00) is in this engine's own AGL table
    // (GlyphList) so it resolves to a Unicode value, but PublicPixel — despite covering Greek, currency and
    // other symbols — has no ligature glyph for it. Guarded by Fixture_font_lacks_ff_ligature so a font
    // change can't silently invalidate it.
    //
    // Contingency note (brief's documented fallback): the brief's original choice, afii10017 (Cyrillic
    // Capital A, U+0410), failed the precondition twice over — PublicPixel actually has a glyph for U+0410
    // (gid 522), and separately "afii10017" is not an entry in this codebase's (intentionally partial) AGL
    // table, so GlyphList.GetUnicode would return null for it regardless of font content. A probe of the
    // AGL table's own entries against PublicPixel's cmap found "peseta" (U+20A7), "ff" (U+FB00), "ffi"
    // (U+FB03) and "ffl" (U+FB04) all absent; "ff" was picked as the clearest name.
    private const int AbsentUnicode = 0xFB00;
    private const string AbsentGlyphName = "ff";

    /// <param name="renderMode">When set, emits <c>{renderMode} Tr</c> before the show operator (e.g. 3 =
    /// invisible), so the RM3-exemption tests can drive the same fixture under text rendering mode 3.</param>
    /// <param name="declaredWidth">The /Widths entry for the shown code — defaults to a program-consistent
    /// value so tests that don't care about 6.2.11.5 don't pick up an incidental width finding.</param>
    private static PdfDocument TrueTypeDocShowingAbsentGlyph(int? renderMode = null, int declaredWidth = ProgramWidth)
    {
        var descriptor = new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEF+PublicPixel"),
            [N("Flags")] = new PdfInteger(32), // nonsymbolic
            [N("FontFile2")] = Ref(3),
        };
        var encoding = new PdfDictionary
        {
            [N("Type")] = N("Encoding"),
            [N("BaseEncoding")] = N("WinAnsiEncoding"),
            [N("Differences")] = new PdfArray(new PdfInteger('A'), N(AbsentGlyphName)),
        };
        var font = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("ABCDEF+PublicPixel"),
            [N("FirstChar")] = new PdfInteger('A'),
            [N("LastChar")] = new PdfInteger('A'),
            [N("Widths")] = new PdfArray(new PdfInteger(declaredWidth)),
            [N("Encoding")] = Ref(4),
            [N("FontDescriptor")] = Ref(2),
        };
        byte[] show = Encoding.ASCII.GetBytes(renderMode is { } rm ? $"{rm} Tr (A)" : "(A)");
        return DocWith(font, show, (2, descriptor), (3, FontFile()), (4, encoding));
    }

    /// <summary>A TrueType font whose <c>/Differences</c> maps the shown code directly to <c>/.notdef</c> —
    /// the faithful 6.2.11.8 path (veraPDF's predicate is the encoding glyph NAME, not a program lookup).
    /// Unlike <see cref="TrueTypeDocShowingAbsentGlyph"/> (a real name absent from the program, which fails
    /// 6.2.11.4.1 only), this must fail 6.2.11.8 only.</summary>
    private static PdfDocument TrueTypeDocShowingNotdefGlyph(int? renderMode = null)
    {
        var descriptor = new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEF+PublicPixel"),
            [N("Flags")] = new PdfInteger(32), // nonsymbolic
            [N("FontFile2")] = Ref(3),
        };
        var encoding = new PdfDictionary
        {
            [N("Type")] = N("Encoding"),
            [N("BaseEncoding")] = N("WinAnsiEncoding"),
            [N("Differences")] = new PdfArray(new PdfInteger('A'), N(".notdef")),
        };
        var font = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("ABCDEF+PublicPixel"),
            [N("FirstChar")] = new PdfInteger('A'),
            [N("LastChar")] = new PdfInteger('A'),
            [N("Widths")] = new PdfArray(new PdfInteger(ProgramWidth)),
            [N("Encoding")] = Ref(4),
            [N("FontDescriptor")] = Ref(2),
        };
        byte[] show = Encoding.ASCII.GetBytes(renderMode is { } rm ? $"{rm} Tr (A)" : "(A)");
        return DocWith(font, show, (2, descriptor), (3, FontFile()), (4, encoding));
    }

    [Fact]
    public void Fixture_font_lacks_ff_ligature()
    {
        // Precondition for the absent-glyph tests, both sides of it: the AGL table this engine ships still
        // maps "ff" to U+FB00 (so ResolveSimpleGlyph actually gets a Unicode value to look up — if a future
        // edit dropped "ff" from GlyphList's partial table, the resolver would route straight to Unknown and
        // the absent-glyph tests would false-pass for the wrong reason), and PublicPixel has no glyph for it.
        Assert.Equal("\uFB00", PdfLibrary.Fonts.GlyphList.GetUnicode(AbsentGlyphName));

        var metrics = new PdfLibrary.Fonts.Embedded.EmbeddedFontMetrics(FontBytes());
        Assert.True(metrics.IsValid);
        Assert.Equal(0, metrics.GetGlyphId((ushort)AbsentUnicode));
    }

    [Fact]
    public void Fixture_font_has_unicode_cmap_encoding()
    {
        // Precondition for the tightened TrueType guard (Finding C): every present/absent/notdef test above
        // routes through ResolveSimpleGlyph's TrueType branch, which now requires HasUnicodeCmapEncoding()
        // before trusting a cmap lookup — if PublicPixel didn't carry a Unicode-capable subtable, every one
        // of those tests would silently resolve to Unknown (no finding) for the wrong reason.
        var metrics = new PdfLibrary.Fonts.Embedded.EmbeddedFontMetrics(FontBytes());
        Assert.True(metrics.IsValid);
        Assert.True(metrics.HasUnicodeCmapEncoding());
    }

    [Fact]
    public void Simple_truetype_absent_glyph_fails_notdef()
    {
        // Faithful clause split: "ff" is a REAL glyph name absent from the program, not the literal
        // ".notdef" name — so this fails 6.2.11.4.1 (glyph-present) only, never 6.2.11.8 (see
        // Simple_truetype_notdef_glyph_fails_notdef for the ".notdef"-named path).
        Finding f = Assert.Single(Run(TrueTypeDocShowingAbsentGlyph()), x => Clause(x) == "6.2.11.4.1");
        Assert.Contains("not present", f.Message);
    }

    [Fact]
    public void Simple_truetype_notdef_glyph_fails_notdef()
    {
        Finding f = Assert.Single(Run(TrueTypeDocShowingNotdefGlyph()), x => Clause(x) == "6.2.11.8");
        Assert.Contains(".notdef", f.Message);
    }

    [Fact]
    public void Simple_truetype_notdef_glyph_notdef_is_profile_aware()
    {
        Finding f = Assert.Single(Run(TrueTypeDocShowingNotdefGlyph(), ConformanceProfile.PdfUA1),
            x => Clause(x) == "7.21.8");
        Assert.Contains("ISO 14289-1", f.Clause);
    }

    [Fact]
    public void Simple_truetype_absent_glyph_does_not_also_fail_notdef()
    {
        // Under the OLD (pre-faithful-split) model this code fired both 6.2.11.8 and 6.2.11.4.1 from one
        // gid==0 resolution. The faithful split attributes 6.2.11.8 only to a literal ".notdef" encoding
        // name, so the absent-"ff" code — a real name — must fail 6.2.11.4.1 and NOT 6.2.11.8.
        Finding[] fs = Run(TrueTypeDocShowingAbsentGlyph());
        Assert.Contains(fs, f => Clause(f) == "6.2.11.4.1");
        Assert.DoesNotContain(fs, f => Clause(f) == "6.2.11.8");
    }

    [Fact]
    public void Simple_truetype_notdef_glyph_does_not_also_fail_glyph_present()
    {
        // The mirror image: a code whose encoding name literally IS ".notdef" resolves to glyph 0, which
        // — being glyph 0 — IS present in the program, so 6.2.11.4.1 must not fire alongside 6.2.11.8.
        Finding[] fs = Run(TrueTypeDocShowingNotdefGlyph());
        Assert.Contains(fs, f => Clause(f) == "6.2.11.8");
        Assert.DoesNotContain(fs, f => Clause(f) == "6.2.11.4.1");
    }

    [Fact]
    public void Simple_present_glyph_emits_no_glyph_present_finding()
    {
        Assert.DoesNotContain(Run(TrueTypeDoc(ProgramWidth)), f => Clause(f) == "6.2.11.4.1");
    }

    // ── RM3 (invisible text) exemption ────────────────────────────────────────────────────────────────

    [Fact]
    public void Rm3_absent_glyph_is_exempt_from_glyph_present_and_metrics()
    {
        // At RM0 (default) the same fixture with a mismatched declared width fires BOTH 6.2.11.4.1
        // (absent "ff" glyph) and 6.2.11.5 (500 declared vs the real 'A' glyph's 1000-unit program advance
        // the width fallback resolves to). veraPDF exempts render-mode-3 (invisible) text from both —
        // but not from 6.2.11.8, see Rm3_notdef_glyph_still_fails_notdef.
        Finding[] rm0 = Run(TrueTypeDocShowingAbsentGlyph(declaredWidth: 500));
        Assert.Contains(rm0, f => Clause(f) == "6.2.11.4.1");
        Assert.Contains(rm0, f => Clause(f) == "6.2.11.5");

        Finding[] rm3 = Run(TrueTypeDocShowingAbsentGlyph(renderMode: 3, declaredWidth: 500));
        Assert.DoesNotContain(rm3, f => Clause(f) == "6.2.11.4.1");
        Assert.DoesNotContain(rm3, f => Clause(f) == "6.2.11.5");
    }

    [Fact]
    public void Rm3_notdef_glyph_still_fails_notdef()
    {
        // .notdef (6.2.11.8) fires "regardless of text rendering mode" — RM3 must NOT suppress it.
        Finding f = Assert.Single(Run(TrueTypeDocShowingNotdefGlyph(renderMode: 3)), x => Clause(x) == "6.2.11.8");
        Assert.Contains(".notdef", f.Message);
    }

    /// <summary>Same font/encoding as <see cref="TrueTypeDocShowingAbsentGlyph"/>, but the shown code lives
    /// inside a Form XObject invoked AFTER the page content sets <c>3 Tr</c> — the form itself sets no local
    /// <c>Tr</c>, relying entirely on the inherited render mode (ISO 32000-1 8.10.2: a Form XObject inherits
    /// the graphics state in effect at invocation), a legitimate invisible-text-layer technique. Named
    /// <paramref name="differencesName"/> so the same builder drives both the absent-glyph case (glyph-present
    /// exemption) and the literal ".notdef" case (not RM3-exempt).</summary>
    private static PdfDocument TrueTypeDocShowingCodeInsideInheritedRm3Form(string differencesName)
    {
        var descriptor = new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEF+PublicPixel"),
            [N("Flags")] = new PdfInteger(32), // nonsymbolic
            [N("FontFile2")] = Ref(3),
        };
        var encoding = new PdfDictionary
        {
            [N("Type")] = N("Encoding"),
            [N("BaseEncoding")] = N("WinAnsiEncoding"),
            [N("Differences")] = new PdfArray(new PdfInteger('A'), N(differencesName)),
        };
        var font = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("ABCDEF+PublicPixel"),
            [N("FirstChar")] = new PdfInteger('A'),
            [N("LastChar")] = new PdfInteger('A'),
            [N("Widths")] = new PdfArray(new PdfInteger(ProgramWidth)),
            [N("Encoding")] = Ref(4),
            [N("FontDescriptor")] = Ref(2),
        };

        // No local /Tr — the shown code's visibility depends entirely on the render mode inherited from
        // the page's graphics state at the point /Fm1 Do is invoked. No /Resources on the form either, so
        // the collector falls back to the page's resources (F0 is reachable from inside the form).
        var formDict = new PdfDictionary
        {
            [N("Type")] = N("XObject"),
            [N("Subtype")] = N("Form"),
            [N("BBox")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100)),
        };
        var form = new PdfStream(formDict, Encoding.ASCII.GetBytes("BT /F0 12 Tf (A) Tj ET"));

        var doc = new PdfDocument();
        doc.AddObject(1, 0, font);
        doc.AddObject(2, 0, descriptor);
        doc.AddObject(3, 0, FontFile());
        doc.AddObject(4, 0, encoding);
        doc.AddObject(5, 0, form);

        // Page sets RM3 inside its own text object, then ends it and invokes the form (Do is illegal
        // inside BT/ET) — the text-state graphics parameter persists past ET.
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes("BT /F0 12 Tf 3 Tr ET /Fm1 Do")));

        doc.AddObject(22, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(21),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary
            {
                [N("Font")] = new PdfDictionary { [N("F0")] = Ref(1) },
                [N("XObject")] = new PdfDictionary { [N("Fm1")] = Ref(5) },
            },
        });
        doc.AddObject(21, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(22)),
            [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(20, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"),
            [N("Pages")] = Ref(21),
        });
        doc.Trailer.Dictionary[N("Root")] = Ref(20);
        return doc;
    }

    [Fact]
    public void Rm3_inherited_by_nested_form_exempts_absent_glyph_from_glyph_present()
    {
        // The nested collector must seed its RenderingMode from the page's state at /Fm1 Do — not default
        // to RM0 — so the form's un-Tr'd absent-"ff" code is still recognized as invisible and 6.2.11.4.1
        // stays exempt, matching the page-level case in Rm3_absent_glyph_is_exempt_from_glyph_present_and_metrics.
        Finding[] fs = Run(TrueTypeDocShowingCodeInsideInheritedRm3Form(AbsentGlyphName));
        Assert.DoesNotContain(fs, f => Clause(f) == "6.2.11.4.1");
    }

    [Fact]
    public void Rm3_inherited_by_nested_form_still_fails_notdef()
    {
        // .notdef (6.2.11.8) is not RM3-exempt — inheriting RM3 through the form must not suppress it either.
        Finding f = Assert.Single(Run(TrueTypeDocShowingCodeInsideInheritedRm3Form(".notdef")),
            x => Clause(x) == "6.2.11.8");
        Assert.Contains(".notdef", f.Message);
    }

    [Fact]
    public void Simple_truetype_present_glyph_is_clean()
    {
        // 'A' with the default WinAnsi mapping resolves to a real PublicPixel glyph — no finding.
        Assert.Empty(Run(TrueTypeDoc(ProgramWidth)));
    }

    // NOTE — no committed, always-run unit test drives the CFF branch of ResolveSimpleGlyph (including its
    // GetGlyphIdByCffEncoding built-in-encoding fallback) below this point: this repo's Resources/ carries
    // no simple CFF (Type1C /FontFile3) font to build a fixture from. Checked before writing this note:
    // PublicPixel.ttf is a plain glyf TrueType ('\x00\x01\x00\x00' sfnt tag, not 'OTTO'); MINIMUM_Rechnung_fx.pdf
    // embeds only /FontFile2 TrueType (verified via `qpdf --qdf --object-streams=disable` + grep — three
    // /FontFile2 entries, zero /FontFile3); conformant-pdfa2b.pdf embeds no fonts at all (it is a metadata-only
    // veraPDF 6.6.4 fixture). A hand-authored CFF binary was deliberately NOT invented here — a synthetic CFF
    // is exactly the kind of guess this resolver exists to avoid, and getting its charset/CharString/built-in
    // Encoding table byte-correct without a real font as a base is not something to improvise. CFF `.notdef`
    // detection, including the GetGlyphIdByCffEncoding fallback specifically, is instead exercised by
    // PdfUaReferenceOracleTests (LocalOnly; real HelveticaNeue-*/SwiftSH-* CFF fonts in
    // PDFUA-Ref-2-01_Magazine-danish.pdf — the same file that surfaced that fallback's necessity) and by the
    // controller-run corpus parity task's CFF fail fixtures.

    /// <summary>Same as <see cref="TrueTypeDocShowingAbsentGlyph"/> but /FontDescriptor /Flags declares the
    /// font symbolic (bit 3). A symbolic font's cmap is not guaranteed to be keyed by AGL Unicode values —
    /// e.g. a (3,0) Windows-Symbol cmap is keyed at 0xF000+code, which this engine's lookup chain has no
    /// retry for — so an AGL-derived Unicode miss here must not be trusted as a confident .notdef.</summary>
    private static PdfDocument SymbolicTrueTypeDocShowingAbsentGlyph()
    {
        var descriptor = new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEF+PublicPixel"),
            [N("Flags")] = new PdfInteger(4), // symbolic
            [N("FontFile2")] = Ref(3),
        };
        var encoding = new PdfDictionary
        {
            [N("Type")] = N("Encoding"),
            [N("BaseEncoding")] = N("WinAnsiEncoding"),
            [N("Differences")] = new PdfArray(new PdfInteger('A'), N(AbsentGlyphName)),
        };
        var font = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("ABCDEF+PublicPixel"),
            [N("FirstChar")] = new PdfInteger('A'),
            [N("LastChar")] = new PdfInteger('A'),
            [N("Widths")] = new PdfArray(new PdfInteger(ProgramWidth)),
            [N("Encoding")] = Ref(4),
            [N("FontDescriptor")] = Ref(2),
        };
        return DocWith(font, Encoding.ASCII.GetBytes("(A)"), (2, descriptor), (3, FontFile()), (4, encoding));
    }

    [Fact]
    public void Simple_truetype_symbolic_absent_glyph_is_unknown_not_notdef()
    {
        // The exact same absent-glyph code that fails 6.2.11.8 for a non-symbolic font (see
        // Simple_truetype_absent_glyph_fails_notdef) must produce NO finding once the font is declared
        // symbolic: the AGL-Unicode → cmap path this rule relies on is not trustworthy for a symbolic font,
        // so ResolveSimpleGlyph must route to Unknown (skip) rather than a confident .notdef.
        Assert.Empty(Run(SymbolicTrueTypeDocShowingAbsentGlyph()));
    }

    // ── FP-safe skips ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Non_embedded_truetype_is_not_reported()
    {
        // No FontFile2 → no program to compare → the rule must stay silent (embedding is FontEmbeddingRule's job).
        Assert.Empty(Run(TrueTypeDoc(declaredWidth: 500, embed: false)));
    }

    [Fact]
    public void Type0_non_identity_cmap_is_skipped()
    {
        // A non-Identity CMap needs a CMap parser the engine lacks: the shown code cannot be mapped to a CID,
        // so even a .notdef code (0) must not be reported.
        Assert.Empty(Run(Type0Doc(0x00, 0x00, encoding: "UniGB-UCS2-H")));
    }

    [Fact]
    public void Document_with_no_used_fonts_is_clean()
    {
        var doc = new PdfDocument();
        doc.AddObject(22, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(21),
            [N("Resources")] = new PdfDictionary(),
        });
        doc.AddObject(21, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(22)),
            [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(20, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"),
            [N("Pages")] = Ref(21),
        });
        doc.Trailer.Dictionary[N("Root")] = Ref(20);
        Assert.Empty(Run(doc));
    }

    // ── profile applicability ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Applies_to_all_pdfa_and_ua_but_not_pdfx4()
    {
        ConformanceProfile applies = new FontProgramRule().AppliesToProfiles;
        Assert.Equal(ConformanceProfile.AllPdfA | ConformanceProfile.PdfUA1, applies);
        Assert.Equal(ConformanceProfile.None, applies & ConformanceProfile.PdfX4);
    }
}
