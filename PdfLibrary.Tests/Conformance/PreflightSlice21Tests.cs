using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 21 — PDF/UA-1 annotation rules (<c>ua-annotation</c>, ISO 14289-1 7.18). Dictionary-level checks
/// (7.18.2 TrapNet, 7.18.3 Tabs, 7.18.5 Link /Contents) run over synthetic annotations; the structure-
/// nesting checks (7.18.1/.4/.5/.8) build a small tagged tree whose element references the annotation via an
/// <c>/OBJR</c>. The veraPDF corpus and reference files back the false-positive side.
/// </summary>
public class PreflightSlice21Tests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfString Str(string s) => new(Encoding.Latin1.GetBytes(s));

    /// <summary>A one-page document. The page (object 10) gets the given annotations (objects 20..) in its
    /// /Annots and the given /Tabs; no structure tree, so only the dictionary-level checks apply.</summary>
    private static PdfDocument Doc(PdfObject? tabs, params PdfDictionary[] annots)
    {
        var doc = new PdfDocument();
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792)),
        };
        if (annots.Length > 0)
        {
            var arr = new PdfArray();
            for (int i = 0; i < annots.Length; i++)
            {
                doc.AddObject(20 + i, 0, annots[i]);
                arr.Add(Ref(20 + i));
            }
            page[N("Annots")] = arr;
        }
        if (tabs is not null)
            page[N("Tabs")] = tabs;

        doc.AddObject(10, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(10)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    private static PdfDictionary Annot(string subtype, int flags = 0, string? contents = null, PdfArray? rect = null)
    {
        var a = new PdfDictionary { [N("Type")] = N("Annot"), [N("Subtype")] = N(subtype) };
        if (flags != 0) a[N("F")] = new PdfInteger(flags);
        if (contents is not null) a[N("Contents")] = Str(contents);
        if (rect is not null) a[N("Rect")] = rect;
        return a;
    }

    private static PdfArray Rect(int x0, int y0, int x1, int y1) =>
        new(new PdfInteger(x0), new PdfInteger(y0), new PdfInteger(x1), new PdfInteger(y1));

    private static Finding[] Run(PdfDocument doc) =>
        new UaAnnotationRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfUA1)).ToArray();

    private static string? Clause(Finding f) => ParitySnapshot.ClauseKey(f.Clause);

    // ── 7.18.2 TrapNet ──────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TrapNet_annotation_is_flagged()
    {
        Finding f = Assert.Single(Run(Doc(N("S"), Annot("TrapNet"))), x => Clause(x) == "7.18.2");
        Assert.Contains("TrapNet", f.Message);
    }

    // ── 7.18.3 Tabs ─────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Page_with_annotation_and_no_tabs_is_flagged() =>
        Assert.Contains(Run(Doc(tabs: null, Annot("Link", contents: "x"))), f => Clause(f) == "7.18.3");

    [Fact]
    public void Page_with_annotation_and_non_S_tabs_is_flagged() =>
        Assert.Contains(Run(Doc(N("C"), Annot("Link", contents: "x"))), f => Clause(f) == "7.18.3");

    [Fact]
    public void Page_with_annotation_and_S_tabs_passes_tabs() =>
        Assert.DoesNotContain(Run(Doc(N("S"), Annot("Link", contents: "x"))), f => Clause(f) == "7.18.3");

    [Fact]
    public void Page_without_annotations_is_clean() => Assert.Empty(Run(Doc(tabs: null)));

    // ── in-scope filter ─────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Hidden_annotation_is_out_of_scope() =>
        Assert.Empty(Run(Doc(tabs: null, Annot("Link", flags: 2)))); // Hidden → no Tabs finding, no Contents finding

    [Fact]
    public void Popup_annotation_is_out_of_scope() =>
        Assert.Empty(Run(Doc(tabs: null, Annot("Popup"))));

    // ── 7.18.5 Link /Contents ───────────────────────────────────────────────────────────────────────
    [Fact]
    public void Link_without_contents_is_flagged() =>
        Assert.Contains(Run(Doc(N("S"), Annot("Link"))), f => Clause(f) == "7.18.5");

    [Fact]
    public void Link_with_empty_contents_is_flagged() =>
        Assert.Contains(Run(Doc(N("S"), Annot("Link", contents: "   "))), f => Clause(f) == "7.18.5");

    [Fact]
    public void Link_with_contents_passes_contents_check() =>
        Assert.DoesNotContain(Run(Doc(N("S"), Annot("Link", contents: "See page 3"))),
            f => Clause(f) == "7.18.5" && f.Message.Contains("Contents"));

    // ── structure nesting (7.18.1/.4/.5/.8) ─────────────────────────────────────────────────────────

    /// <summary>A one-page tagged document: object 30 is a structure element of type <paramref name="tag"/>
    /// (null → the annotation is left untagged) whose /K is an /OBJR pointing at the annotation (object 20).
    /// When <paramref name="elemAlt"/> is set, the element carries that /Alt.</summary>
    private static PdfDocument TaggedDoc(string subtype, string? tag, string? contents = null, string? elemAlt = null)
    {
        var doc = new PdfDocument();
        PdfDictionary annot = Annot(subtype, contents: contents);
        doc.AddObject(20, 0, annot);

        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792)),
            [N("Tabs")] = N("S"),
            [N("Annots")] = new PdfArray(Ref(20)),
        };
        doc.AddObject(10, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(10)), [N("Count")] = new PdfInteger(1),
        });

        var structRoot = new PdfDictionary { [N("Type")] = N("StructTreeRoot") };
        if (tag is not null)
        {
            var objr = new PdfDictionary { [N("Type")] = N("OBJR"), [N("Obj")] = Ref(20), [N("Pg")] = Ref(10) };
            var elem = new PdfDictionary { [N("Type")] = N("StructElem"), [N("S")] = N(tag), [N("K")] = objr };
            if (elemAlt is not null) elem[N("Alt")] = Str(elemAlt);
            doc.AddObject(30, 0, elem);
            structRoot[N("K")] = new PdfArray(Ref(30));
        }
        doc.AddObject(31, 0, structRoot);

        doc.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2), [N("StructTreeRoot")] = Ref(31),
        });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    [Theory]
    [InlineData("Widget", "Document", "7.18.4")] // widget not under Form
    [InlineData("Link", "P", "7.18.5")]          // link not under Link
    [InlineData("Highlight", "H1", "7.18.1")]    // other annotation not under Annot
    public void Annotation_under_wrong_tag_is_flagged(string subtype, string tag, string clause)
    {
        Assert.Contains(Run(TaggedDoc(subtype, tag, contents: "x")), f => Clause(f) == clause);
    }

    [Theory]
    [InlineData("Widget", "Form")]
    [InlineData("Link", "Link")]
    [InlineData("Highlight", "Annot")]
    public void Annotation_under_correct_tag_passes(string subtype, string tag)
    {
        Assert.Empty(Run(TaggedDoc(subtype, tag, contents: "x")));
    }

    [Fact]
    public void PrinterMark_in_structure_is_flagged() =>
        Assert.Contains(Run(TaggedDoc("PrinterMark", "Annot", contents: "x")), f => Clause(f) == "7.18.8");

    [Fact]
    public void PrinterMark_not_tagged_passes() =>
        Assert.Empty(Run(TaggedDoc("PrinterMark", tag: null, contents: "x")));

    // ── 7.18.1 alternate description (28-004) ────────────────────────────────────────────────────────

    [Fact]
    public void Annotation_without_contents_or_alt_is_flagged() =>
        Assert.Contains(Run(TaggedDoc("Highlight", "Annot")), f => Clause(f) == "7.18.1");

    [Fact]
    public void Annotation_with_contents_satisfies_alt_requirement() =>
        Assert.DoesNotContain(Run(TaggedDoc("Highlight", "Annot", contents: "a mark")), f => Clause(f) == "7.18.1");

    [Fact]
    public void Annotation_with_enclosing_element_alt_passes() =>
        Assert.DoesNotContain(Run(TaggedDoc("Highlight", "Annot", elemAlt: "a mark")), f => Clause(f) == "7.18.1");

    [Fact]
    public void PrinterMark_without_contents_is_not_subject_to_alt_requirement() =>
        Assert.Empty(Run(TaggedDoc("PrinterMark", tag: null))); // artifact PrinterMark, no Contents → clean

    // ── off-CropBox exclusion ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Off_cropbox_annotation_is_out_of_scope() =>
        // Rect entirely outside the MediaBox/CropBox (0..612 × 0..792): no Tabs, Contents, or nesting finding.
        Assert.Empty(Run(Doc(tabs: null, Annot("Link", rect: Rect(800, 800, 810, 810)))));

    // ── 7.18.1 form-field description (28-005) ───────────────────────────────────────────────────────

    /// <summary>A one-page document with a single AcroForm field. When <paramref name="kids"/> is null the
    /// field (object 20) is a merged widget-field placed directly on the page; otherwise it is a parent
    /// field whose widget kids (objects 21+) are the page annotations. /Tabs is /S so only field checks fire;
    /// no structure tree, so the /Alt fallback is absent.</summary>
    private static PdfDocument FieldDoc(Action<PdfDictionary> configureField, params Action<PdfDictionary>[] kids)
    {
        var doc = new PdfDocument();
        var field = new PdfDictionary { [N("FT")] = N("Tx"), [N("T")] = Str("f") };
        var annotRefs = new PdfArray();

        if (kids.Length == 0)
        {
            field[N("Type")] = N("Annot");
            field[N("Subtype")] = N("Widget");
            field[N("Rect")] = Rect(100, 100, 200, 120);
            annotRefs.Add(Ref(20));
        }
        else
        {
            var kidRefs = new PdfArray();
            int kn = 21;
            foreach (Action<PdfDictionary> k in kids)
            {
                var w = new PdfDictionary
                {
                    [N("Type")] = N("Annot"), [N("Subtype")] = N("Widget"),
                    [N("Parent")] = Ref(20), [N("Rect")] = Rect(100, 100, 200, 120),
                };
                k(w);
                doc.AddObject(kn, 0, w);
                kidRefs.Add(Ref(kn));
                annotRefs.Add(Ref(kn));
                kn++;
            }
            field[N("Kids")] = kidRefs;
        }
        configureField(field);
        doc.AddObject(20, 0, field);

        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2), [N("Tabs")] = N("S"),
            [N("MediaBox")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792)),
            [N("Annots")] = annotRefs,
        };
        doc.AddObject(10, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(10)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2),
            [N("AcroForm")] = new PdfDictionary { [N("Fields")] = new PdfArray(Ref(20)) },
        });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    [Fact]
    public void Merged_field_without_tu_is_flagged() =>
        Assert.Contains(Run(FieldDoc(_ => { })), f => Clause(f) == "7.18.1");

    [Fact]
    public void Merged_field_with_tu_passes() =>
        Assert.Empty(Run(FieldDoc(f => f[N("TU")] = Str("Your name"))));

    [Fact]
    public void Hidden_field_is_out_of_scope() =>
        Assert.Empty(Run(FieldDoc(f => f[N("F")] = new PdfInteger(2)))); // Hidden merged field, no TU → clean

    [Fact]
    public void Parent_field_tu_covers_bare_widget_kids()
    {
        // The Btn field carries /TU; its two bare widget kids do not. The field-level /TU satisfies 7.18.1
        // (mirrors veraPDF's 7.18.1-t03-pass-e, which the widget-level read wrongly failed).
        PdfDocument doc = FieldDoc(f => { f[N("FT")] = N("Btn"); f[N("TU")] = Str("Choose"); }, _ => { }, _ => { });
        Assert.Empty(Run(doc));
    }

    [Fact]
    public void Parent_field_without_tu_is_flagged_despite_widget_tu()
    {
        // The Btn field has no /TU; putting /TU on the bare widget kids does not count (mirrors 7.18.1-t03-fail-d).
        PdfDocument doc = FieldDoc(f => f[N("FT")] = N("Btn"),
            w => w[N("TU")] = Str("x"), w => w[N("TU")] = Str("y"));
        Assert.Contains(Run(doc), f => Clause(f) == "7.18.1");
    }
}
