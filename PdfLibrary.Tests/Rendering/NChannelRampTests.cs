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
        // Review finding (Important 1): asserting only "differs from the whole-space solid" cannot see
        // the exact failure this test exists to prevent — a swatch silently going black (solid staying
        // at its (0,0,0) default) while the ramp is correct, since (0,0,0) also differs from the
        // whole-space's 0.9-cyan solid and would keep this test green.
        //
        // Instead pin the EXACT RGB the /Colorants Separation's C1 = [0.5 0 0 0] converts to at tint 1,
        // via the SAME conversion (PdfColorToRgb.ToRgb) BuildTintToRgb itself calls — derived from the
        // fixture's own numbers rather than a hand-typed value copied from a debugger run.
        PdfArray space = Parse(
            "[/DeviceN [/Spot1 /Cyan] /DeviceCMYK " + WholeSpaceAlways09
            + " << /Subtype /NChannel " + SpotOwnSeparation + " >>]");

        (_, (byte R, byte G, byte B) solid) = ColorSpaceResolver.BuildTintRamp(space, null, 0, 2);

        (byte R, byte G, byte B) expected = PdfColorToRgb.ToRgb([0.5, 0.0, 0.0, 0.0], "DeviceCMYK");
        Assert.Equal(expected, solid);
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

    // --- Important 2: the new path must not change the ramp's colour space / length out from under
    // the AlternateSpace label PageColorant ships alongside it. ---

    private const string WholeSpaceLabTint =
        "<< /FunctionType 2 /Domain [0 1 0 1] /C0 [50 20 -20] /C1 [50 20 -20] /N 1 "
        + "/Range [0 100 -100 100 -100 100] >>";

    [Fact]
    public void NChannelWithALabAlternate_KeepsTheIsolatedEvaluation_EvenWithAUsableColorantsEntry()
    {
        // PageColorant.TintRamp is documented as "tint 0..1 -> alternate-space colour" for the SPACE's
        // OWN alternate (Lab, 3 components here), and PageColorant.AlternateSpace still reports "Lab".
        // OwnColorantRamp always emits 4-component DeviceCMYK from the /Colorants entry regardless of
        // the entry's own alternate -- taking the entry's ramp here would silently change BOTH the
        // colour space and the component count while the label still says Lab. A Lab-alternate space
        // must therefore keep the isolated (whole-space) evaluation even when /Colorants has a usable
        // CMYK entry for this component.
        PdfArray space = Parse(
            "[/DeviceN [/Spot1 /Cyan] /Lab " + WholeSpaceLabTint
            + " << /Subtype /NChannel " + SpotOwnSeparation + " >>]");

        (double[][]? ramp, _) = ColorSpaceResolver.BuildTintRamp(space, null, 0, 2);

        Assert.NotNull(ramp);
        // Isolated evaluation: the whole-space Lab tint transform's raw 3-component output (L, a, b),
        // NOT the /Colorants entry's 4-component CMYK [c, m, y, k].
        Assert.Equal(3, ramp![255].Length);
        Assert.Equal(50.0, ramp[255][0], 3);
        Assert.Equal(20.0, ramp[255][1], 3);
        Assert.Equal(-20.0, ramp[255][2], 3);
    }

    // --- Minor 1 (whole-branch review): DeviceGray own-alternate must NOT take the /Colorants ramp ---

    private const string WholeSpaceGrayAlways09 =
        "<< /FunctionType 2 /Domain [0 1 0 1] /C0 [0.9] /C1 [0.9] /N 1 /Range [0 1] >>";

    [Fact]
    public void NChannelWithADeviceGrayAlternate_KeepsTheIsolatedEvaluation_EvenWithAUsableColorantsEntry()
    {
        // OwnColorantRamp always emits 4-component DeviceCMYK from the /Colorants entry. Before this
        // fix the gate at ColorSpaceResolver.cs:609 admitted DeviceGray alongside DeviceCMYK, but
        // PageColorant.AlternateSpace would still report "DeviceGray" (1 component) while the ramp
        // silently became 4-component CMYK -- exactly the hazard the Lab exclusion above protects
        // against, just for Gray instead of Lab. The gate is narrowed to DeviceCMYK only; a
        // DeviceGray-alternate space must keep the isolated (whole-space) evaluation even when
        // /Colorants has a usable CMYK entry for this component.
        PdfArray space = Parse(
            "[/DeviceN [/Spot1 /Cyan] /DeviceGray " + WholeSpaceGrayAlways09
            + " << /Subtype /NChannel " + SpotOwnSeparation + " >>]");

        (double[][]? ramp, _) = ColorSpaceResolver.BuildTintRamp(space, null, 0, 2);

        Assert.NotNull(ramp);
        // Isolated evaluation: the whole-space Gray tint transform's raw 1-component output,
        // NOT the /Colorants entry's 4-component CMYK [c, m, y, k] (C1 = [0.5 0 0 0]).
        Assert.Single(ramp![255]);
        Assert.Equal(0.9, ramp[255][0], 3);
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

    // --- Minor 2: entry is a multi-colorant DeviceN (arity != 1) -> isolated evaluation ---

    // A constant-0.3-cyan DeviceN function for the /Colorants entry — deliberately a DIFFERENT constant
    // from WholeSpaceAlways09's 0.9, so that wrongly accepting this multi-input entry (evaluating its
    // 2-input transform on the 1-element array OwnColorantRamp supplies) is DISTINGUISHABLE from the
    // correct isolated-evaluation fallback. Reusing WholeSpaceAlways09 itself here would make the two
    // outcomes indistinguishable (both constant 0.9) and the test vacuous regardless of which path ran.
    private const string EntryConstant03 =
        "<< /FunctionType 2 /Domain [0 1] /C0 [0.3 0 0 0] /C1 [0.3 0 0 0] /N 1 >>";

    [Fact]
    public void ColorantsEntryThatIsMultiInput_FallsBackToTheIsolatedEvaluation()
    {
        // Table 71 requires a /Colorants value to be a full Separation: exactly one input. Without the
        // `inputs != 1` check, BuildTintToCmyk accepts DeviceN too (inputComponents = Names.Count = 2
        // here) and its delegate would evaluate a 2-input-declared tint transform on the 1-element array
        // OwnColorantRamp supplies -- which may not throw, silently yielding a WRONG ramp rather than
        // falling back. OwnAlternateFor documents this exact hazard for the sibling per-operator path.
        PdfArray space = Parse(
            "[/DeviceN [/Spot1 /Cyan] /DeviceCMYK " + WholeSpaceAlways09
            + " << /Subtype /NChannel /Colorants << /Spot1 [/DeviceN [/Spot1 /Spot2] /DeviceCMYK "
            + EntryConstant03 + "] >> >>]");

        (double[][]? ramp, _) = ColorSpaceResolver.BuildTintRamp(space, null, 0, 2);

        Assert.NotNull(ramp);
        Assert.Equal(0.9, ramp![255][0], 3);    // isolated evaluation (whole-space), NOT the entry's 0.3
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

    // --- M-5 (whole-branch review): /Process /Components is a CORRUPT indirect reference, exercised
    // through IsProcessColorant -> isolated evaluation, no throw (Axis B, at IsProcessColorant's own
    // level, not merely at OwnColorantRamp's) ---

    [Fact]
    public void CorruptProcessComponentsReference_FallsBackToTheIsolatedEvaluation_RatherThanThrowing()
    {
        // IsProcessColorant dereferences /Process /Components (ColorSpaceResolver.cs, inside
        // IsProcessColorant) with no try/catch of its own -- by design, documented as relying entirely on
        // OwnColorantRamp's enclosing try covering "the whole body and not merely the ramp evaluation".
        // This pins that reliance at the level it actually matters: if OwnColorantRamp's try were ever
        // narrowed to cover only the ramp-evaluation loop (a shape this program has hit before — see the
        // sibling CorruptColorantsEntryReference test above for the /Colorants-side version of the same
        // guard), a corrupt /Process /Components reference would throw out of GetPageColorants instead of
        // falling back. Genuinely corrupt target — a lone ']' body under an in-use xref entry — because a
        // merely non-existent object returns null without throwing, which would make this test vacuous.
        (PdfArray space, PdfDocument doc) = ParseWithDoc(
            "[/DeviceN [/PlateX /Cyan] /DeviceCMYK " + WholeSpaceAlways09
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK /Components 5 0 R >> "
            + "/Colorants << /PlateX [/Separation /PlateX /DeviceCMYK "
            + "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0.5 0 0 0] /N 1 >>] >> >>]", "]");
        using (doc)
        {
            (double[][]? ramp, _) = ColorSpaceResolver.BuildTintRamp(space, doc, 0, 2);

            Assert.NotNull(ramp);
            // Isolated (whole-space) evaluation, NOT the /PlateX /Colorants entry (which would ramp to 0.5).
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

    // --- Pass 2b-engine Task 1: ColorantOrigin.ProcessChannelCount ---
    //
    // Deliberately NOT re-testing ProcessChannelCount()'s own table: Pass 2a-prime Task 1 already pins
    // every row of it (/DeviceCMYK, /DeviceGray, absent, unreadable, ICCBased /N 4 / /N 1 / other,
    // unresolvable stream, Count < 2, every other family). These pin the NEW thing — that the value is
    // surfaced faithfully and paired with Components. The ICCBased row is the one exception, included
    // because it is the only path where the count comes from neither a default nor a literal name.

    [Fact]
    public void NChannelOverDeviceCmyk_CarriesAProcessChannelCountOfFour()
    {
        PdfArray space = Parse(
            "[/DeviceN [/Cyan /Spot1] /DeviceCMYK " + WholeSpaceAlways09
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK /Components [/Cyan] >> >>]");

        ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(space, [0.5, 0.5], null);

        Assert.NotNull(origin);
        Assert.NotNull(origin!.Components);
        Assert.Equal(4, origin.ProcessChannelCount);
    }

    // THE LOAD-BEARING TEST. Ink1 is LISTED, so ProcessChannelFor answers from processChannels and returns
    // index 0 (0 < 1, so it is in range) — the same value a /Cyan gets under a four-channel space. Only the
    // COUNT tells the two apart, which is the entire reason this property exists. Task 2 and the compositor
    // both refuse to place a component when this is not 4; if this test can pass with the count reported as
    // 4, both refusals are unpinned and a gray colorant lands on the cyan plate.
    [Fact]
    public void ListedNameUnderAOneChannelProcessSpace_GetsChannelZeroButACountOfOne()
    {
        PdfArray space = Parse(
            "[/DeviceN [/Ink1] /DeviceCMYK " + WholeSpaceAlways09
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceGray /Components [/Ink1] >> >>]");

        ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(space, [0.5], null);

        ColourantComponent ink1 = Assert.Single(origin!.Components!);
        Assert.Equal(ColourantRole.Process, ink1.Role);
        Assert.Equal(0, ink1.ProcessChannel);        // indistinguishable from cyan on its own …
        Assert.Equal(1, origin.ProcessChannelCount); // … until the count says otherwise
    }

    [Fact]
    public void NChannelWithNoProcessDictionary_CarriesTheNoConstraintCountOfFour()
    {
        PdfArray space = Parse(
            "[/DeviceN [/Cyan /Spot1] /DeviceCMYK " + WholeSpaceAlways09
            + " << /Subtype /NChannel >>]");

        ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(space, [0.5, 0.5], null);

        Assert.NotNull(origin!.Components);
        Assert.Equal(4, origin.ProcessChannelCount);
    }

    // The ICCBased row: the count comes from the profile stream's /N, which must be an INDIRECT object, so
    // this is the one case needing ParseWithDoc. /N 1 (not 4) so the assertion cannot pass by defaulting.
    [Fact]
    public void NChannelOverAnIccBasedGrayProcessSpace_CarriesACountOfOne()
    {
        (PdfArray space, PdfDocument doc) = ParseWithDoc(
            "[/DeviceN [/Ink1] /DeviceCMYK " + WholeSpaceAlways09
            + " << /Subtype /NChannel /Process << /ColorSpace [/ICCBased 5 0 R] /Components [/Ink1] >> >>]",
            "<< /N 1 /Length 0 >> stream\nendstream");
        using (doc)
        {
            ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(space, [0.5], doc);

            Assert.NotNull(origin!.Components);
            Assert.Equal(1, origin.ProcessChannelCount);
        }
    }

    // A suppressed component list must carry a suppressed count too: the two are one answer, and a consumer
    // that saw ProcessChannelCount == 4 alongside Components == null would be told the space is
    // four-channel-shaped when the engine has in fact declined to describe it at all.
    [Fact]
    public void NChannelOverAnUnsupportedProcessSpace_SuppressesBothComponentsAndTheCount()
    {
        PdfArray space = Parse(
            "[/DeviceN [/Cyan /Spot1] /DeviceCMYK " + WholeSpaceAlways09
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceRGB /Components [/Cyan] >> >>]");

        ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(space, [0.5, 0.5], null);

        Assert.NotNull(origin);
        Assert.Null(origin!.Components);
        Assert.Null(origin.ProcessChannelCount);
    }

    // A plain DeviceN has no per-component answer at all, so it has no count either.
    [Fact]
    public void PlainDeviceN_CarriesNoProcessChannelCount()
    {
        PdfArray space = Parse("[/DeviceN [/Cyan /Spot1] /DeviceCMYK " + WholeSpaceAlways09 + "]");

        ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(space, [0.5, 0.5], null);

        Assert.NotNull(origin);
        Assert.Null(origin!.Components);
        Assert.Null(origin.ProcessChannelCount);
    }
}
