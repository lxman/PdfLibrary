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
/// PDF/UA-1 (ISO 14289-1, 7.21). These lock the two shipped checks against a REAL embedded font program
/// (the CC0 <c>PublicPixel.ttf</c> — every glyph advances exactly 1000 units in PDF space, so a declared
/// width of 1000 is consistent and anything else is not), never a faked <see cref="Rules"/> metric:
/// <list type="bullet">
///   <item>font metrics (6.2.11.5 / 7.21.5) — declared vs embedded advance width, TrueType + Type0;</item>
///   <item>.notdef glyph (6.2.11.8 / 7.21.8) — a shown Type0 code mapping to glyph 0.</item>
/// </list>
/// Real per-font-type detection breadth (Type1/CFF, CIDFontType0, the tolerance) is measured by the
/// veraPDF parity harness; these pin the profile-aware clause mapping, the message, and the FP-safe skips.
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
