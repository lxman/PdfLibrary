using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ICCSharp.Profile;
using PdfLibrary.Content;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Task 4 of the B-2 ICC/CMS phase: ICCBased (N&gt;=3) and Lab fills/strokes carry a proof-target
/// CMYK alongside the sRGB flatten, threaded through <see cref="ColorSpaceResolver.ResolveColorSpace"/>
/// and stored on <see cref="PdfGraphicsState"/> for the CMYK compositor (Task 7).
/// </summary>
public class ProofCmykStateTests
{
    private static PdfName N(string s) => new(s);

    private static PdfStream MakeIccStream(byte[] bytes, int n) =>
        new(new PdfDictionary { [N("N")] = new PdfInteger(n) }, bytes);

    // Task 4: state-level (ResolveColorSpace) proof of the ri thread. Same escape hatch as
    // ProofCmykResolverTests — the bundled default CMYK profile's A2B tables are byte-identical across
    // intents, so a per-intent DIFFERENCE can only be demonstrated through a profile whose tables
    // actually diverge. Reuses ProofCmykResolverTests' exact /OutputIntents fixture and skip-if-absent
    // guard rather than inventing a second one.
    private static readonly string RswopIccPath =
        @"C:\Windows\System32\spool\drivers\color\RSWOP.icm";

    private static PdfDocument DocWithCmykOutputIntent(byte[] destProfileBytes)
    {
        var doc = new PdfDocument();
        var intentDict = new PdfDictionary { [N("S")] = new PdfName("GTS_PDFA1") };
        doc.AddObject(2, 0, new PdfStream(new PdfDictionary(), destProfileBytes));
        intentDict[N("DestOutputProfile")] = new PdfIndirectReference(2, 0);
        var intents = new PdfArray { intentDict };
        var catalog = new PdfDictionary
        {
            [N("Type")] = new PdfName("Catalog"),
            [N("OutputIntents")] = intents,
        };
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[N("Root")] = new PdfIndirectReference(1, 0);
        return doc;
    }

    [Fact]
    public void Iccbased_fill_resolve_with_ri_differs_from_default_intent()
    {
        if (!File.Exists(RswopIccPath)) return;
        PdfDocument doc = DocWithCmykOutputIntent(File.ReadAllBytes(RswopIccPath));
        var resolver = new ColorSpaceResolver(doc);
        PdfStream srgbStream = MakeIccStream(BuiltInProfiles.Srgb.Bytes.ToArray(), n: 3);
        var spaces = new PdfDictionary
        {
            [N("CS1")] = new PdfArray(new PdfName("ICCBased"), srgbStream),
        };

        string? nameDefault = "CS1";
        List<double>? colorDefault = [0.0, 0.0, 1.0];
        resolver.ResolveColorSpace(ref nameDefault, ref colorDefault, spaces, false, null, out double[]? proofDefault);

        string? namePerceptual = "CS1";
        List<double>? colorPerceptual = [0.0, 0.0, 1.0];
        resolver.ResolveColorSpace(ref namePerceptual, ref colorPerceptual, spaces, false, "Perceptual", out double[]? proofPerceptual);

        Assert.NotNull(proofDefault);
        Assert.NotNull(proofPerceptual);
        double maxDelta = proofDefault!.Zip(proofPerceptual!, (a, b) => Math.Abs(a - b)).Max();
        Assert.True(maxDelta > 0.005, $"expected per-intent difference, max channel delta was {maxDelta}");
    }

    [Fact]
    public void Lab_fill_resolve_with_ri_differs_from_default_intent()
    {
        if (!File.Exists(RswopIccPath)) return;
        PdfDocument doc = DocWithCmykOutputIntent(File.ReadAllBytes(RswopIccPath));
        var resolver = new ColorSpaceResolver(doc);

        var labDict = new PdfDictionary
        {
            [N("WhitePoint")] = new PdfArray(new PdfReal(0.9642), new PdfReal(1.0), new PdfReal(0.8249)),
        };
        var spaces = new PdfDictionary
        {
            [N("CS1")] = new PdfArray(new PdfName("Lab"), labDict),
        };

        string? nameDefault = "CS1";
        List<double>? colorDefault = [30.0, 70.0, 20.0];
        resolver.ResolveColorSpace(ref nameDefault, ref colorDefault, spaces, false, null, out double[]? proofDefault);

        string? namePerceptual = "CS1";
        List<double>? colorPerceptual = [30.0, 70.0, 20.0];
        resolver.ResolveColorSpace(ref namePerceptual, ref colorPerceptual, spaces, false, "Perceptual", out double[]? proofPerceptual);

        Assert.NotNull(proofDefault);
        Assert.NotNull(proofPerceptual);
        double maxDelta = proofDefault!.Zip(proofPerceptual!, (a, b) => Math.Abs(a - b)).Max();
        Assert.True(maxDelta > 0.005, $"expected per-intent difference, max channel delta was {maxDelta}");
    }

