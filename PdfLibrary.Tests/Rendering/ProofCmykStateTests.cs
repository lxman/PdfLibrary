using System.Collections.Generic;
using System.Linq;
using ICCSharp.Profile;
using PdfLibrary.Content;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering;
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

    [Fact]
    public void Clone_copies_proof_arrays_deeply()
    {
        var s = new PdfGraphicsState { ResolvedFillProofCmyk = [0.1, 0.2, 0.3, 0.4] };
        PdfGraphicsState c = s.Clone();

        Assert.NotSame(s.ResolvedFillProofCmyk, c.ResolvedFillProofCmyk);
        Assert.Equal(s.ResolvedFillProofCmyk, c.ResolvedFillProofCmyk);
    }
}
