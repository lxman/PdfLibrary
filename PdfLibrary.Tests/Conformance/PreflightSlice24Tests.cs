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
/// Slice 24 (PDF/UA-1) — the structural bucket, Slice B1: natural language for non-structure-element text
/// (ISO 14289-1:2014, 7.2), the three remaining Matterhorn CP11 conditions, calibrated against veraPDF's
/// PDF_UA-1 profile and its 7.2-t24/t25/t33 fixtures:
/// <list type="bullet">
///   <item><b>t24 (PDAnnot, <see cref="UaContentLangRule"/>)</b> — an annotation with non-empty /Contents
///     and no determinable language (no /Lang on the annotation, its enclosing structure element, or the
///     catalog): <c>Contents == null || containsLang == true || gContainsCatalogLang == true</c>.</item>
///   <item><b>t25 (PDFormField)</b> — a form field with non-empty /TU and the same undetermined-language
///     condition.</item>
///   <item><b>t33 (XMPLangAlt)</b> — a language-alternative metadata property whose only value carries the
///     undefined language x-default (veraPDF <c>XMPLangAlt.xDefault</c>).</item>
/// </list>
/// All three share the catalog-/Lang short-circuit (<c>gContainsCatalogLang</c>). The corpus oracle
/// (<see cref="CorpusOracleTests"/>) and the reference-file pass-oracle back the detection/0-FP surface;
/// these are the hand-built edge cases pinned to the exact fixture behaviours.
/// </summary>
public class PreflightSlice24Tests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfString Str(string s) => new(Encoding.ASCII.GetBytes(s));
    private static ConformanceContext Ctx(PdfDocument doc) => new(doc, ConformanceProfile.PdfUA1);
    private static Finding[] Findings(PdfDocument doc) => new UaContentLangRule().Check(Ctx(doc)).ToArray();
    private static PdfArray Rect() =>
        new(new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100));

    // ── Annotations (7.2 t24) ────────────────────────────────────────────────

    /// <summary>A one-page document whose page carries a single Link annotation (object 40) built by
    /// <paramref name="configAnnot"/>. When <paramref name="tagged"/> the annotation is wired into a
    /// structure tree via a /Link element containing an /OBJR to it; <paramref name="structLang"/> sets a
    /// /Lang on that element. <paramref name="catalogLang"/> sets the catalog default language.</summary>
    private static PdfDocument AnnotDoc(
        Action<PdfDictionary> configAnnot, string? catalogLang = null, bool tagged = false, string? structLang = null)
    {
        var doc = new PdfDocument();

        var annot = new PdfDictionary
        {
            [N("Type")] = N("Annot"),
            [N("Subtype")] = N("Link"),
            [N("Rect")] = Rect(),
        };
        configAnnot(annot);
        doc.AddObject(40, 0, annot);

        doc.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("Annots")] = new PdfArray(Ref(40)),
        });
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });

        var catalog = new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) };
        if (catalogLang is not null) catalog[N("Lang")] = Str(catalogLang);

        if (tagged)
        {
            var link = new PdfDictionary
            {
                [N("Type")] = N("StructElem"),
                [N("S")] = N("Link"),
                [N("K")] = new PdfDictionary { [N("Type")] = N("OBJR"), [N("Obj")] = Ref(40) },
            };
            if (structLang is not null) link[N("Lang")] = Str(structLang);
            doc.AddObject(32, 0, link);
            doc.AddObject(31, 0, new PdfDictionary { [N("Type")] = N("StructTreeRoot"), [N("K")] = Ref(32) });
            catalog[N("StructTreeRoot")] = Ref(31);
        }

        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    [Fact] // corpus 7.2-t24-fail-a: /Contents, no lang anywhere
    public void Annotation_with_contents_and_no_lang_is_flagged()
    {
        Finding f = Assert.Single(Findings(AnnotDoc(a => a[N("Contents")] = Str("Click to redirect"))));
        Assert.Equal("ua-content-lang", f.RuleId);
        Assert.Contains("annotation", f.Message);
        Assert.Contains("natural language cannot be determined", f.Message);
        Assert.Contains("ISO 14289-1:2014, 7.2", f.Clause);
        Assert.Equal(40, f.ObjectNumber);
    }

    [Fact] // corpus 7.2-t24-pass-a: catalog /Lang makes it determinable
    public void Annotation_with_contents_passes_when_catalog_has_lang()
    {
        Assert.Empty(Findings(AnnotDoc(a => a[N("Contents")] = Str("Click to redirect"), catalogLang: "en")));
    }

    [Fact] // the annotation declares its own /Lang
    public void Annotation_with_own_lang_passes()
    {
        Assert.Empty(Findings(AnnotDoc(a =>
        {
            a[N("Contents")] = Str("Click to redirect");
            a[N("Lang")] = Str("en-US");
        })));
    }

    [Fact]
    public void Annotation_without_contents_is_not_flagged()
    {
        Assert.Empty(Findings(AnnotDoc(_ => { })));                        // no /Contents at all
        Assert.Empty(Findings(AnnotDoc(a => a[N("Contents")] = Str(""))));  // present but empty
    }

    [Fact] // corpus 7.2-t24-pass-b: the enclosing /Link structure element carries /Lang
    public void Annotation_passes_when_enclosing_structure_element_has_lang()
    {
        Assert.Empty(Findings(AnnotDoc(
            a => a[N("Contents")] = Str("Click to redirect"), tagged: true, structLang: "en-US")));
    }

    [Fact] // the OBJR container has no /Lang → still undetermined even though it is tagged
    public void Annotation_flagged_when_enclosing_structure_element_lacks_lang()
    {
        Finding f = Assert.Single(Findings(AnnotDoc(
            a => a[N("Contents")] = Str("Click to redirect"), tagged: true, structLang: null)));
        Assert.Equal("ua-content-lang", f.RuleId);
        Assert.Equal(40, f.ObjectNumber);
    }

    // ── Form fields (7.2 t25) ──────────────────────────────────────────────────

    /// <summary>A document with an AcroForm whose single field (object 40, a /Tx widget) is built by
    /// <paramref name="configField"/>. Optionally tagged with a /Form structure element carrying an /OBJR to
    /// the widget, with <paramref name="structLang"/> on that element.</summary>
    private static PdfDocument FieldDoc(
        Action<PdfDictionary> configField, string? catalogLang = null, bool tagged = false, string? structLang = null)
    {
        var doc = new PdfDocument();

        var field = new PdfDictionary
        {
            [N("Type")] = N("Annot"),
            [N("Subtype")] = N("Widget"),
            [N("FT")] = N("Tx"),
            [N("Rect")] = Rect(),
        };
        configField(field);
        doc.AddObject(40, 0, field);

        doc.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("Annots")] = new PdfArray(Ref(40)),
        });
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });

        var catalog = new PdfDictionary
        {
            [N("Type")] = N("Catalog"),
            [N("Pages")] = Ref(2),
            [N("AcroForm")] = new PdfDictionary { [N("Fields")] = new PdfArray(Ref(40)) },
        };
        if (catalogLang is not null) catalog[N("Lang")] = Str(catalogLang);

        if (tagged)
        {
            var form = new PdfDictionary
            {
                [N("Type")] = N("StructElem"),
                [N("S")] = N("Form"),
                [N("K")] = new PdfDictionary { [N("Type")] = N("OBJR"), [N("Obj")] = Ref(40) },
            };
            if (structLang is not null) form[N("Lang")] = Str(structLang);
            doc.AddObject(32, 0, form);
            doc.AddObject(31, 0, new PdfDictionary { [N("Type")] = N("StructTreeRoot"), [N("K")] = Ref(32) });
            catalog[N("StructTreeRoot")] = Ref(31);
        }

        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    [Fact] // corpus 7.2-t25-fail-a
    public void Field_with_tu_and_no_lang_is_flagged()
    {
        Finding f = Assert.Single(Findings(FieldDoc(a => a[N("TU")] = Str("firstname"))));
        Assert.Equal("ua-content-lang", f.RuleId);
        Assert.Contains("form field", f.Message);
        Assert.Contains("/TU", f.Message);
        Assert.Equal(40, f.ObjectNumber);
    }

    [Fact] // corpus 7.2-t25-pass-a
    public void Field_with_tu_passes_when_catalog_has_lang()
    {
        Assert.Empty(Findings(FieldDoc(a => a[N("TU")] = Str("firstname"), catalogLang: "en-US")));
    }

    [Fact]
    public void Field_with_own_lang_passes()
    {
        Assert.Empty(Findings(FieldDoc(a =>
        {
            a[N("TU")] = Str("firstname");
            a[N("Lang")] = Str("en-US");
        })));
    }

    [Fact]
    public void Field_without_tu_is_not_flagged()
    {
        Assert.Empty(Findings(FieldDoc(_ => { })));
        Assert.Empty(Findings(FieldDoc(a => a[N("TU")] = Str(""))));
    }

    [Fact] // corpus 7.2-t25-pass-b: enclosing /Form structure element carries /Lang
    public void Field_passes_when_enclosing_structure_element_has_lang()
    {
        Assert.Empty(Findings(FieldDoc(a => a[N("TU")] = Str("firstname"), tagged: true, structLang: "en-US")));
    }

    [Fact] // matches t25-fail-a's shape: a lang-bearing ancestor does not help; only the OBJR container counts
    public void Field_flagged_when_enclosing_structure_element_lacks_lang()
    {
        Finding f = Assert.Single(Findings(
            FieldDoc(a => a[N("TU")] = Str("firstname"), tagged: true, structLang: null)));
        Assert.Equal(40, f.ObjectNumber);
    }

    // ── XMP language alternatives (7.2 t33) ─────────────────────────────────────

    private static byte[] Xmp(string body) => Encoding.UTF8.GetBytes(
        "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>"
        + "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">"
        + "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
        + body
        + "</rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>");

    // A dc:title language alternative whose items carry the given xml:lang qualifiers.
    private static string TitleAlt(params string[] langs) =>
        "<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title><rdf:Alt>"
        + string.Concat(langs.Select(l => $"<rdf:li xml:lang=\"{l}\">Title</rdf:li>"))
        + "</rdf:Alt></dc:title></rdf:Description>";

    private static PdfDocument XmpDoc(string body, string? catalogLang = null)
    {
        var doc = new PdfDocument();
        doc.AddObject(2, 0, new PdfStream(new PdfDictionary(), Xmp(body)));
        var catalog = new PdfDictionary { [N("Type")] = N("Catalog"), [N("Metadata")] = Ref(2) };
        if (catalogLang is not null) catalog[N("Lang")] = Str(catalogLang);
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    [Fact] // corpus 7.2-t33-fail-a: the only value carries x-default (undefined language), no catalog /Lang
    public void LangAlt_with_only_xdefault_is_flagged()
    {
        Finding f = Assert.Single(Findings(XmpDoc(TitleAlt("x-default"))));
        Assert.Equal("ua-content-lang", f.RuleId);
        Assert.Contains("language-alternative", f.Message);
        Assert.Contains("x-default", f.Message);
        Assert.Null(f.ObjectNumber);
        Assert.Contains("ISO 14289-1:2014, 7.2", f.Clause);
    }

    [Fact] // corpus 7.2-t33-pass-b: the value carries a specific language
    public void LangAlt_with_specific_language_passes()
    {
        Assert.Empty(Findings(XmpDoc(TitleAlt("en-US"))));
    }

    [Fact] // corpus 7.2-t33-pass-a: catalog /Lang makes an x-default-only value determinable
    public void LangAlt_with_only_xdefault_passes_when_catalog_has_lang()
    {
        Assert.Empty(Findings(XmpDoc(TitleAlt("x-default"), catalogLang: "en-US")));
    }

    [Fact] // the common Acrobat serialization: x-default alongside a specific language → not flagged
    public void LangAlt_with_xdefault_and_specific_language_passes()
    {
        Assert.Empty(Findings(XmpDoc(TitleAlt("x-default", "en-US"))));
    }

    // ── The global short-circuit ────────────────────────────────────────────────

    /// <summary>A document carrying all three offences at once — a Link annotation with /Contents, a /Tx
    /// widget field with /TU, and an x-default-only dc:title — with the catalog /Lang optionally set.</summary>
    private static PdfDocument ComboDoc(string? catalogLang)
    {
        var doc = new PdfDocument();
        doc.AddObject(7, 0, new PdfStream(new PdfDictionary(), Xmp(TitleAlt("x-default"))));
        doc.AddObject(40, 0, new PdfDictionary
        {
            [N("Type")] = N("Annot"), [N("Subtype")] = N("Link"), [N("Rect")] = Rect(),
            [N("Contents")] = Str("Click me"),
        });
        doc.AddObject(41, 0, new PdfDictionary
        {
            [N("Type")] = N("Annot"), [N("Subtype")] = N("Widget"), [N("FT")] = N("Tx"), [N("Rect")] = Rect(),
            [N("TU")] = Str("First name"),
        });
        doc.AddObject(6, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(5), [N("Annots")] = new PdfArray(Ref(40), Ref(41)),
        });
        doc.AddObject(5, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(6)), [N("Count")] = new PdfInteger(1),
        });

        var catalog = new PdfDictionary
        {
            [N("Type")] = N("Catalog"),
            [N("Pages")] = Ref(5),
            [N("Metadata")] = Ref(7),
            [N("AcroForm")] = new PdfDictionary { [N("Fields")] = new PdfArray(Ref(41)) },
        };
        if (catalogLang is not null) catalog[N("Lang")] = Str(catalogLang);
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    [Fact]
    public void All_three_conditions_fire_without_catalog_lang()
    {
        Finding[] findings = Findings(ComboDoc(catalogLang: null));
        Assert.Equal(3, findings.Length);
        Assert.All(findings, f => Assert.Equal("ua-content-lang", f.RuleId));
        Assert.Contains(findings, f => f.Message.Contains("annotation"));
        Assert.Contains(findings, f => f.Message.Contains("form field"));
        Assert.Contains(findings, f => f.Message.Contains("language-alternative"));
    }

    [Fact] // a catalog /Lang suppresses all three at once
    public void Catalog_lang_suppresses_all_three()
    {
        Assert.Empty(Findings(ComboDoc(catalogLang: "en-US")));
    }
}