    [Fact]
    public void Iccbased_fill_resolve_produces_proof_cmyk()
    {
        var resolver = new ColorSpaceResolver(document: null);
        string? name = "CS1";
        List<double>? color = [1.0, 0.0, 0.0];
        PdfStream srgbStream = MakeIccStream(BuiltInProfiles.Srgb.Bytes.ToArray(), n: 3);
        var spaces = new PdfDictionary
        {
            [N("CS1")] = new PdfArray(new PdfName("ICCBased"), srgbStream),
        };

        resolver.ResolveColorSpace(ref name, ref color, spaces, out double[]? proof);

        Assert.Equal("DeviceRGB", name); // sRGB flatten unchanged
        Assert.NotNull(proof);
        Assert.Equal(4, proof!.Length);
    }

    [Fact]
    public void Device_rgb_resolve_clears_proof_cmyk()
    {
        var resolver = new ColorSpaceResolver(document: null);
        string? name = "DeviceRGB";
        List<double>? color = [0.2, 0.4, 0.6];

        resolver.ResolveColorSpace(ref name, ref color, colorSpaces: null, out double[]? proof);

        Assert.Null(proof);
    }

    [Fact]
    public void Lab_fill_resolve_produces_proof_cmyk()
    {
        var resolver = new ColorSpaceResolver(document: null);
        string? name = "CS1";
        List<double>? color = [100.0, 0.0, 0.0];

        var labDict = new PdfDictionary
        {
            [N("WhitePoint")] = new PdfArray(new PdfReal(0.9642), new PdfReal(1.0), new PdfReal(0.8249)),
        };
        var spaces = new PdfDictionary
        {
            [N("CS1")] = new PdfArray(new PdfName("Lab"), labDict),
        };

        resolver.ResolveColorSpace(ref name, ref color, spaces, out double[]? proof);

        Assert.Equal("DeviceRGB", name);
        Assert.NotNull(proof);
        Assert.Equal(4, proof!.Length);
        for (var i = 0; i < 4; i++) Assert.True(proof[i] < 0.08, $"channel {i} = {proof[i]}");
    }

    // Defect A (B-2 Phase C Task 5 debug report, panel 22.1): the resolved proof CMYK (and resolved
    // device colour) for a fill is computed eagerly at the colour operator, keyed off
    // CurrentState.RenderingIntent *at that instant*. A later `ri`/ExtGState `/RI` only assigns
    // RenderingIntent without re-triggering resolution, so it silently no-ops when it follows the
    // colour operator in the content stream — exactly GWG 22.1's own operator order:
    //   /CS1 cs 60 0 0 sc     <-- Lab fill colour SET here (resolved under stale default intent)
    //   /Perceptual ri        <-- has zero effect on the fill below, pre-fix
    //   0 0 10 10 re f        <-- paints with whatever was resolved at the `sc` line above
    // Drives the real content-processing pipeline (PdfContentParser -> PdfRenderer.ProcessOperators)
    // rather than ColorSpaceResolver directly, since the bug is in operator-ordering/re-trigger timing,
    // not in resolution itself. Reuses this file's RSWOP OutputIntent fixture/skip-guard because the
    // bundled default CMYK profile's A2B tables are byte-identical across intents (documented above).
    private static PdfResources LabResources()
    {
        var labDict = new PdfDictionary
        {
            [N("WhitePoint")] = new PdfArray(new PdfReal(0.9642), new PdfReal(1.0), new PdfReal(0.8249)),
        };
        var colorSpaces = new PdfDictionary
        {
            [N("CS1")] = new PdfArray(new PdfName("Lab"), labDict),
        };
        var resourcesDict = new PdfDictionary { [N("ColorSpace")] = colorSpaces };
        return new PdfResources(resourcesDict);
    }

    [Fact]
    public void Ri_operator_after_colour_op_retroactively_reresolves_fill_proof_cmyk()
    {
        if (!File.Exists(RswopIccPath)) return;
        PdfDocument doc = DocWithCmykOutputIntent(File.ReadAllBytes(RswopIccPath));
        PdfResources resources = LabResources();

        // Independent oracle: what ColorSpaceResolver itself produces for this Lab colour under each
        // intent (same computation ProofCmykStateTests already trusts above).
        var oracle = new ColorSpaceResolver(doc);
        var spacesForOracle = new PdfDictionary { [N("CS1")] = new PdfArray(new PdfName("Lab"),
            new PdfDictionary { [N("WhitePoint")] = new PdfArray(new PdfReal(0.9642), new PdfReal(1.0), new PdfReal(0.8249)) }) };
        string? oracleNameDefault = "CS1";
        List<double>? oracleColorDefault = [60.0, 0.0, 0.0];
        oracle.ResolveColorSpace(ref oracleNameDefault, ref oracleColorDefault, spacesForOracle, false, null, out double[]? expectedDefault);
        string? oracleNamePerceptual = "CS1";
        List<double>? oracleColorPerceptual = [60.0, 0.0, 0.0];
        oracle.ResolveColorSpace(ref oracleNamePerceptual, ref oracleColorPerceptual, spacesForOracle, false, "Perceptual", out double[]? expectedPerceptual);
        Assert.NotNull(expectedDefault);
        Assert.NotNull(expectedPerceptual);

        var mock = new MockRenderTarget();
        var renderer = new PdfRenderer(mock, resources, document: doc);

        byte[] content = Encoding.ASCII.GetBytes("/CS1 cs 60 0 0 sc /Perceptual ri 0 0 10 10 re f");
        List<PdfOperator> ops = PdfContentParser.Parse(content);
        renderer.ProcessOperators(ops);

        Assert.NotNull(mock.LastFillState);
        double[]? actual = mock.LastFillState!.ResolvedFillProofCmyk;
        Assert.NotNull(actual);

        double maxDeltaFromPerceptual = actual!.Zip(expectedPerceptual!, (a, b) => Math.Abs(a - b)).Max();
        Assert.True(maxDeltaFromPerceptual < 0.005,
            $"expected the post-fix fill to resolve under the LATER /Perceptual ri, max delta from perceptual oracle was {maxDeltaFromPerceptual}");

        double maxDeltaFromDefault = actual.Zip(expectedDefault!, (a, b) => Math.Abs(a - b)).Max();
        Assert.True(maxDeltaFromDefault > 0.002,
            $"expected the resolved proof to differ from the stale default-intent value, max delta was {maxDeltaFromDefault}");
    }

