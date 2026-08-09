using System;
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// The walker and <see cref="ConformanceContext"/> must return the SAME set of referenced fonts. If they
/// ever diverge, the Fonts panel (Task 3) will list fonts no rule considers referenced, or omit one a
/// finding names. As of this commit <see cref="ConformanceContext.CollectReferencedFonts"/> delegates
/// straight to <see cref="ReferencedFontWalker.Collect"/>, so this is a near-tautology for THIS commit —
/// its value is as a regression guard for the day someone edits one side and not the other. Kept
/// deliberately; see task-2-brief.md Step 5.
///
/// No corpus-wide fixture enumerator exists in PdfLibrary.Tests (only <c>CorpusHarness</c>, which walks
/// an external, unvendored veraPDF corpus). Per the task brief's fallback, each of the six branches
/// <see cref="ReferencedFontWalker.Collect"/> walks — plus the Type0-descendant hop — gets its own
/// hand-built fixture, built with the <c>PreflightSlice12Tests</c> pattern (direct <see cref="PdfDocument"/>
/// object construction) so the branch it exercises is verified by construction, not by filename.
/// </summary>
public class ReferencedFontWalkerTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfArray Rect(int x0, int y0, int x1, int y1) =>
        new(new PdfInteger(x0), new PdfInteger(y0), new PdfInteger(x1), new PdfInteger(y1));

    private static PdfDictionary SimpleFont(string baseFont = "Helvetica") => new()
    {
        [N("Type")] = N("Font"),
        [N("Subtype")] = N("Type1"),
        [N("BaseFont")] = N(baseFont),
        [N("Encoding")] = N("WinAnsiEncoding"),
    };

    private static void AssertWalkerAgreesWithContext(PdfDocument document)
    {
        var context = new ConformanceContext(document, ConformanceProfile.PdfA2b);
        System.Collections.Generic.IReadOnlyList<PdfDictionary> viaContext = context.ReferencedFonts;

        System.Collections.Generic.IReadOnlyList<PdfDictionary> viaWalker = ReferencedFontWalker.Collect(
            document, document.GetPages(), context.Annotations, document.GetCatalog());

        Assert.Equal(
            viaContext.Select(d => d.IsIndirect ? d.ObjectNumber : -1).OrderBy(n => n),
            viaWalker.Select(d => d.IsIndirect ? d.ObjectNumber : -1).OrderBy(n => n));
    }

    // ── branch 1: page /Resources inherited from a grandparent /Pages node ────

    [Fact]
    public void Font_in_grandparent_pages_resources_is_collected()
    {
        var doc = new PdfDocument();
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf (A) Tj ET")));
        doc.AddObject(30, 0, SimpleFont());

        // page (3) -> intermediate Pages node (4, no /Resources of its own) -> root Pages (2, holds /Resources)
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(4),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(11),
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(4, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Parent")] = Ref(2),
            [N("Kids")] = new PdfArray(Ref(3)),
            [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(4)),
            [N("Count")] = new PdfInteger(1),
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = Ref(30) } },
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);

        var context = new ConformanceContext(doc, ConformanceProfile.PdfA2b);
        Assert.Contains(context.ReferencedFonts, f => f.IsIndirect && f.ObjectNumber == 30);
        AssertWalkerAgreesWithContext(doc);
    }

    // ── branch 2: a Form XObject's own /Resources ──────────────────────────────

    [Fact]
    public void Font_in_form_xobject_resources_is_collected()
    {
        var doc = new PdfDocument();
        doc.AddObject(30, 0, SimpleFont());
        doc.AddObject(40, 0, new PdfStream(
            new PdfDictionary
            {
                [N("Type")] = N("XObject"),
                [N("Subtype")] = N("Form"),
                [N("BBox")] = Rect(0, 0, 100, 100),
                [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = Ref(30) } },
            },
            Encoding.ASCII.GetBytes("BT /F0 12 Tf (A) Tj ET")));
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("/Fm0 Do")));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary { [N("XObject")] = new PdfDictionary { [N("Fm0")] = Ref(40) } },
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);

        var context = new ConformanceContext(doc, ConformanceProfile.PdfA2b);
        Assert.Contains(context.ReferencedFonts, f => f.IsIndirect && f.ObjectNumber == 30);
        AssertWalkerAgreesWithContext(doc);
    }

    // ── branch 3: a tiling pattern stream's /Resources ─────────────────────────

    [Fact]
    public void Font_in_tiling_pattern_resources_is_collected()
    {
        var doc = new PdfDocument();
        doc.AddObject(30, 0, SimpleFont());
        doc.AddObject(50, 0, new PdfStream(
            new PdfDictionary
            {
                [N("Type")] = N("Pattern"),
                [N("PatternType")] = new PdfInteger(1),
                [N("PaintType")] = new PdfInteger(1),
                [N("TilingType")] = new PdfInteger(1),
                [N("BBox")] = Rect(0, 0, 10, 10),
                [N("XStep")] = new PdfInteger(10),
                [N("YStep")] = new PdfInteger(10),
                [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = Ref(30) } },
            },
            Encoding.ASCII.GetBytes("BT /F0 12 Tf (A) Tj ET")));
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("/P0 scn 0 0 100 100 re f")));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary { [N("Pattern")] = new PdfDictionary { [N("P0")] = Ref(50) } },
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);

        var context = new ConformanceContext(doc, ConformanceProfile.PdfA2b);
        Assert.Contains(context.ReferencedFonts, f => f.IsIndirect && f.ObjectNumber == 30);
        AssertWalkerAgreesWithContext(doc);
    }

    // ── branch 4: ExtGState /Font entry ─────────────────────────────────────────

    [Fact]
    public void Font_in_extgstate_font_entry_is_collected()
    {
        var doc = new PdfDocument();
        doc.AddObject(30, 0, SimpleFont());
        doc.AddObject(60, 0, new PdfDictionary
        {
            [N("Type")] = N("ExtGState"),
            [N("Font")] = new PdfArray(Ref(30), new PdfInteger(12)),
        });
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("/GS0 gs")));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary { [N("ExtGState")] = new PdfDictionary { [N("GS0")] = Ref(60) } },
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);

        var context = new ConformanceContext(doc, ConformanceProfile.PdfA2b);
        Assert.Contains(context.ReferencedFonts, f => f.IsIndirect && f.ObjectNumber == 30);
        AssertWalkerAgreesWithContext(doc);
    }

    // ── branch 5: an annotation's /AP appearance stream ─────────────────────────

    [Fact]
    public void Font_in_annotation_appearance_stream_is_collected()
    {
        var doc = new PdfDocument();
        doc.AddObject(30, 0, SimpleFont());
        doc.AddObject(70, 0, new PdfStream(
            new PdfDictionary
            {
                [N("Type")] = N("XObject"),
                [N("Subtype")] = N("Form"),
                [N("BBox")] = Rect(0, 0, 20, 20),
                [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = Ref(30) } },
            },
            Encoding.ASCII.GetBytes("BT /F0 8 Tf (x) Tj ET")));
        doc.AddObject(71, 0, new PdfDictionary
        {
            [N("Type")] = N("Annot"),
            [N("Subtype")] = N("FreeText"),
            [N("Rect")] = Rect(0, 0, 20, 20),
            [N("AP")] = new PdfDictionary { [N("N")] = Ref(70) },
        });
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), []));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(11),
            [N("Annots")] = new PdfArray(Ref(71)),
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);

        var context = new ConformanceContext(doc, ConformanceProfile.PdfA2b);
        Assert.Contains(context.ReferencedFonts, f => f.IsIndirect && f.ObjectNumber == 30);
        AssertWalkerAgreesWithContext(doc);
    }

    // ── branch 6: a Type3 font's own glyph /Resources ──────────────────────────

    [Fact]
    public void Font_in_type3_glyph_resources_is_collected()
    {
        var doc = new PdfDocument();
        doc.AddObject(30, 0, SimpleFont("Helvetica-Glyph"));
        doc.AddObject(80, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("0 0 d0 BT /F0 12 Tf (A) Tj ET")));
        doc.AddObject(81, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type3"),
            [N("FontBBox")] = Rect(0, 0, 1000, 1000),
            [N("FontMatrix")] = new PdfArray(
                new PdfReal(0.001), new PdfInteger(0), new PdfInteger(0),
                new PdfReal(0.001), new PdfInteger(0), new PdfInteger(0)),
            [N("CharProcs")] = new PdfDictionary { [N("g1")] = Ref(80) },
            [N("Encoding")] = N("WinAnsiEncoding"),
            // The Type3 glyph draws with the SAME 30 0 obj Type1 font via its own /Resources.
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = Ref(30) } },
        });
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F1 12 Tf (A) Tj ET")));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F1")] = Ref(81) } },
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);

        var context = new ConformanceContext(doc, ConformanceProfile.PdfA2b);
        // Both the Type3 font itself (81) and the Type1 font its glyph draws with (30) are referenced.
        Assert.Contains(context.ReferencedFonts, f => f.IsIndirect && f.ObjectNumber == 81);
        Assert.Contains(context.ReferencedFonts, f => f.IsIndirect && f.ObjectNumber == 30);
        AssertWalkerAgreesWithContext(doc);
    }

    // ── AcroForm /DR pool, gated on /NeedAppearances true ───────────────────────

    [Fact]
    public void AcroForm_dr_font_is_collected_when_needappearances_true()
    {
        var doc = new PdfDocument();
        doc.AddObject(30, 0, SimpleFont());
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), []));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(11),
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(90, 0, new PdfDictionary
        {
            [N("Fields")] = new PdfArray(),
            [N("NeedAppearances")] = PdfBoolean.True,
            [N("DR")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("Helv")] = Ref(30) } },
        });
        doc.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2), [N("AcroForm")] = Ref(90),
        });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);

        var context = new ConformanceContext(doc, ConformanceProfile.PdfA2b);
        Assert.Contains(context.ReferencedFonts, f => f.IsIndirect && f.ObjectNumber == 30);
        AssertWalkerAgreesWithContext(doc);
    }

    [Fact]
    public void AcroForm_dr_font_is_NOT_collected_when_needappearances_false_or_absent()
    {
        var doc = new PdfDocument();
        doc.AddObject(30, 0, SimpleFont());
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), []));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(11),
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(90, 0, new PdfDictionary
        {
            [N("Fields")] = new PdfArray(),
            [N("DR")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("Helv")] = Ref(30) } },
        });
        doc.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2), [N("AcroForm")] = Ref(90),
        });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);

        var context = new ConformanceContext(doc, ConformanceProfile.PdfA2b);
        Assert.DoesNotContain(context.ReferencedFonts, f => f.IsIndirect && f.ObjectNumber == 30);
        AssertWalkerAgreesWithContext(doc);
    }

    // ── Type0 -> DescendantFonts hop ─────────────────────────────────────────────

    [Fact]
    public void Type0_font_reaches_its_descendant_cidfont()
    {
        var doc = new PdfDocument();
        doc.AddObject(21, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType2"),
            [N("BaseFont")] = N("CIDFontX"),
            [N("CIDSystemInfo")] = new PdfDictionary
            {
                [N("Registry")] = new PdfString(Encoding.ASCII.GetBytes("Adobe")),
                [N("Ordering")] = new PdfString(Encoding.ASCII.GetBytes("Identity")),
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
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf <0001> Tj ET")));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = Ref(20) } },
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);

        var context = new ConformanceContext(doc, ConformanceProfile.PdfA2b);
        Assert.Contains(context.ReferencedFonts, f => f.IsIndirect && f.ObjectNumber == 20);
        Assert.Contains(context.ReferencedFonts, f => f.IsIndirect && f.ObjectNumber == 21);
        AssertWalkerAgreesWithContext(doc);
    }
}
