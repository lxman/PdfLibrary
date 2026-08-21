using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Conformance;
using PdfLibrary.Tests.Fonts.Embedded;
using Xunit;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// Task 10 fix round (issues 27-28 follow-up review, 2026-08-16): the AGL completion added ~4,000
/// reverse (Unicode → name) entries. <see cref="PdfFontEncoding.SetUnicode"/> uses that reverse map
/// as a rendering-fallback to DERIVE a glyph name for a code that has Unicode but no
/// encoding-assigned name (e.g. every code in a WinAnsiEncoding-based font — <c>CreateWinAnsiEncoding</c>
/// calls <c>SetUnicode</c> exclusively, never <c>SetCharacterName</c>). <c>FontProgramRule</c>'s CFF
/// glyph-present resolver (<c>ResolveSimpleGlyph</c>) treated that derived name as authoritative: a
/// miss against the font's own (subsetted) charset became a confident <c>NotDef</c> — a false
/// positive, since a derived name is this engine's own reconstruction, never something the document
/// or font program actually asserts. Confirmed against the real corpus: 4 CC-MAIN documents (
/// <c>2000_2000302.pdf</c>, <c>2000_2000381.pdf</c>, <c>2000_2000506.pdf</c>, <c>6000_6000536.pdf</c>)
/// picked up 7 new "glyph not present" findings post-Task-10 that a direct veraPDF re-check on all
/// four showed were NOT real (6.2.11.4.1 test 2 passes on every one).
///
/// <para>Fix: <see cref="PdfFontEncoding"/> now tracks which codes' names were derived
/// (<see cref="PdfFontEncoding.IsDerivedName"/>); <c>ResolveSimpleGlyph</c> returns
/// <c>SimpleGlyphResolution.Unknown</c> for a derived-name code in its CFF branch only, the same
/// "skip rather than guess" contract the resolver already applies to unmapped names and
/// predefined-charset CFF. Round 2 removed the analogous TrueType-branch gate — provenance carries
/// no information there, since the TrueType arm only ever uses a name as a courier for the
/// encoding's own Unicode value; see <see cref="TrueTypeDerivedNameTests"/> for that story.</para>
/// </summary>
public class DerivedNameProvenanceTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfArray Rect(int x0, int y0, int x1, int y1) =>
        new(new PdfInteger(x0), new PdfInteger(y0), new PdfInteger(x1), new PdfInteger(y1));

    // Probe code 169 ('©' / "copyright" under WinAnsiEncoding). Moved off code 65 ('A') 2026-08-20
    // (this branch): Task 6 assigns WinAnsi's ASCII band (32-126) BY NAME now, not by reverse-AGL,
    // so 'A' is no longer a derived name and can't probe the derived-name premise any more --
    // WinAnsiEncoding's Latin-1 Supplement band (160-255) is the part Task 6 left as SetUnicode-only
    // (see PdfFontEncoding.CreateWinAnsiEncoding), so it is still reverse-AGL derived. The fixture's
    // built-in CFF Encoding maps a DIFFERENT code (90) to its one custom glyph, and its charset holds
    // only that custom glyph plus "emdash" — neither is "copyright" — so code 169 is unmapped by
    // both the built-in encoding AND a by-name charset lookup: exactly the shape that, pre-fix, made
    // a derived name look like a confident absence.
    private const byte ProbeCode = 169; // '©' — reverse-AGL name "copyright"
    private const byte BuiltInMappedCode = 90; // unrelated to the probe code, deliberately
    private const string CustomGlyphName = "afii10034"; // a real AGL name, not "copyright" or "emdash"

    private static byte[] CffBytes =>
        SymbolicCffFixtureFont.Build(BuiltInMappedCode, CustomGlyphName, customAdvance: 576, emdashAdvance: 1000);

    /// <summary>One-page document, non-symbolic CFF font, <c>/Encoding</c> = a dict whose
    /// <c>/BaseEncoding</c> is <c>/WinAnsiEncoding</c> and carries NO <c>/Differences</c> — so every
    /// code's name (including <see cref="ProbeCode"/>'s) is SetUnicode-derived, never
    /// SetCharacterName-assigned. Shows <see cref="ProbeCode"/> in its content stream.</summary>
    private static PdfDocument BuildDoc()
    {
        var doc = new PdfDocument();
        doc.AddObject(12, 0, new PdfStream(new PdfDictionary(), CffBytes));
        doc.AddObject(11, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("DerivedNameCffFixture"),
            [N("Flags")] = new PdfInteger(34), // Serif | Nonsymbolic
            [N("FontFile3")] = Ref(12),
        });

        var widths = new PdfObject[255 - 32 + 1];
        for (var i = 0; i < widths.Length; i++) widths[i] = new PdfInteger(0);

        var fontDict = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type1"),
            [N("BaseFont")] = N("DerivedNameCffFixture"),
            [N("FirstChar")] = new PdfInteger(32),
            [N("LastChar")] = new PdfInteger(255),
            [N("Widths")] = new PdfArray(widths),
            [N("FontDescriptor")] = Ref(11),
            [N("Encoding")] = new PdfDictionary { [N("BaseEncoding")] = N("WinAnsiEncoding") },
        };
        doc.AddObject(10, 0, fontDict);

        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes($"BT /F0 12 Tf <{ProbeCode:X2}> Tj ET")));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(4),
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = Ref(10) } },
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    private static PdfFont FontFrom(PdfDocument doc)
    {
        var dict = (PdfDictionary)doc.Objects[10];
        PdfFont? font = PdfFont.Create(dict, doc);
        Assert.NotNull(font);
        return font!;
    }

    [Fact]
    public void Fixture_honesty_probe_code_is_unmapped_by_both_built_in_encoding_and_charset_name()
    {
        var metrics = new PdfLibrary.Fonts.Embedded.EmbeddedFontMetrics(CffBytes);
        Assert.True(metrics.IsValid);
        Assert.True(metrics.IsCffFont);
        Assert.Equal((ushort)0, metrics.GetGlyphIdByCffEncoding(ProbeCode));
        Assert.Equal((ushort)0, metrics.GetGlyphIdByName("copyright"));
    }

    [Fact]
    public void Winansi_base_encoding_derives_the_probe_codes_name()
    {
        using PdfDocument doc = BuildDoc();
        PdfFont font = FontFrom(doc);
        Assert.Equal("copyright", font.Encoding!.GetGlyphName(ProbeCode));
        Assert.True(font.Encoding.IsDerivedName(ProbeCode));
    }

    [Fact]
    public void Derived_name_code_absent_from_program_yields_no_glyph_present_finding()
    {
        // The regression this fix closes: a code whose name came from SetUnicode's reverse-AGL
        // fallback, not the document's own encoding data, must not be treated as a confident
        // .notdef when it misses the font's (subsetted) charset and built-in encoding.
        using PdfDocument doc = BuildDoc();
        Finding[] findings = new FontProgramRule()
            .Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)).ToArray();

        Assert.DoesNotContain(findings, f => ParitySnapshot.ClauseKey(f.Clause) == "6.2.11.4.1");
    }

    [Fact]
    public void Encoding_assigned_name_absent_from_program_still_yields_a_finding()
    {
        // Sibling positive case, same fixture: an EXPLICITLY assigned name (/Differences, SetCharacterName)
        // that misses the charset and built-in encoding must still fire — the fix must not suppress a
        // genuine glyph-present defect, only a derived-name guess.
        using PdfDocument doc = BuildDoc();
        var dict = (PdfDictionary)doc.Objects[10];
        dict[N("Encoding")] = new PdfDictionary
        {
            [N("Differences")] = new PdfArray(new PdfInteger(ProbeCode), new PdfName("nonexistentglyph")),
        };
        PdfFont font = FontFrom(doc);
        Assert.Equal("nonexistentglyph", font.Encoding!.GetGlyphName(ProbeCode));
        Assert.False(font.Encoding.IsDerivedName(ProbeCode));

        Finding[] findings = new FontProgramRule()
            .Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)).ToArray();
        Assert.Contains(findings, f => ParitySnapshot.ClauseKey(f.Clause) == "6.2.11.4.1");
    }
}
