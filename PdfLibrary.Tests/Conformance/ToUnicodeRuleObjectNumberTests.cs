using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Task 1 of F-1 (font remediation spine): both PDF/A-2u ToUnicode rules must name the offending font's
/// indirect object number on their findings, so downstream remediation can map a finding back to a font.
/// Every other font-facing conformance rule already does this (e.g. <see cref="FontEmbeddingRule"/>).
///
/// Fixtures are hand-built one-page documents, matching the established convention for these two rules
/// (see <c>PreflightSlice12Tests.cs</c>) rather than files on disk: no document under <c>TestPDFs/</c>
/// naturally trips either rule (confirmed by scanning the corpus — the only hits were the pre-existing
/// encrypted-file load failures, unrelated to ToUnicode). Both scenarios below are proven finding-triggers
/// by <c>PreflightSlice12Tests.cs</c> already (<c>Type0_identity_font_without_tounicode_is_flagged</c> and
/// <c>ToUnicode_mapping_to_u0000_is_flagged</c>); this file's only addition is making the font dictionary an
/// INDIRECT object (added via <see cref="PdfDocument.AddObject"/>) so <c>Finding.ObjectNumber</c> has
/// something real to name.
/// </summary>
public class ToUnicodeRuleObjectNumberTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
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

    /// <summary>An indirect Type0/Identity-H font (object 20) with no /ToUnicode — proven to trip
    /// <c>pdfa2u-tounicode</c> by <c>PreflightSlice12Tests.Type0_identity_font_without_tounicode_is_flagged</c>.</summary>
    private static void AddType0(PdfDocument doc, string ordering)
    {
        doc.AddObject(21, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType2"),
            [N("BaseFont")] = N("CIDFontX"),
            [N("CIDSystemInfo")] = new PdfDictionary
            {
                [N("Registry")] = new PdfString(Encoding.ASCII.GetBytes("Adobe")),
                [N("Ordering")] = new PdfString(Encoding.ASCII.GetBytes(ordering)),
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

    /// <summary>A minimal ToUnicode CMap stream mapping the given <paramref name="bfChar"/> entries
    /// (e.g. <c>"&lt;41&gt; &lt;0000&gt;"</c>).</summary>
    private static PdfStream ToUnicodeStream(string bfChar) => new(new PdfDictionary(), Encoding.ASCII.GetBytes(
        "/CIDInit /ProcSet findresource begin 12 dict begin begincmap\n"
        + "1 begincodespacerange <00> <FF> endcodespacerange\n"
        + $"1 beginbfchar {bfChar} endbfchar\nendcmap end end"));

    /// <summary>An indirect simple Type1 font (object 40) whose /ToUnicode (object 30) maps the drawn code
    /// to the forbidden U+0000 — proven to trip <c>pdfa2u-tounicode-values</c> by
    /// <c>PreflightSlice12Tests.ToUnicode_mapping_to_u0000_is_flagged</c>.</summary>
    private static void AddSimpleFontWithForbiddenToUnicode(PdfDocument doc)
    {
        doc.AddObject(30, 0, ToUnicodeStream("<41> <0000>"));
        doc.AddObject(40, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type1"),
            [N("BaseFont")] = N("Helvetica"),
            [N("Encoding")] = N("WinAnsiEncoding"),
            [N("ToUnicode")] = Ref(30),
        });
    }

    [Fact]
    public void Pdfa2uToUnicode_NamesTheOffendingFontObject()
    {
        PdfDocument doc = Doc(Ref(20), "BT /F0 12 Tf <0001> Tj ET", d => AddType0(d, "Identity"));

        Finding finding = Assert.Single(new Pdfa2uToUnicodeRule().Check(Ctx(doc)));
        Assert.Equal("pdfa2u-tounicode", finding.RuleId);
        Assert.NotNull(finding.ObjectNumber);
        Assert.True(finding.ObjectNumber > 0);
        Assert.Equal(20, finding.ObjectNumber);
    }

    [Fact]
    public void Pdfa2uToUnicodeValues_NamesTheOffendingFontObject()
    {
        PdfDocument doc = Doc(Ref(40), "BT /F0 12 Tf (A) Tj ET", AddSimpleFontWithForbiddenToUnicode);

        Finding finding = Assert.Single(new Pdfa2uToUnicodeValuesRule().Check(Ctx(doc)));
        Assert.Equal("pdfa2u-tounicode-values", finding.RuleId);
        Assert.NotNull(finding.ObjectNumber);
        Assert.True(finding.ObjectNumber > 0);
        Assert.Equal(40, finding.ObjectNumber);
    }
}
