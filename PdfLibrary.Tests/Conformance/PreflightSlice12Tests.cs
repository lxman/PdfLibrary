using System;
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 12 of the preflight: the PDF/A-2u Unicode delta (ISO 19005-2, 6.2.11.7.2) —
/// <see cref="Pdfa2uToUnicodeRule"/> (every rendered code maps to Unicode) and
/// <see cref="Pdfa2uToUnicodeValuesRule"/> (those /ToUnicode values are usable). Rule-level tests over
/// hand-built one-page documents; the veraPDF 2u corpus backs the real red/green surface
/// (<see cref="CorpusOracleTests"/>).
/// </summary>
public class PreflightSlice12Tests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfString Str(string s) => new(Encoding.ASCII.GetBytes(s));
    private static PdfArray Rect(int x0, int y0, int x1, int y1) =>
        new(new PdfInteger(x0), new PdfInteger(y0), new PdfInteger(x1), new PdfInteger(y1));

    /// <summary>A one-page document whose page shows <paramref name="content"/> with font /F0 =
    /// <paramref name="fontValue"/>. <paramref name="extra"/> adds supporting objects (object numbers ≥ 20).</summary>
    private static PdfDocument Doc(PdfObject fontValue, string content, Action<PdfDocument>? extra = null)
    {
        var doc = new PdfDocument();
        extra?.Invoke(doc);
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(content)));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = fontValue } },
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(3)),
            [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    private static ConformanceContext Ctx(PdfDocument doc) => new(doc, ConformanceProfile.PdfA2u);

    private static ConformanceContext UaCtx(PdfDocument doc) => new(doc, ConformanceProfile.PdfUA1);

    private static PdfDictionary SimpleFont(PdfObject encoding, PdfObject? toUnicode = null)
    {
        var font = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type1"),
            [N("BaseFont")] = N("Helvetica"),
            [N("Encoding")] = encoding,
        };
        if (toUnicode is not null)
            font[N("ToUnicode")] = toUnicode;
        return font;
    }

    /// <summary>A minimal ToUnicode CMap stream mapping the given <paramref name="bfChar"/> entries
    /// (e.g. <c>"&lt;41&gt; &lt;0041&gt;"</c>).</summary>
    private static PdfStream ToUnicodeStream(string bfChar) => new(new PdfDictionary(), Encoding.ASCII.GetBytes(
        "/CIDInit /ProcSet findresource begin 12 dict begin begincmap\n"
        + "1 begincodespacerange <00> <FF> endcodespacerange\n"
        + $"1 beginbfchar {bfChar} endbfchar\nendcmap end end"));

    private static void AddType0(PdfDocument doc, string ordering)
    {
        doc.AddObject(21, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType2"),
            [N("BaseFont")] = N("CIDFontX"),
            [N("CIDSystemInfo")] = new PdfDictionary
            {
                [N("Registry")] = Str("Adobe"),
                [N("Ordering")] = Str(ordering),
                [N("Supplement")] = new PdfInteger(0),
            },
        });
        doc.AddObject(20, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("CIDFontX"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(21)),
        });
    }

    // ── t1: text-to-Unicode mapping (Pdfa2uToUnicodeRule) ─────────────────────

    [Fact]
    public void Simple_font_with_standard_encoding_maps_via_agl_and_passes()
    {
        // WinAnsi 'A' → glyph name "A" → Adobe Glyph List → U+0041; no /ToUnicode needed.
        var doc = Doc(SimpleFont(N("WinAnsiEncoding")), "BT /F0 12 Tf (A) Tj ET");
        Assert.Empty(new Pdfa2uToUnicodeRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Simple_font_with_unmappable_custom_glyph_name_is_flagged()
    {
        PdfObject encoding = new PdfDictionary
        {
            [N("Type")] = N("Encoding"),
            [N("BaseEncoding")] = N("WinAnsiEncoding"),
            [N("Differences")] = new PdfArray(new PdfInteger(1), N("myCustomGlyph")),
        };
        var doc = Doc(SimpleFont(encoding), "BT /F0 12 Tf <01> Tj ET");
        Finding finding = Assert.Single(new Pdfa2uToUnicodeRule().Check(Ctx(doc)));
        Assert.Equal("pdfa2u-tounicode", finding.RuleId);
    }

    [Fact]
    public void Type0_identity_font_without_tounicode_is_flagged()
    {
        var doc = Doc(Ref(20), "BT /F0 12 Tf <0001> Tj ET", d => AddType0(d, "Identity"));
        Assert.Single(new Pdfa2uToUnicodeRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Ua_font_without_unicode_mapping_is_attributed_to_clause_7_21_7()
    {
        // The UA-1 font ToUnicode-presence check belongs to the FONT clause 7.21.7 (Unicode character maps),
        // not the text/structure clause 7.2 — matching veraPDF (whose sole toUnicode rule is 7.21.7 t1) and
        // PdfLibrary's own A-2u attribution of the identical check to the font clause 6.2.11.7.2.
        PdfObject encoding = new PdfDictionary
        {
            [N("Type")] = N("Encoding"),
            [N("BaseEncoding")] = N("WinAnsiEncoding"),
            [N("Differences")] = new PdfArray(new PdfInteger(1), N("myCustomGlyph")),
        };
        var doc = Doc(SimpleFont(encoding), "BT /F0 12 Tf <01> Tj ET");
        Finding f = Assert.Single(new UaTextUnicodeRule().Check(UaCtx(doc)));
        Assert.Equal("ua-text-unicode", f.RuleId);
        Assert.Equal("ISO 14289-1:2014, 7.21.7", f.Clause);
    }

    [Fact] // conservative: a registered Adobe collection carries a derivable mapping we do not model — not flagged
    public void Type0_registered_collection_without_tounicode_passes()
    {
        var doc = Doc(Ref(20), "BT /F0 12 Tf <0001> Tj ET", d => AddType0(d, "Japan1"));
        Assert.Empty(new Pdfa2uToUnicodeRule().Check(Ctx(doc)));
    }

    [Fact] // an unmappable code that is never drawn must not fault the font
    public void Unused_unmappable_code_is_not_flagged()
    {
        PdfObject encoding = new PdfDictionary
        {
            [N("Type")] = N("Encoding"),
            [N("BaseEncoding")] = N("WinAnsiEncoding"),
            [N("Differences")] = new PdfArray(new PdfInteger(1), N("myCustomGlyph")),
        };
        // Draws 'A' (code 65, maps via AGL); code 1 (the custom glyph) is declared but never shown.
        var doc = Doc(SimpleFont(encoding), "BT /F0 12 Tf (A) Tj ET");
        Assert.Empty(new Pdfa2uToUnicodeRule().Check(Ctx(doc)));
    }

    // ── t2: ToUnicode value validity (Pdfa2uToUnicodeValuesRule) ──────────────

    [Fact]
    public void ToUnicode_mapping_to_u0000_is_flagged()
    {
        var doc = Doc(SimpleFont(N("WinAnsiEncoding"), Ref(30)), "BT /F0 12 Tf (A) Tj ET",
            d => d.AddObject(30, 0, ToUnicodeStream("<41> <0000>")));
        Finding finding = Assert.Single(new Pdfa2uToUnicodeValuesRule().Check(Ctx(doc)));
        Assert.Equal("pdfa2u-tounicode-values", finding.RuleId);
    }

    [Fact]
    public void Valid_tounicode_value_passes()
    {
        var doc = Doc(SimpleFont(N("WinAnsiEncoding"), Ref(30)), "BT /F0 12 Tf (A) Tj ET",
            d => d.AddObject(30, 0, ToUnicodeStream("<41> <0041>")));
        Assert.Empty(new Pdfa2uToUnicodeValuesRule().Check(Ctx(doc)));
        Assert.Empty(new Pdfa2uToUnicodeRule().Check(Ctx(doc)));
    }

    // ── profile gating ────────────────────────────────────────────────────────

    [Fact]
    public void The_unicode_rules_do_not_run_for_pdfa2b()
    {
        PdfObject encoding = new PdfDictionary
        {
            [N("Type")] = N("Encoding"),
            [N("BaseEncoding")] = N("WinAnsiEncoding"),
            [N("Differences")] = new PdfArray(new PdfInteger(1), N("myCustomGlyph")),
        };
        var doc = Doc(SimpleFont(encoding), "BT /F0 12 Tf <01> Tj ET");

        PreflightResult result = Preflighter.Check(doc, ConformanceProfile.PdfA2b);

        Assert.DoesNotContain(result.Findings, f => f.RuleId.StartsWith("pdfa2u", StringComparison.Ordinal));
    }
}