    [Fact]
    public void ExtGState_RI_after_colour_op_retroactively_reresolves_fill_proof_cmyk()
    {
        if (!File.Exists(RswopIccPath)) return;
        PdfDocument doc = DocWithCmykOutputIntent(File.ReadAllBytes(RswopIccPath));

        var labDict = new PdfDictionary
        {
            [N("WhitePoint")] = new PdfArray(new PdfReal(0.9642), new PdfReal(1.0), new PdfReal(0.8249)),
        };
        var colorSpaces = new PdfDictionary
        {
            [N("CS1")] = new PdfArray(new PdfName("Lab"), labDict),
        };
        var extGState = new PdfDictionary { [N("RI")] = new PdfName("Perceptual") };
        var extGStates = new PdfDictionary { [N("GS0")] = extGState };
        var resourcesDict = new PdfDictionary
        {
            [N("ColorSpace")] = colorSpaces,
            [N("ExtGState")] = extGStates,
        };
        var resources = new PdfResources(resourcesDict);

        var oracle = new ColorSpaceResolver(doc);
        var spacesForOracle = new PdfDictionary { [N("CS1")] = new PdfArray(new PdfName("Lab"),
            new PdfDictionary { [N("WhitePoint")] = new PdfArray(new PdfReal(0.9642), new PdfReal(1.0), new PdfReal(0.8249)) }) };
        string? oracleNameDefault = "CS1";
        List<double>? oracleColorDefault = [60.0, 0.0, 0.0];
        oracle.ResolveColorSpace(ref oracleNameDefault, ref oracleColorDefault, spacesForOracle, false, null, out double[]? expectedDefault);
        string? oracleNamePerceptual = "CS1";
        List<double>? oracleColorPerceptual = [60.0, 0.0, 0.0];
        oracle.ResolveColorSpace(ref oracleNamePerceptual, ref oracleColorPerceptual, spacesForOracle, false, "Perceptual", out double[]? expectedPerceptual);
        Assert.NotNull(expectedDefault);
        Assert.NotNull(expectedPerceptual);

        var mock = new MockRenderTarget();
        var renderer = new PdfRenderer(mock, resources, document: doc);

        // /GS0 gs applies /RI /Perceptual AFTER the colour operator — mirrors the ri-operator scenario
        // above but through the ExtGState path (PdfRenderer.OnSetGraphicsState -> ExtGStateApplier).
        byte[] content = Encoding.ASCII.GetBytes("/CS1 cs 60 0 0 sc /GS0 gs 0 0 10 10 re f");
        List<PdfOperator> ops = PdfContentParser.Parse(content);
        renderer.ProcessOperators(ops);

        Assert.NotNull(mock.LastFillState);
        double[]? actual = mock.LastFillState!.ResolvedFillProofCmyk;
        Assert.NotNull(actual);

        double maxDeltaFromPerceptual = actual!.Zip(expectedPerceptual!, (a, b) => Math.Abs(a - b)).Max();
        Assert.True(maxDeltaFromPerceptual < 0.005,
            $"expected the post-fix fill to resolve under ExtGState /RI /Perceptual, max delta from perceptual oracle was {maxDeltaFromPerceptual}");

        double maxDeltaFromDefault = actual.Zip(expectedDefault!, (a, b) => Math.Abs(a - b)).Max();
        Assert.True(maxDeltaFromDefault > 0.002,
            $"expected the resolved proof to differ from the stale default-intent value, max delta was {maxDeltaFromDefault}");
    }

    [Fact]
    public void Clone_copies_proof_arrays_deeply()
    {
        var s = new PdfGraphicsState { ResolvedFillProofCmyk = [0.1, 0.2, 0.3, 0.4] };
        PdfGraphicsState c = s.Clone();

        Assert.NotSame(s.ResolvedFillProofCmyk, c.ResolvedFillProofCmyk);
        Assert.Equal(s.ResolvedFillProofCmyk, c.ResolvedFillProofCmyk);
    }
}
