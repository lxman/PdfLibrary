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

    // The minimal-TrueType byte builders live in ZeroAdvanceSfntFixture (promoted, F-4a Task 1),
    // shared with ProgramWidthResolverTests so both exercise the exact same program shape.

    // ── fixture honesty: the program must actually reproduce the defect's preconditions ───────
    [Fact]
    public void Fixture_program_maps_code_10_to_a_real_gid_with_zero_advance()
    {
        var metrics = new EmbeddedFontMetrics(ZeroAdvanceSfntFixture.FontBytes());
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
        byte[] font = ZeroAdvanceSfntFixture.FontBytes();
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
        // unmeasurable (same treatment as gid 0) and the code is skipped -- no WIDTH finding.
        //
        // Separately (this branch, 2026-08-20): code 10 carries no /Encoding entry at all, so ISO
        // 32000-1 9.6.6's "undefined code renders .notdef" widening now ALSO fires a genuine
        // 6.2.11.8 finding on this exact fixture -- unrelated to the zero-advance skip this test
        // exists to pin, and expected rather than a regression: this fixture's own point (issue
        // 26's header comment above) is reproducing "Visual Studio Icon Library - Common
        // Elements.pdf"'s real undefined code 10, which the local-708 real-document scan
        // independently confirmed picks up this exact new finding post-branch.
        Finding[] findings = new FontProgramRule()
            .Check(new ConformanceContext(ZeroAdvanceDoc(), ConformanceProfile.PdfA2b))
            .ToArray();
        Assert.DoesNotContain(findings, f => ParitySnapshot.ClauseKey(f.Clause) == "6.2.11.5");
        Finding f = Assert.Single(findings);
        Assert.Equal("6.2.11.8", ParitySnapshot.ClauseKey(f.Clause));
        Assert.Contains(".notdef", f.Message);
    }
}
