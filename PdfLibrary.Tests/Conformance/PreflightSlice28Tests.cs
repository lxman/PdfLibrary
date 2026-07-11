using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 28 — extending the /ToUnicode <b>value</b> check (<see cref="Pdfa2uToUnicodeValuesRule"/>) to
/// PDF/UA-1 (ISO 14289-1, 7.21.7, test 2). The one rule is now profile-aware, with a profile-specific
/// forbidden set:
/// <list type="bullet">
///   <item><b>PDF/A-2u</b> (6.2.11.7.2) — empty, U+0000, U+FEFF, U+FFFE or U+FFFF (unchanged).</item>
///   <item><b>PDF/UA-1</b> (7.21.7-t2) — U+0000, U+FFFE or U+FEFF (a substring check; excludes U+FFFF,
///     does not fault empty — matching veraPDF's <c>toUnicode.indexOf(...) == -1</c> tests).</item>
/// </list>
/// The A-2u set is a strict superset of the UA set, so the only value that distinguishes the two profiles
/// in both directions is U+FFFF (A-2u flags it; UA does not). These facts guard the profile-aware set in
/// both directions; the veraPDF UA corpus (7.21.7-t02-fail-a/-b/-c) backs the real red surface
/// (<see cref="CorpusOracleTests"/>).
/// </summary>
public class PreflightSlice28Tests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfArray Rect(int x0, int y0, int x1, int y1) =>
        new(new PdfInteger(x0), new PdfInteger(y0), new PdfInteger(x1), new PdfInteger(y1));

    /// <summary>A one-page document that shows code 0x41 ('A') with font /F0, whose /ToUnicode maps
    /// 0x41 to the four hex digits in <paramref name="hexValue"/> (e.g. <c>"0000"</c> → U+0000).</summary>
    private static PdfDocument DocMapping(string hexValue)
    {
        var doc = new PdfDocument();
        doc.AddObject(30, 0, ToUnicodeStream($"<41> <{hexValue}>"));
        var font = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type1"),
            [N("BaseFont")] = N("Helvetica"),
            [N("Encoding")] = N("WinAnsiEncoding"),
            [N("ToUnicode")] = Ref(30),
        };
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf (A) Tj ET")));
        doc.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = font } },
        });
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

    private static PdfStream ToUnicodeStream(string bfChar) => new(new PdfDictionary(), Encoding.ASCII.GetBytes(
        "/CIDInit /ProcSet findresource begin 12 dict begin begincmap\n"
        + "1 begincodespacerange <00> <FF> endcodespacerange\n"
        + $"1 beginbfchar {bfChar} endbfchar\nendcmap end end"));

    /// <summary>Whether the value rule fires for <paramref name="hexValue"/> under <paramref name="profile"/>.</summary>
    private static bool Flags(ConformanceProfile profile, string hexValue) =>
        new Pdfa2uToUnicodeValuesRule().Check(new ConformanceContext(DocMapping(hexValue), profile)).Any();

    // ── U+0000: forbidden under BOTH profiles ───────────────────────────────────

    [Fact]
    public void U0000_is_flagged_under_pdfa2u() => Assert.True(Flags(ConformanceProfile.PdfA2u, "0000"));

    [Fact]
    public void U0000_is_flagged_under_pdfua1() => Assert.True(Flags(ConformanceProfile.PdfUA1, "0000"));

    // ── U+FFFE and U+FEFF: forbidden under BOTH (the UA set adds nothing A-2u lacks) ─

    [Fact]
    public void Ufffe_is_flagged_under_pdfua1() => Assert.True(Flags(ConformanceProfile.PdfUA1, "FFFE"));

    [Fact]
    public void Ufffe_is_flagged_under_pdfa2u() => Assert.True(Flags(ConformanceProfile.PdfA2u, "FFFE"));

    [Fact]
    public void Ufeff_is_flagged_under_pdfua1() => Assert.True(Flags(ConformanceProfile.PdfUA1, "FEFF"));

    [Fact]
    public void Ufeff_is_flagged_under_pdfa2u() => Assert.True(Flags(ConformanceProfile.PdfA2u, "FEFF"));

    // ── U+FFFF: the one value that distinguishes the profiles — A-2u forbids it, UA does not ─

    [Fact]
    public void Uffff_is_flagged_under_pdfa2u() => Assert.True(Flags(ConformanceProfile.PdfA2u, "FFFF"));

    [Fact] // UA's set (7.21.7-t2) excludes U+FFFF — over-flagging it here would be a false positive
    public void Uffff_is_NOT_flagged_under_pdfua1() => Assert.False(Flags(ConformanceProfile.PdfUA1, "FFFF"));

    // ── A normal value: clean under BOTH profiles ────────────────────────────────

    [Fact]
    public void Normal_value_is_clean_under_pdfa2u() => Assert.False(Flags(ConformanceProfile.PdfA2u, "0041"));

    [Fact]
    public void Normal_value_is_clean_under_pdfua1() => Assert.False(Flags(ConformanceProfile.PdfUA1, "0041"));

    // ── the finding surfaces under the shared RuleId + the UA clause ─────────────

    [Fact]
    public void Ua_finding_uses_the_shared_ruleid_and_the_ua_clause()
    {
        Finding finding = Assert.Single(
            new Pdfa2uToUnicodeValuesRule().Check(new ConformanceContext(DocMapping("FFFE"), ConformanceProfile.PdfUA1)));
        Assert.Equal("pdfa2u-tounicode-values", finding.RuleId);
        Assert.Contains("7.21.7", finding.Clause);
    }
}
