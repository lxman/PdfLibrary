using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// PDF/A-2/3 clause 6.2.4.2 test 2 (<see cref="IccCmykOverprintRule"/>), calibrated against veraPDF's
/// PDICCBasedCMYK rule (<c>overprintFlag == false || OPM == 0</c>): overprint mode must not be 1 when an
/// ICCBased CMYK colour space (an ICCBased space whose profile stream has <c>/N 4</c>) is painted with the
/// matching overprint enabled — stroke overprint (/OP) for a stroke, fill overprint (/op) for a fill. The
/// check is evaluated at the paint operator against the live graphics state.
/// </summary>
public class IccCmykOverprintRuleTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfArray Rect() => new(new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792));
    private static byte[] C(string s) => Encoding.Latin1.GetBytes(s);

    /// <summary>An ExtGState with the given overprint keys (null = key absent).</summary>
    private static PdfDictionary Gs(bool? op, bool? fillOp, int? opm)
    {
        var d = new PdfDictionary { [N("Type")] = N("ExtGState") };
        if (op is { } o) d[N("OP")] = o ? PdfBoolean.True : PdfBoolean.False;
        if (fillOp is { } f) d[N("op")] = f ? PdfBoolean.True : PdfBoolean.False;
        if (opm is { } m) d[N("OPM")] = new PdfInteger(m);
        return d;
    }

    /// <summary>A one-page document whose resources expose /CS0 = [/ICCBased (N components)] and
    /// /GS0 = the given ExtGState, with the given content stream.</summary>
    private static PdfDocument Doc(int iccN, PdfDictionary extGState, string content)
    {
        var doc = new PdfDocument();
        doc.AddObject(10, 0, new PdfStream(new PdfDictionary { [N("N")] = new PdfInteger(iccN) }, [0x00]));
        doc.AddObject(20, 0, extGState);
        // A second ExtGState with a REAL-valued /OPM 0.0 (non-standard, but veraPDF truncates it to 0).
        doc.AddObject(21, 0, new PdfDictionary { [N("Type")] = N("ExtGState"), [N("OPM")] = new PdfReal(0.0) });
        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), C(content)));
        var resources = new PdfDictionary
        {
            [N("ColorSpace")] = new PdfDictionary { [N("CS0")] = new PdfArray(N("ICCBased"), Ref(10)) },
            [N("ExtGState")] = new PdfDictionary { [N("GS0")] = Ref(20), [N("GS1")] = Ref(21) },
        };
        doc.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(), [N("Resources")] = resources, [N("Contents")] = Ref(4),
        });
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    private static Finding[] Run(int iccN, PdfDictionary extGState, string content) =>
        [.. new IccCmykOverprintRule().Check(new ConformanceContext(Doc(iccN, extGState, content), ConformanceProfile.PdfA2b))];

    private static void AssertFlagged(Finding[] findings)
    {
        Finding f = Assert.Single(findings);
        Assert.Equal("icc-cmyk-overprint", f.RuleId);
        Assert.EndsWith("6.2.4.2", f.Clause);
        Assert.Equal(FindingSeverity.Error, f.Severity);
    }

    private const string Stroke = "q /GS0 gs /CS0 CS 0 0 0 1 SC 10 10 m 20 20 l S Q";
    private const string Fill = "q /GS0 gs /CS0 cs 0 0 0 1 sc 10 10 m 20 20 l f Q";

    [Fact]
    public void Icc_cmyk_stroke_with_stroke_overprint_and_opm_1_is_flagged()
    {
        AssertFlagged(Run(4, Gs(op: true, fillOp: false, opm: 1), Stroke));
    }

    [Fact]
    public void Icc_cmyk_fill_with_fill_overprint_and_opm_1_is_flagged()
    {
        AssertFlagged(Run(4, Gs(op: false, fillOp: true, opm: 1), Fill));
    }

    [Fact]
    public void Op_alone_sets_fill_overprint_so_cmyk_fill_with_opm_1_is_flagged()
    {
        // /OP present and /op absent: /OP also sets fill overprint.
        AssertFlagged(Run(4, Gs(op: true, fillOp: null, opm: 1), Fill));
    }

    [Fact]
    public void Icc_cmyk_fill_without_fill_overprint_passes()
    {
        // pass-b shape: stroke overprint on, but the CMYK is filled and fill overprint (op) is off.
        Assert.Empty(Run(4, Gs(op: true, fillOp: false, opm: 1), Fill));
    }

    [Fact]
    public void Icc_cmyk_stroke_without_stroke_overprint_passes()
    {
        // pass-c shape: fill overprint on, but the CMYK is stroked and stroke overprint (OP) is off.
        Assert.Empty(Run(4, Gs(op: false, fillOp: true, opm: 1), Stroke));
    }

    [Fact]
    public void Icc_rgb_with_overprint_and_opm_1_passes()
    {
        // pass-a shape: ICCBased RGB (N 3), not CMYK — the rule does not apply.
        Assert.Empty(Run(3, Gs(op: true, fillOp: true, opm: 1), Fill));
    }

    [Fact]
    public void Icc_cmyk_overprint_with_opm_0_passes()
    {
        Assert.Empty(Run(4, Gs(op: true, fillOp: true, opm: 0), Fill));
    }

    [Fact]
    public void Icc_cmyk_overprint_opm_1_but_never_painted_passes()
    {
        // Colour space set and colour set, but the path is ended with 'n' (no paint).
        Assert.Empty(Run(4, Gs(op: true, fillOp: true, opm: 1), "q /GS0 gs /CS0 cs 0 0 0 1 sc 10 10 m 20 20 l n Q"));
    }

    [Fact]
    public void A_restore_before_paint_undoes_the_overprint_state()
    {
        // Overprint turned on inside a q/Q, restored before the CMYK fill outside it.
        Assert.Empty(Run(4, Gs(op: true, fillOp: true, opm: 1),
            "/CS0 cs 0 0 0 1 sc q /GS0 gs Q 10 10 m 20 20 l f"));
    }

    [Fact]
    public void A_real_valued_opm_of_zero_overrides_a_prior_opm_of_one()
    {
        // /GS0 sets OPM 1 (+overprint); /GS1 sets OPM 0.0 (a real). veraPDF truncates the real to 0, so the
        // fill passes. The rule must read a real /OPM too, not ignore it and keep the stale OPM 1.
        Assert.Empty(Run(4, Gs(op: true, fillOp: true, opm: 1),
            "q /CS0 cs /GS0 gs /GS1 gs 0 0 0 1 sc 10 10 m 20 20 l f Q"));
    }

    [Fact]
    public void A_device_cmyk_fill_operator_resets_the_icc_colour_space()
    {
        // After selecting ICCBased CMYK, a 'k' operator sets DeviceCMYK (not ICCBased) — rule no longer applies.
        Assert.Empty(Run(4, Gs(op: true, fillOp: true, opm: 1), "q /GS0 gs /CS0 cs 0 0 0 1 k 10 10 m 20 20 l f Q"));
    }
}
