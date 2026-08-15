using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Fonts.Embedded;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Issue 26: FontProgramRule's TrueType path guarded gid == 0 but accepted a real gid whose
/// advance is 0 as a measurement, manufacturing a width finding (0 vs /Widths). Reproducer:
/// "Visual Studio Icon Library - Common Elements.pdf", ABCDEE+Calibri, code 10 (LINE FEED) in a
/// Tj string — Mac-Roman cmap fallback returns a non-zero gid, advance 0, vs /Widths 507.
/// These are ordinary unit tests, NOT LocalOnly: the font program is synthesized in memory.
/// </summary>
public class FontProgramZeroAdvanceTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    // ── minimal-TrueType byte builders (big-endian, mirroring CmapSubtablePreferenceTests) ────
    private static void U16(List<byte> b, int v) { b.Add((byte)(v >> 8)); b.Add((byte)v); }
    private static void U32(List<byte> b, uint v)
    { b.Add((byte)(v >> 24)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 8)); b.Add((byte)v); }

    private static byte[] Head()
    {
        var b = new List<byte>();
        U32(b, 0x00010000);            // version 1.0
        U32(b, 0);                     // fontRevision
        U32(b, 0);                     // checkSumAdjustment
        U32(b, 0x5F0F3CF5);            // magicNumber
        U16(b, 0);                     // flags
        U16(b, 1000);                  // unitsPerEm
        for (var i = 0; i < 16; i++) b.Add(0); // created + modified (2 × longdatetime)
        U16(b, 0); U16(b, 0); U16(b, 0); U16(b, 0); // xMin yMin xMax yMax
        U16(b, 0);                     // macStyle
        U16(b, 8);                     // lowestRecPPEM
        U16(b, 2);                     // fontDirectionHint
        U16(b, 0);                     // indexToLocFormat
        U16(b, 0);                     // glyphDataFormat
        return b.ToArray();            // 54 bytes
    }

    private static byte[] Maxp(ushort numGlyphs)
    {
        var b = new List<byte>();
        U32(b, 0x00010000);
        U16(b, numGlyphs);
        for (var i = 0; i < 13; i++) U16(b, 0); // maxPoints … maxComponentDepth
        return b.ToArray();            // 32 bytes
    }

    private static byte[] Hhea(ushort numberOfHMetrics)
    {
        var b = new List<byte>();
        U32(b, 0x00010000);
        U16(b, 800);                   // ascender
        U16(b, unchecked((ushort)-200)); // descender
        U16(b, 0);                     // lineGap
        U16(b, 500);                   // advanceWidthMax
        for (var i = 0; i < 3; i++) U16(b, 0); // minLSB, minRSB, xMaxExtent
        U16(b, 1); U16(b, 0); U16(b, 0);       // caretSlopeRise/Run, caretOffset
        for (var i = 0; i < 4; i++) U16(b, 0); // reserved
        U16(b, 0);                     // metricDataFormat
        U16(b, numberOfHMetrics);
        return b.ToArray();            // 36 bytes
    }

    /// <summary>hmtx: gid 0 advances 500; gid 1 advances 0 — the defect's trigger.</summary>
    private static byte[] Hmtx()
    {
        var b = new List<byte>();
        U16(b, 500); U16(b, 0);        // gid 0: advance 500, lsb 0
        U16(b, 0); U16(b, 0);          // gid 1: advance 0, lsb 0
        return b.ToArray();
    }

    /// <summary>A lone (1,0) Mac-Roman format-6 subtable mapping code 10 → gid 1.</summary>
    private static byte[] CmapMacFormat6()
    {
        var b = new List<byte>();
        U16(b, 0);                     // table version
        U16(b, 1);                     // numTables
        U16(b, 1); U16(b, 0);          // platform 1 (Macintosh), encoding 0 (Roman)
        U32(b, 12);                    // subtable offset
        U16(b, 6);                     // format 6
        U16(b, 12);                    // length (5 × u16 header + 1 × u16 entry)
        U16(b, 0);                     // language
        U16(b, 10);                    // firstCode = 10 (LINE FEED)
        U16(b, 1);                     // entryCount
        U16(b, 1);                     // glyphIndexArray = [gid 1]
        return b.ToArray();
    }

    private static byte[] FontBytes() => MinimalSfnt.Build(
        ("head", Head()),
        ("maxp", Maxp(2)),
        ("hhea", Hhea(2)),
        ("hmtx", Hmtx()),
        ("cmap", CmapMacFormat6()),
        ("glyf", new byte[4]));        // content unused; presence required for IsValid

    // ── fixture honesty: the program must actually reproduce the defect's preconditions ───────
    [Fact]
    public void Fixture_program_maps_code_10_to_a_real_gid_with_zero_advance()
    {
        var metrics = new EmbeddedFontMetrics(FontBytes());
        Assert.True(metrics.IsValid);
        Assert.Equal(1, metrics.GetGlyphId(10));
        Assert.Equal(0, metrics.GetAdvanceWidth(1));
        Assert.Equal(500, metrics.GetAdvanceWidth(0)); // a nonzero advance elsewhere, so hmtx parsed
    }

    // ── the rule-level gate (spec gate 5) ─────────────────────────────────────────────────────
    // Why code 10 reaches TrueTypeAdvance's raw-code fallback at all: this font has no /Encoding
    // entry, so PdfFont defaults it to StandardEncoding (Task 2's behaviour), and StandardEncoding
    // has no name at code 10 (it is a control code in every base encoding) — so GetGlyphName(10) is
    // null and the Unicode/AGL path is skipped before it can even try. Separately, the fixture's cmap
    // is a lone (1,0) Mac-Roman subtable: there is no (3,1) Windows-Unicode subtable for
    // GetUnicodeAdvanceWidth to consult, so even a font whose encoding DID name code 10 would still
    // fall through to the raw-code path here, keying strictly off the raw code via GetGlyphId. That
    // makes this gate robust to future encoding-name changes — it is exercising the raw-code fallback
    // itself, not merely the absence of a name for code 10.
    private static PdfDocument ZeroAdvanceDoc()
    {
        byte[] font = FontBytes();
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
            [N("FontDescriptor")] = Ref(2),
        });
        // Show code 10 in a Tj hex string so the used-glyph walk reaches the font.
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes("BT /F0 12 Tf <0A> Tj ET")));
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

    [Fact]
    public void Zero_program_advance_produces_no_width_finding()
    {
        // Before the fix: a 6.2.11.5 finding, |507 - 0| = 507 units. After: the zero advance is
        // unmeasurable (same treatment as gid 0) and the code is skipped — no finding at all.
        Finding[] findings = new FontProgramRule()
            .Check(new ConformanceContext(ZeroAdvanceDoc(), ConformanceProfile.PdfA2b))
            .ToArray();
        Assert.Empty(findings);
    }
}
