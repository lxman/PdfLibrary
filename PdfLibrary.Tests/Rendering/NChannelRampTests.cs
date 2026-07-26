using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// ISO 32000-2 Table 71: for an NChannel space, /Attributes /Colorants /&lt;name&gt; is a full Separation
/// describing "the appearance of that colorant alone" — authoritative for that component, where zeroing
/// the other inputs of the whole-space transform is only an approximation.
///
/// <para><c>Parse</c>/<c>ParseWithDoc</c> below are local to this file, matching the established
/// convention in this test project: <c>ColourantComponentTests</c>, <c>SpotColorSpaceTests</c>,
/// <c>OriginForColorSpaceObjectTests</c>, <c>ColorSpaceResolverCharacterizationTests</c> and
/// <c>ColorSpaceResolverPaintsNothingTests</c> each already carry their own private copy of this exact
/// helper rather than sharing one. Following that precedent (instead of introducing a lone shared
/// <c>PdfTestHelpers</c> class for just this file) keeps the new file consistent with every existing
/// neighbour.</para>
/// </summary>
public class NChannelRampTests
{
    /// <summary>A whole-space transform that IGNORES its inputs and always returns 0.9 on cyan, versus a
    /// /Colorants Separation that ramps linearly to 0.5 cyan. The two disagree at every tint except 0, so
    /// the ramp's SOURCE is what the assertion measures — not merely that a ramp was produced.</summary>
    private const string WholeSpaceAlways09 =
        "<< /FunctionType 2 /Domain [0 1 0 1] /C0 [0.9 0 0 0] /C1 [0.9 0 0 0] /N 1 "
        + "/Range [0 1 0 1 0 1 0 1] >>";

    private const string SpotOwnSeparation =
        "/Colorants << /Spot1 [/Separation /Spot1 /DeviceCMYK "
        + "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0.5 0 0 0] /N 1 >>] >>";

    private static PdfArray Parse(string pdfArrayLiteral)
    {
        byte[] pdf = ColourConformancePage.Build(pdfArrayLiteral, "1 0 0 rg 0 0 1 1 re f");
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        PdfDictionary colorSpaces = page.GetResources()!.GetColorSpaces()!;
        return (PdfArray)colorSpaces[new PdfName("Cs0")]!;
    }

