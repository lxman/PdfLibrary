using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts.Remediation;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Fonts.Embedded;

namespace PdfLibrary.Tests.Fonts.Remediation;

/// <summary>
/// F-4a Task 3/4 shared fixture: the <c>/Widths [507]</c> vs hmtx-450 mismatch document and the
/// planner helper, used by both <see cref="WidthPatchProposalTests"/> (Task 3, planner) and
/// <c>WidthPatchApplyTests</c> (Task 4, editor write + close-by-construction gate) so the two suites
/// cannot silently diverge on the document shape the gate relies on.
/// </summary>
internal static class WidthPatchFixtures
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    public static FontRemediationPlanner Planner() => new(new StubFontProvider(null));

    /// <summary>Same shape as FontProgramZeroAdvanceTests.ZeroAdvanceDoc / ProgramWidthResolverTests.Doc,
    /// but with a nonzero (mismatched) gid-1 advance: /Widths [507] vs hmtx advance 450.
    ///
    /// <para>Carries an explicit <c>/Encoding</c> naming code 10 (added 2026-08-20, this branch):
    /// without it, code 10 has no glyph name under the default encoding, and ISO 32000-1 9.6.6's
    /// "undefined code renders .notdef" widening (<see cref="PdfLibrary.Conformance.Rules.FontProgramRule"/>)
    /// now ALSO fires a genuine 6.2.11.8 finding alongside the intended width mismatch -- this
    /// fixture's whole point is a font with EXACTLY one problem (the width), so an assigned name
    /// restores that. Safe: the font's lone (1,0) Mac-Roman cmap subtable has no (3,1)
    /// Windows-Unicode counterpart, so the width check's raw-code cmap fallback (see
    /// FontProgramZeroAdvanceTests' header comment) is unchanged by naming the code -- only the
    /// notdef widening's null-name gate is affected.</para></summary>
    public static PdfDocument MismatchDoc()
    {
        byte[] font = ZeroAdvanceSfntFixture.FontBytes(gid1Advance: 450);
        var doc = new PdfDocument();
        doc.AddObject(3, 0, new PdfStream(
            new PdfDictionary { [N("Length1")] = new PdfInteger(font.Length) }, font));
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEE+ZeroAdvance"),
            [N("Flags")] = new PdfInteger(32),     // non-symbolic
            [N("FontFile2")] = Ref(3),
        });
        doc.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("ABCDEE+ZeroAdvance"),
            [N("FirstChar")] = new PdfInteger(10),
            [N("LastChar")] = new PdfInteger(10),
            [N("Widths")] = new PdfArray(new PdfInteger(507)),
            [N("Encoding")] = new PdfDictionary
            {
                [N("Differences")] = new PdfArray(new PdfInteger(10), N("A")),
            },
            [N("FontDescriptor")] = Ref(2),
        });
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes("BT /F0 12 Tf <0A> Tj ET")));
        AddSinglePageCatalog(doc, font: 1);
        return doc;
    }

    /// <summary>A font whose only font-program finding is 6.2.11.8 (a shown code encoded to
    /// ".notdef" via /Differences); no /Widths array, so 6.2.11.5 can never fire. Shared with F-4b
    /// Task 5 (<c>ReplaceProgramProposalTests</c>'s simple-font-scope decline fact), which needs the
    /// same simple-font notdef fixture <see cref="WidthPatchProposalTests"/> already established.
    /// </summary>
    public static PdfDocument NotdefOnlyDoc()
    {
        byte[] font = ZeroAdvanceSfntFixture.FontBytes(gid1Advance: 450);
        var doc = new PdfDocument();
        doc.AddObject(3, 0, new PdfStream(
            new PdfDictionary { [N("Length1")] = new PdfInteger(font.Length) }, font));
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEE+ZeroAdvance"),
            [N("Flags")] = new PdfInteger(32),     // non-symbolic
            [N("FontFile2")] = Ref(3),
        });
        doc.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("ABCDEE+ZeroAdvance"),
            [N("FirstChar")] = new PdfInteger(65),
            [N("LastChar")] = new PdfInteger(65),
            [N("Encoding")] = new PdfDictionary
            {
                [N("BaseEncoding")] = N("WinAnsiEncoding"),
                [N("Differences")] = new PdfArray(new PdfInteger(65), N(".notdef")),
            },
            [N("FontDescriptor")] = Ref(2),
        });
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes("BT /F0 12 Tf <41> Tj ET")));
        AddSinglePageCatalog(doc, font: 1);
        return doc;
    }

    public static void AddSinglePageCatalog(PdfDocument doc, int font)
    {
        AddSinglePageCatalog(doc, new PdfDictionary { [N("F0")] = Ref(font) });
    }

    /// <summary>Same single-page catalog, but with TWO font resources (<c>/F0</c>, <c>/F1</c>) —
    /// for a fixture whose page draws through two distinct wrapper fonts (e.g. a shared program
    /// holder or a shared descriptor). Kept as an overload rather than widening the single-font
    /// signature's callers.</summary>
    public static void AddSinglePageCatalog(PdfDocument doc, int font1, int font2)
    {
        AddSinglePageCatalog(doc, new PdfDictionary { [N("F0")] = Ref(font1), [N("F1")] = Ref(font2) });
    }

    /// <summary>Same idiom, THREE font resources (<c>/F0</c>-<c>/F2</c>) — for a fixture that needs a
    /// third, undrawn font present purely so <c>FontInventory.Read</c> (which walks REFERENCED fonts,
    /// not just drawn ones) discovers it, e.g. a blocking sibling that shares a holder key but is
    /// never itself shown in the content stream.</summary>
    public static void AddSinglePageCatalog(PdfDocument doc, int font1, int font2, int font3)
    {
        AddSinglePageCatalog(doc, new PdfDictionary
        {
            [N("F0")] = Ref(font1), [N("F1")] = Ref(font2), [N("F2")] = Ref(font3),
        });
    }

    private static void AddSinglePageCatalog(PdfDocument doc, PdfDictionary fontResources)
    {
        doc.AddObject(22, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(21),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary
            {
                [N("Font")] = fontResources,
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
    }
}
