using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 20 of the preflight: PDF/A file-structure rules — the file header
/// (<see cref="FileHeaderRule"/>, ISO 19005-2 6.1.2) and implementation limits
/// (<see cref="ImplementationLimitsRule"/>, 6.1.13: page boundary sizes, string length, name length).
/// Header cases are driven from raw bytes via the <see cref="ConformanceContext"/> source-bytes ctor;
/// the limit cases run over hand-built documents. The veraPDF corpus backs the false-positive side.
/// </summary>
public class PreflightSlice20Tests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static byte[] L(string s) => Encoding.Latin1.GetBytes(s);

    private static ConformanceContext HeaderCtx(byte[]? bytes) =>
        new(new PdfDocument(), ConformanceProfile.PdfA2b, bytes);

    // ── File header (6.1.2) ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("%PDF-1.7\n%öäüß\n1 0 obj\n")]        // LF, minimal 4 high bytes
    [InlineData("%PDF-1.4\r\n%âãÏÓ\r\n1 0 obj\n")]    // CRLF (typical producer header)
    [InlineData("%PDF-1.0\r%âãÏÓ\r1 0 obj\n")]        // lone CR
    [InlineData("%PDF-1.7\n%öäüßabc\n1 0 obj\n")]     // more than four comment bytes
    public void Valid_header_passes(string header)
    {
        Assert.Empty(new FileHeaderRule().Check(HeaderCtx(L(header))));
    }

    [Theory]
    [InlineData("%PDF-2.0\n%öäüß\n")]        // version not 1.x (t01-fail-a)
    [InlineData("SomeData\n%PDF-1.7\n%öäüß\n")] // header not at offset 0 (t01-fail-b)
    [InlineData("%PDF-1.70\n%öäüß\n")]       // extra version digit → no EOL (t01-fail-c)
    [InlineData("%PDF-1.9\n%öäüß\n")]        // minor version out of 0–7 (t01-fail-d)
    [InlineData("%PDF-1.7   \n%öäüß\n")]     // spaces after version, not EOL (t01-fail-e)
    [InlineData("%PDF-1.7\n%öäü\n")]              // fewer than four comment bytes (t02-fail-a)
    [InlineData("%PDF-1.7\ninvalidData\n")]                     // second line is not a %-comment (t02-fail-b)
    [InlineData("%PDF-1.7\n%aäüß\n")]            // ANSI byte among first four (t02-fail-c)
    [InlineData("%PDF-1.7\n\n%öäüß\n")]      // blank line before the comment (t02-fail-d)
    public void Invalid_header_is_flagged(string header)
    {
        Finding finding = Assert.Single(new FileHeaderRule().Check(HeaderCtx(L(header))));
        Assert.Equal("file-header", finding.RuleId);
        Assert.Equal(FindingSeverity.Error, finding.Severity);
        Assert.EndsWith("6.1.2", finding.Clause);
    }

    [Fact]
    public void Header_rule_skips_when_source_bytes_unavailable()
    {
        Assert.Empty(new FileHeaderRule().Check(HeaderCtx(null)));
    }

    // ── Implementation limits: page boundary sizes (6.1.13) ─────────────────────────────────────────

    /// <summary>One-page document; the page and its parent /Pages node dictionaries are configured by
    /// the callers so both own-box and inherited-box cases can be exercised.</summary>
    private static PdfDocument PageDoc(Action<PdfDictionary> page, Action<PdfDictionary> pagesNode)
    {
        var doc = new PdfDocument();
        var pageDict = new PdfDictionary { [N("Type")] = N("Page"), [N("Parent")] = Ref(2) };
        page(pageDict);
        var node = new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(3)),
            [N("Count")] = new PdfInteger(1),
        };
        pagesNode(node);
        doc.AddObject(3, 0, pageDict);
        doc.AddObject(2, 0, node);
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    private static PdfArray Rect(int x0, int y0, int x1, int y1) =>
        new(new PdfInteger(x0), new PdfInteger(y0), new PdfInteger(x1), new PdfInteger(y1));

    private static IReadOnlyList<Finding> Limits(PdfDocument doc) =>
        new ImplementationLimitsRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)).ToList();

    [Fact]
    public void Page_box_within_limits_passes()
    {
        PdfDocument doc = PageDoc(p => p[N("MediaBox")] = Rect(0, 0, 612, 792), _ => { });
        Assert.Empty(Limits(doc));
    }

    [Fact]
    public void Page_box_too_small_is_flagged()
    {
        PdfDocument doc = PageDoc(p => p[N("MediaBox")] = Rect(0, 0, 2, 2), _ => { });
        Assert.Contains(Limits(doc), f => f.Message.Contains("MediaBox") && f.Clause.EndsWith("6.1.13"));
    }

    [Fact]
    public void Page_box_too_large_is_flagged()
    {
        PdfDocument doc = PageDoc(p => p[N("MediaBox")] = Rect(0, 0, 14405, 14405), _ => { });
        Assert.Contains(Limits(doc), f => f.Message.Contains("MediaBox"));
    }

    [Fact]
    public void Inherited_root_node_box_too_small_is_flagged()
    {
        // The page defines no MediaBox; it inherits the too-small one from the root /Pages node (t09-c/d).
        PdfDocument doc = PageDoc(_ => { }, node => node[N("MediaBox")] = Rect(0, 0, 2, 2));
        Assert.Contains(Limits(doc), f => f.Clause.EndsWith("6.1.13"));
    }

    [Fact]
    public void Page_overriding_bad_inherited_box_passes()
    {
        // The root node carries an invalid box, but the page overrides MediaBox and inherits a valid
        // CropBox — the effective per-page boxes are valid, so nothing fires (mirrors t09-pass-a/b).
        PdfDocument doc = PageDoc(
            p => p[N("MediaBox")] = Rect(0, 0, 400, 400),
            node =>
            {
                node[N("MediaBox")] = Rect(0, 0, 2, 2);
                node[N("CropBox")] = Rect(0, 0, 400, 400);
            });
        Assert.Empty(Limits(doc));
    }

    // ── Implementation limits: string and name length (6.1.13) ──────────────────────────────────────

    /// <summary>A document whose catalog (reachable from the trailer /Root) carries the supplied entries,
    /// so the reachable-object walk visits them.</summary>
    private static PdfDocument CatalogDoc(Action<PdfDictionary> configureCatalog)
    {
        var doc = new PdfDocument();
        var catalog = new PdfDictionary { [N("Type")] = N("Catalog") };
        configureCatalog(catalog);
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    [Fact]
    public void String_over_32767_bytes_is_flagged()
    {
        PdfDocument doc = CatalogDoc(c => c[N("Big")] = new PdfString(new byte[32768]));
        Finding finding = Assert.Single(Limits(doc));
        Assert.Contains("string", finding.Message);
        Assert.EndsWith("6.1.13", finding.Clause);
    }

    [Fact]
    public void String_at_the_limit_passes()
    {
        PdfDocument doc = CatalogDoc(c => c[N("AtLimit")] = new PdfString(new byte[32767]));
        Assert.Empty(Limits(doc));
    }

    [Fact]
    public void Name_value_over_127_bytes_is_flagged()
    {
        PdfDocument doc = CatalogDoc(c => c[N("Ref")] = new PdfName(new string('X', 128)));
        Finding finding = Assert.Single(Limits(doc));
        Assert.Contains("name", finding.Message);
        Assert.EndsWith("6.1.13", finding.Clause);
    }

    [Fact]
    public void Name_key_over_127_bytes_is_flagged()
    {
        PdfDocument doc = CatalogDoc(c => c[N(new string('K', 129))] = new PdfString(L("x")));
        Assert.Single(Limits(doc));
    }

    [Fact]
    public void Name_at_the_limit_passes()
    {
        PdfDocument doc = CatalogDoc(c => c[N(new string('K', 127))] = new PdfName(new string('V', 127)));
        Assert.Empty(Limits(doc));
    }
}