    /// <summary>Same idiom as <c>ColourantComponentTests.ParseWithDoc</c> / <c>SpotColorSpaceTests.ParseWithDoc</c>
    /// — keeps the document alive so <see cref="ColorSpaceResolver.Deref"/> actually resolves indirect
    /// references. Caller disposes via <c>using (doc)</c>.</summary>
    private static (PdfArray Array, PdfDocument Doc) ParseWithDoc(
        string pdfArrayLiteral, params string[] extraObjects)
    {
        byte[] pdf = ColourConformancePage.Build(pdfArrayLiteral, "1 0 0 rg 0 0 1 1 re f",
            withFont: false, extraResources: "", extraObjects: extraObjects);
        PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf));
        PdfPage page = doc.GetPage(0)!;
        PdfDictionary colorSpaces = page.GetResources()!.GetColorSpaces()!;
        return ((PdfArray)colorSpaces[new PdfName("Cs0")]!, doc);
    }

    // --- Row: NChannel, /Colorants /<name> is a usable Separation -> ramp from that Separation ---

    [Fact]
    public void NChannelComponent_RampComesFromItsOwnColorantsSeparation()
    {
        PdfArray space = Parse(
            "[/DeviceN [/Spot1 /Cyan] /DeviceCMYK " + WholeSpaceAlways09
            + " << /Subtype /NChannel " + SpotOwnSeparation + " >>]");

        (double[][]? ramp, _) = ColorSpaceResolver.BuildTintRamp(space, null, 0, 2);

        Assert.NotNull(ramp);
        Assert.Equal(0.0, ramp![0][0], 3);      // tint 0  -> C0 = 0.0, NOT the whole-space 0.9
        Assert.Equal(0.25, ramp[128][0], 2);    // tint ~.5 -> 0.25
        Assert.Equal(0.5, ramp[255][0], 3);     // tint 1  -> C1 = 0.5
    }

    [Fact]
    public void NChannelComponent_SolidComesFromTheSameColorantsSeparationAsTheRamp()
    {
        // Self-review requirement: the solid swatch must come from the SAME source as the ramp, so the
        // two cannot disagree. Proven differentially: the /Colorants Separation (C1 = 0.5 cyan) and the
        // whole-space transform (always 0.9 cyan) must produce visibly DIFFERENT solids — if the solid
        // quietly kept using the whole-space evaluator while the ramp switched to /Colorants, this would
        // stay green with the isolated-evaluation solid.
        PdfArray withColorants = Parse(
            "[/DeviceN [/Spot1 /Cyan] /DeviceCMYK " + WholeSpaceAlways09
            + " << /Subtype /NChannel " + SpotOwnSeparation + " >>]");
        PdfArray withoutColorants = Parse(
            "[/DeviceN [/Spot1 /Cyan] /DeviceCMYK " + WholeSpaceAlways09 + " << /Subtype /NChannel >>]");

        (_, (byte R, byte G, byte B) solidWithColorants) =
            ColorSpaceResolver.BuildTintRamp(withColorants, null, 0, 2);
        (_, (byte R, byte G, byte B) solidIsolated) =
            ColorSpaceResolver.BuildTintRamp(withoutColorants, null, 0, 2);

        Assert.NotEqual(solidIsolated, solidWithColorants);
    }

    // --- Row: NChannel, /Colorants absent -> isolated evaluation ---

    [Fact]
    public void NChannelComponentWithoutAColorantsEntry_FallsBackToTheIsolatedEvaluation()
    {
        PdfArray space = Parse(
            "[/DeviceN [/Spot1 /Cyan] /DeviceCMYK " + WholeSpaceAlways09 + " << /Subtype /NChannel >>]");

        (double[][]? ramp, _) = ColorSpaceResolver.BuildTintRamp(space, null, 0, 2);

        Assert.NotNull(ramp);
        Assert.Equal(0.9, ramp![255][0], 3);    // today's behaviour, unchanged
    }

    // --- Row: NChannel, no /Colorants entry for THIS name -> isolated evaluation ---
    // Distinct from the row above: here /Colorants is present and has entries, just not for Spot1.

    [Fact]
    public void ColorantsDictionaryPresentButNoEntryForThisName_FallsBackToTheIsolatedEvaluation()
    {
        PdfArray space = Parse(
            "[/DeviceN [/Spot1 /Cyan] /DeviceCMYK " + WholeSpaceAlways09
            + " << /Subtype /NChannel /Colorants << /OtherSpot [/Separation /OtherSpot /DeviceCMYK "
            + "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0.2 0 0 0] /N 1 >>] >> >>]");

        (double[][]? ramp, _) = ColorSpaceResolver.BuildTintRamp(space, null, 0, 2);

        Assert.NotNull(ramp);
        Assert.Equal(0.9, ramp![255][0], 3);
    }

    // --- Row: Not NChannel -> byte-identical to today (the 50-corpus-file protection) ---

    [Fact]
    public void PlainDeviceN_IsUnaffected_EvenWithAColorantsDictionary()
    {
        // /Subtype defaults to DeviceN (Table 70). Row 5-3's per-component rule is NChannel-only, and the
        // 50 non-NChannel corpus files depend on this staying byte-identical.
        PdfArray space = Parse(
            "[/DeviceN [/Spot1 /Cyan] /DeviceCMYK " + WholeSpaceAlways09
            + " << " + SpotOwnSeparation + " >>]");

        (double[][]? ramp, _) = ColorSpaceResolver.BuildTintRamp(space, null, 0, 2);

        Assert.NotNull(ramp);
        Assert.Equal(0.9, ramp![255][0], 3);
    }

    // --- Row: entry is not a Separation array -> isolated evaluation ---

    [Fact]
    public void ColorantsEntryThatIsNotASeparation_FallsBackToTheIsolatedEvaluation()
    {
        PdfArray space = Parse(
            "[/DeviceN [/Spot1 /Cyan] /DeviceCMYK " + WholeSpaceAlways09
            + " << /Subtype /NChannel /Colorants << /Spot1 /DeviceRGB >> >>]");

        (double[][]? ramp, _) = ColorSpaceResolver.BuildTintRamp(space, null, 0, 2);

        Assert.NotNull(ramp);
        Assert.Equal(0.9, ramp![255][0], 3);
    }

    // --- Row: entry's alternate is not CMYK/Gray -> isolated evaluation ---

    [Fact]
    public void ColorantsEntryWithANonCmykAlternate_FallsBackToTheIsolatedEvaluation()
    {
        // BuildTintToCmyk accepts only DeviceCMYK and DeviceGray alternates; a DeviceRGB Separation isn't
        // reducible to plates here, mirroring ColourantComponentTests.
        // ColorantsEntryWithANonCmykAlternate_LeavesTheAlternateNull for the single-tint path.
        PdfArray space = Parse(
            "[/DeviceN [/Spot1 /Cyan] /DeviceCMYK " + WholeSpaceAlways09
            + " << /Subtype /NChannel /Colorants << /Spot1 [/Separation /Spot1 /DeviceRGB "
            + "<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0 0 0] /N 1 >>] >> >>]");

        (double[][]? ramp, _) = ColorSpaceResolver.BuildTintRamp(space, null, 0, 2);

        Assert.NotNull(ramp);
        Assert.Equal(0.9, ramp![255][0], 3);
    }

    // --- Row: NChannel, entry's tint transform THROWS on evaluate -> isolated evaluation, no throw ---

    /// <summary>A Type 0 (Sampled) function whose /Domain declares 2 inputs, wired as a single-input
    /// Separation's tint transform. <c>PdfFunction.Create</c> succeeds (nothing at creation time
    /// cross-checks Domain against the colour space's declared input count), but
    /// <c>SampledFunction.Evaluate</c> reads 2 input slots from the 1-element array
    /// <c>OwnColorantRamp</c> supplies, throwing <c>IndexOutOfRangeException</c>. Same "createable but
    /// throws" shape <c>TintRampTests.ThrowingTintTransform</c> pins for the whole-space case — built via
    /// direct <see cref="PdfObject"/> construction (as that test does) because the mismatched-domain
    /// sampled function needs real binary sample bytes a PDF-literal string can't conveniently carry.</summary>
    private static PdfStream MismatchedDomainSampledFunction()
    {
        var d = new PdfDictionary();
        d.Add(new PdfName("FunctionType"), new PdfInteger(0));
        d.Add(new PdfName("Domain"), new PdfArray(new PdfReal(0), new PdfReal(1), new PdfReal(0), new PdfReal(1)));
        d.Add(new PdfName("Range"), new PdfArray(new PdfReal(0), new PdfReal(1)));
        d.Add(new PdfName("Size"), new PdfArray(new PdfInteger(2), new PdfInteger(2)));
        d.Add(new PdfName("BitsPerSample"), new PdfInteger(8));
        return new PdfStream(d, new byte[4]);
    }

    private static PdfStream Type4(string program, double[] domain, double[] range)
    {
        var d = new PdfDictionary();
        d.Add(new PdfName("FunctionType"), new PdfInteger(4));
        d.Add(new PdfName("Domain"), new PdfArray(domain.Select(v => (PdfObject)new PdfReal(v)).ToArray()));
        d.Add(new PdfName("Range"), new PdfArray(range.Select(v => (PdfObject)new PdfReal(v)).ToArray()));
        return new PdfStream(d, Encoding.ASCII.GetBytes(program));
    }

    [Fact]
    public void ColorantsEntryWhoseTintTransformThrowsOnEvaluate_FallsBackToTheIsolatedEvaluation()
    {
        // Whole-space DeviceN tint transform: { 0 0 } pushes two zeros, leaving [a b 0 0] as CMYK — the
        // isolated evaluation of colorant 0 (Spot1) holds b=0, so C ramps 0 -> 1 with the swept input.
        PdfStream wholeSpaceTint = Type4("{ 0 0 }", [0, 1, 0, 1], [0, 1, 0, 1, 0, 1, 0, 1]);
        var throwingSeparation = new PdfArray(
            new PdfName("Separation"), new PdfName("Spot1"), new PdfName("DeviceCMYK"),
            MismatchedDomainSampledFunction());
        var colorants = new PdfDictionary();
        colorants.Add(new PdfName("Spot1"), throwingSeparation);
        var attrs = new PdfDictionary();
        attrs.Add(new PdfName("Subtype"), new PdfName("NChannel"));
        attrs.Add(new PdfName("Colorants"), colorants);
        var names = new PdfArray(new PdfName("Spot1"), new PdfName("Other"));
        var space = new PdfArray(new PdfName("DeviceN"), names, new PdfName("DeviceCMYK"), wholeSpaceTint, attrs);

        (double[][]? ramp, _) = ColorSpaceResolver.BuildTintRamp(space, null, 0, 2);

        Assert.NotNull(ramp);
        Assert.Equal(1.0, ramp![255][0], 3);   // isolated evaluation: Spot1 swept to 1, Other held at 0
    }

    // --- Row: NChannel, entry is a CORRUPT indirect reference -> isolated evaluation, no throw (Axis B) ---

    [Fact]
    public void CorruptColorantsEntryReference_FallsBackToTheIsolatedEvaluation_RatherThanThrowing()
    {
        // GetPageColorants must never throw (see BuildTintRamp's own catch comment); a corrupt entry is
        // a fallback, not a failure. Genuinely corrupt target — a lone ']' body under an in-use xref
        // entry — because a merely non-existent object returns null without throwing, which would make
        // this test vacuous.
        (PdfArray space, PdfDocument doc) = ParseWithDoc(
            "[/DeviceN [/Spot1 /Cyan] /DeviceCMYK " + WholeSpaceAlways09
            + " << /Subtype /NChannel /Colorants << /Spot1 5 0 R >> >>]", "]");
        using (doc)
        {
            (double[][]? ramp, _) = ColorSpaceResolver.BuildTintRamp(space, doc, 0, 2);

            Assert.NotNull(ramp);
            Assert.Equal(0.9, ramp![255][0], 3);
        }
    }

    // --- Table 71: "shall be ignored if the colorant is also present in the process dictionary" ---
    // Not a row in the plan's degenerate table, but a real correctness gap the plan's own Step-3 snippet
    // left open: nothing in that snippet checks role, so a reserved name with its own /Colorants entry
    // would otherwise wrongly take that entry instead of falling back. See the task report for details.

    [Fact]
    public void ProcessComponent_StillUsesTheIsolatedEvaluation()
    {
        // Cyan is reserved, so it is a process colorant regardless of whether a /Process dictionary
        // exists at all (Table 71: reserved names "need not have entries in the process dictionary").
        PdfArray space = Parse(
            "[/DeviceN [/Spot1 /Cyan] /DeviceCMYK " + WholeSpaceAlways09
            + " << /Subtype /NChannel /Colorants << /Cyan [/Separation /Cyan /DeviceCMYK "
            + "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0.5 0 0 0] /N 1 >>] >> >>]");

        (double[][]? ramp, _) = ColorSpaceResolver.BuildTintRamp(space, null, 1, 2);

        Assert.NotNull(ramp);
        Assert.Equal(0.9, ramp![255][0], 3);
    }

    [Fact]
    public void NonReservedNameListedInProcessComponents_StillUsesTheIsolatedEvaluation()
    {
        // The other half of the Table 71 rule: a non-reserved name that IS listed in /Process /Components
        // is a process colorant too, and its /Colorants definition must likewise be ignored.
        PdfArray space = Parse(
            "[/DeviceN [/PlateX /Cyan] /DeviceCMYK " + WholeSpaceAlways09
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK /Components [/PlateX] >> "
            + "/Colorants << /PlateX [/Separation /PlateX /DeviceCMYK "
            + "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0.5 0 0 0] /N 1 >>] >> >>]");

        (double[][]? ramp, _) = ColorSpaceResolver.BuildTintRamp(space, null, 0, 2);

        Assert.NotNull(ramp);
        Assert.Equal(0.9, ramp![255][0], 3);
    }
}
