using System;
using System.Linq;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Metadata;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 13 (PDF/UA-1, ISO 14289-1) phase 1: identification (<see cref="UaIdentificationRule"/>) and the
/// catalog/metadata rules — tagging (<see cref="UaTaggedRule"/>), title display
/// (<see cref="UaDisplayDocTitleRule"/>), document title (<see cref="UaTitleRule"/>), and no-XFA
/// (<see cref="UaXfaRule"/>). Rule-level tests over hand-built documents; the veraPDF PDF_UA-1 corpus backs
/// the red/green surface (<see cref="CorpusOracleTests"/>).
/// </summary>
public class PreflightSlice13Tests
{
    private const string PdfUaIdNs = "http://www.aiim.org/pdfua/ns/id/";
    private const string DublinCoreNs = "http://purl.org/dc/elements/1.1/";

    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    /// <summary>A document whose catalog <paramref name="configure"/> tweaks. The catalog starts fully
    /// PDF/UA-conformant (structure tree, MarkInfo, DisplayDocTitle, pdfuaid + dc:title metadata); each test
    /// removes or changes one thing.</summary>
    private static PdfDocument Doc(Action<PdfDocument, PdfDictionary> configure, string? pdfUaPart = "1", string? title = "A Title")
    {
        var doc = new PdfDocument();
        doc.AddObject(30, 0, new PdfStream(
            new PdfDictionary { [N("Type")] = N("Metadata"), [N("Subtype")] = N("XML") }, Xmp(pdfUaPart, title)));
        doc.AddObject(31, 0, new PdfDictionary { [N("Type")] = N("StructTreeRoot") });
        var catalog = new PdfDictionary
        {
            [N("Type")] = N("Catalog"),
            [N("Pages")] = Ref(2),
            [N("Metadata")] = Ref(30),
            [N("StructTreeRoot")] = Ref(31),
            // A default language so the document is genuinely 7.2-conformant: without it the x-default-only
            // dc:title (SetLangAlt defaults to xml:lang="x-default") has an undetermined natural language
            // (ua-content-lang / veraPDF 7.2-t33).
            [N("Lang")] = new PdfString(System.Text.Encoding.ASCII.GetBytes("en-US")),
            [N("MarkInfo")] = new PdfDictionary { [N("Marked")] = PdfBoolean.True },
            [N("ViewerPreferences")] = new PdfDictionary { [N("DisplayDocTitle")] = PdfBoolean.True },
        };
        configure(doc, catalog);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(3)),
            [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(3, 0, new PdfDictionary { [N("Type")] = N("Page"), [N("Parent")] = Ref(2) });
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    private static byte[] Xmp(string? pdfUaPart, string? title)
    {
        XmpPacket packet = XmpPacket.CreateEmpty();
        if (pdfUaPart is not null)
            packet.SetSimple(PdfUaIdNs, "pdfuaid", "part", pdfUaPart);
        if (title is not null)
            packet.SetLangAlt(DublinCoreNs, "dc", "title", title);
        return packet.Serialize();
    }

    private static ConformanceContext Ctx(PdfDocument doc) => new(doc, ConformanceProfile.PdfUA1);

    // Raw XMP with the given rdf:Description namespace declarations and property body — for prefix tests,
    // where XmpPacket cannot help (it collapses multiple prefixes bound to one namespace URI onto a single
    // serialized prefix, so it cannot reproduce a non-"pdfuaid" prefix on the AIIM pdfuaid namespace).
    private static byte[] RawXmp(string nsDecls, string body) => System.Text.Encoding.UTF8.GetBytes(
        "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">"
        + "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
        + $"<rdf:Description rdf:about=\"\" {nsDecls}>{body}</rdf:Description>"
        + "</rdf:RDF></x:xmpmeta>");

    // A minimal document carrying only the given XMP metadata — enough for UaIdentificationRule, which reads
    // only the catalog /Metadata stream.
    private static PdfDocument DocWithXmp(byte[] xmp)
    {
        var doc = new PdfDocument();
        doc.AddObject(30, 0, new PdfStream(
            new PdfDictionary { [N("Type")] = N("Metadata"), [N("Subtype")] = N("XML") }, xmp));
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(3, 0, new PdfDictionary { [N("Type")] = N("Page"), [N("Parent")] = Ref(2) });
        doc.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2), [N("Metadata")] = Ref(30),
        });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    private const string AiimNs = "http://www.aiim.org/pdfua/ns/id/";

    // ── ua-identification (clause 5) ──────────────────────────────────────────

    [Fact]
    public void Conformant_identification_passes()
    {
        Assert.Empty(new UaIdentificationRule().Check(Ctx(Doc((_, _) => { }))));
    }

    [Fact]
    public void Missing_pdfuaid_part_is_flagged()
    {
        Finding f = Assert.Single(new UaIdentificationRule().Check(Ctx(Doc((_, _) => { }, pdfUaPart: null))));
        Assert.Equal("ua-identification", f.RuleId);
    }

    [Fact]
    public void Wrong_pdfuaid_part_is_flagged()
    {
        Assert.Single(new UaIdentificationRule().Check(Ctx(Doc((_, _) => { }, pdfUaPart: "2"))));
    }

    [Fact]
    public void Pdfuaid_part_with_a_non_pdfuaid_prefix_is_flagged()
    {
        // AIIM pdfuaid namespace bound to prefix "pdfuaia"; value is correct (1) so only the prefix is wrong.
        // veraPDF 5 t3 resolves the property by URI and requires the literal prefix "pdfuaid".
        byte[] xmp = RawXmp($"xmlns:pdfuaia=\"{AiimNs}\"", "<pdfuaia:part>1</pdfuaia:part>");
        Finding f = Assert.Single(new UaIdentificationRule().Check(Ctx(DocWithXmp(xmp))));
        Assert.Equal("ua-identification", f.RuleId);
        Assert.Contains("part", f.Message);
        Assert.Contains("pdfuaia", f.Message);
    }

    [Fact]
    public void Pdfuaid_amd_with_a_non_pdfuaid_prefix_is_flagged_while_correct_part_is_not()
    {
        // Both prefixes bind the AIIM namespace; part is correctly pdfuaid, amd is pdfuaia. Only amd is
        // flagged — verifies the reader reads the ACTUAL per-property prefix, not a collapsed namespace map.
        byte[] xmp = RawXmp(
            $"xmlns:pdfuaid=\"{AiimNs}\" xmlns:pdfuaia=\"{AiimNs}\"",
            "<pdfuaid:part>1</pdfuaid:part><pdfuaia:amd>2014</pdfuaia:amd>");
        Finding f = Assert.Single(new UaIdentificationRule().Check(Ctx(DocWithXmp(xmp))));
        Assert.Contains("amd", f.Message);
        Assert.DoesNotContain("part", f.Message);
    }

    [Fact]
    public void Pdfuaid_corr_with_a_non_pdfuaid_prefix_is_flagged()
    {
        byte[] xmp = RawXmp(
            $"xmlns:pdfuaid=\"{AiimNs}\" xmlns:pdfuaia=\"{AiimNs}\"",
            "<pdfuaid:part>1</pdfuaid:part><pdfuaia:corr>2014</pdfuaia:corr>");
        Finding f = Assert.Single(new UaIdentificationRule().Check(Ctx(DocWithXmp(xmp))));
        Assert.Contains("corr", f.Message);
    }

    [Fact]
    public void Pdfuaid_part_amd_corr_all_with_the_pdfuaid_prefix_pass()
    {
        // FP-safety: the conformant shape — every identification property carries the pdfuaid prefix.
        byte[] xmp = RawXmp($"xmlns:pdfuaid=\"{AiimNs}\"",
            "<pdfuaid:part>1</pdfuaid:part><pdfuaid:amd>2014</pdfuaid:amd><pdfuaid:corr>2015</pdfuaid:corr>");
        Assert.Empty(new UaIdentificationRule().Check(Ctx(DocWithXmp(xmp))));
    }

    [Fact]
    public void Pdfuaid_part_in_the_default_namespace_is_not_flagged()
    {
        // FP-safety: an absent (default-namespace) prefix is allowed — veraPDF's test is
        // partPrefix == null || partPrefix == "pdfuaid", so a null/empty prefix must not fire.
        byte[] xmp = RawXmp($"xmlns=\"{AiimNs}\"", "<part>1</part>");
        Assert.Empty(new UaIdentificationRule().Check(Ctx(DocWithXmp(xmp))));
    }

    [Fact]
    public void Malformed_metadata_does_not_crash_or_emit_a_prefix_finding()
    {
        // Core robustness claim: binary garbage in the /Metadata stream must not throw (the XmlReader walk
        // tolerates unparseable input) and must not produce a spurious prefix finding. (The value check may
        // still flag a missing pdfuaid:part — that is separate and correct.)
        byte[] garbage = [0x00, 0x01, 0xFF, 0xFE, 0x42, 0x00, 0x99, 0x3C, 0x78];
        Assert.DoesNotContain(new UaIdentificationRule().Check(Ctx(DocWithXmp(garbage))),
            f => f.Message.Contains("prefix"));
    }

    [Fact]
    public void Pdfuaid_part_in_attribute_form_with_a_wrong_prefix_is_flagged()
    {
        // Compact/attribute-form XMP: pdfuaia:part="1" on rdf:Description. The reader must read the
        // attribute's real prefix, not just element-form properties.
        byte[] xmp = RawXmp($"xmlns:pdfuaia=\"{AiimNs}\" pdfuaia:part=\"1\"", "");
        Finding f = Assert.Single(new UaIdentificationRule().Check(Ctx(DocWithXmp(xmp))));
        Assert.Contains("part", f.Message);
        Assert.Contains("pdfuaia", f.Message);
    }

    // ── ua-tagged (7.1) ───────────────────────────────────────────────────────

    [Fact]
    public void Fully_tagged_document_passes()
    {
        Assert.Empty(new UaTaggedRule().Check(Ctx(Doc((_, _) => { }))));
    }

    [Fact]
    public void Missing_struct_tree_root_is_flagged()
    {
        Finding f = Assert.Single(new UaTaggedRule().Check(Ctx(Doc((_, c) => c.Remove(N("StructTreeRoot"))))));
        Assert.Contains("structure tree", f.Message);
    }

    [Fact]
    public void MarkInfo_not_marked_is_flagged()
    {
        Finding f = Assert.Single(new UaTaggedRule().Check(Ctx(Doc((_, c) =>
            c[N("MarkInfo")] = new PdfDictionary { [N("Marked")] = PdfBoolean.False }))));
        Assert.Contains("tagged", f.Message);
    }

    // ── ua-display-doc-title / ua-title (7.1) ─────────────────────────────────

    [Fact]
    public void Missing_display_doc_title_is_flagged()
    {
        Assert.Single(new UaDisplayDocTitleRule().Check(Ctx(Doc((_, c) => c.Remove(N("ViewerPreferences"))))));
    }

    [Fact]
    public void Display_doc_title_true_passes()
    {
        Assert.Empty(new UaDisplayDocTitleRule().Check(Ctx(Doc((_, _) => { }))));
    }

    [Fact]
    public void Missing_dc_title_is_flagged()
    {
        Assert.Single(new UaTitleRule().Check(Ctx(Doc((_, _) => { }, title: null))));
    }

    [Fact]
    public void Present_dc_title_passes()
    {
        Assert.Empty(new UaTitleRule().Check(Ctx(Doc((_, _) => { }))));
    }

    // ── ua-xfa (7.15) ─────────────────────────────────────────────────────────

    [Fact]
    public void Xfa_form_is_flagged()
    {
        var doc = Doc((d, c) =>
        {
            d.AddObject(40, 0, new PdfDictionary { [N("XFA")] = new PdfArray() });
            c[N("AcroForm")] = Ref(40);
        });
        Assert.Single(new UaXfaRule().Check(Ctx(doc)));
    }

    [Fact]
    public void No_xfa_passes()
    {
        Assert.Empty(new UaXfaRule().Check(Ctx(Doc((_, _) => { }))));
    }

    // ── ua-figure-alt (7.3) — structure-tree walk ─────────────────────────────

    private static PdfString Str(string s) => new(System.Text.Encoding.ASCII.GetBytes(s));

    /// <summary>A minimal document whose structure tree <paramref name="build"/> populates (setting /K,
    /// /RoleMap and adding element objects). Enough for the structure-tree rules, which read only the catalog
    /// /StructTreeRoot.</summary>
    private static PdfDocument DocWithStructTree(Action<PdfDocument, PdfDictionary> build)
    {
        var doc = new PdfDocument();
        var structTreeRoot = new PdfDictionary { [N("Type")] = N("StructTreeRoot") };
        build(doc, structTreeRoot);
        doc.AddObject(31, 0, structTreeRoot);
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("StructTreeRoot")] = Ref(31) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    [Fact]
    public void Figure_with_empty_alt_and_no_actualtext_is_flagged()
    {
        var doc = DocWithStructTree((d, root) =>
        {
            d.AddObject(50, 0, new PdfDictionary { [N("S")] = N("Figure"), [N("Alt")] = Str("") });
            root[N("K")] = Ref(50);
        });
        Finding f = Assert.Single(new UaFigureAltRule().Check(Ctx(doc)));
        Assert.Equal("ua-figure-alt", f.RuleId);
    }

    [Fact]
    public void Figure_with_nonempty_alt_passes()
    {
        var doc = DocWithStructTree((d, root) =>
        {
            d.AddObject(50, 0, new PdfDictionary { [N("S")] = N("Figure"), [N("Alt")] = Str("A bar chart") });
            root[N("K")] = Ref(50);
        });
        Assert.Empty(new UaFigureAltRule().Check(Ctx(doc)));
    }

    [Fact] // an /ActualText key present (even empty) defers to the marked-content phase — not flagged here
    public void Figure_with_actualtext_key_is_deferred()
    {
        var doc = DocWithStructTree((d, root) =>
        {
            d.AddObject(50, 0, new PdfDictionary
            {
                [N("S")] = N("Figure"),
                [N("Alt")] = Str(""),
                [N("ActualText")] = Str(""),
            });
            root[N("K")] = Ref(50);
        });
        Assert.Empty(new UaFigureAltRule().Check(Ctx(doc)));
    }

    [Fact] // a custom structure type role-mapped to Figure is still checked
    public void Rolemapped_figure_with_empty_alt_is_flagged()
    {
        var doc = DocWithStructTree((d, root) =>
        {
            root[N("RoleMap")] = new PdfDictionary { [N("MyPicture")] = N("Figure") };
            d.AddObject(50, 0, new PdfDictionary { [N("S")] = N("MyPicture"), [N("Alt")] = Str("") });
            root[N("K")] = Ref(50);
        });
        Assert.Single(new UaFigureAltRule().Check(Ctx(doc)));
    }

    // ── end-to-end ─────────────────────────────────────────────────────────────

    [Fact]
    public void Conformant_ua_document_has_no_phase1_findings()
    {
        PreflightResult result = Preflighter.Check(Doc((_, _) => { }), ConformanceProfile.PdfUA1);
        Assert.DoesNotContain(result.Findings, f =>
            f.RuleId.StartsWith("ua-", StringComparison.Ordinal) && f.Severity == FindingSeverity.Error);
    }
}
