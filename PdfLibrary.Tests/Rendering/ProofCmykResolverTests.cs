using ICCSharp;
using ICCSharp.Profile;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering.Icc;

namespace PdfLibrary.Tests.Rendering;

public class ProofCmykResolverTests
{
    private static PdfName N(string s) => new(s);

    private static PdfStream MakeIccStream(byte[] bytes, int n) =>
        new(new PdfDictionary { [N("N")] = new PdfInteger(n) }, bytes);

    [Fact]
    public void Null_document_falls_back_to_provider_profile_and_has_target()
    {
        var r = new ProofCmykResolver(null);
        Assert.True(r.HasTarget);
    }

    [Fact]
    public void Icc_srgb_source_matches_two_profile_oracle()
    {
        var r = new ProofCmykResolver(null);
        PdfStream srgbStream = MakeIccStream(BuiltInProfiles.Srgb.Bytes.ToArray(), n: 3);
        double[]? got = r.TryIccToProofCmyk(srgbStream, new[] { 1.0, 0.0, 0.0 });
        Assert.NotNull(got);
        double[] oracle = IccTransform.Create(BuiltInProfiles.Srgb,
            CmykProfileProvider.Default.GetProfile(),
            new TransformOptions { Intent = RenderingIntent.RelativeColorimetric }).Apply(1.0, 0.0, 0.0);
        for (var i = 0; i < 4; i++) Assert.True(Math.Abs(got![i] - oracle[i]) < 1e-9);
    }

    [Fact]
    public void Gray_source_returns_null()   // N=1 exclusion is the resolver's job, single choke point
    {
        var r = new ProofCmykResolver(null);
        // any 1-component call must refuse regardless of stream contents
        PdfStream srgbStream = MakeIccStream(BuiltInProfiles.Srgb.Bytes.ToArray(), n: 1);
        Assert.Null(r.TryIccToProofCmyk(srgbStream, new[] { 0.5 }));
    }

    [Fact]
    public void Lab_white_is_near_zero_ink()
    {
        var r = new ProofCmykResolver(null);
        double[]? got = r.TryLabToProofCmyk(100, 0, 0);
        Assert.NotNull(got);
        for (var i = 0; i < 4; i++) Assert.True(got![i] < 0.08);
    }

    [Fact]
    public void Image_bulk_matches_scalar()
    {
        var r = new ProofCmykResolver(null);
        PdfStream srgbStream = MakeIccStream(BuiltInProfiles.Srgb.Bytes.ToArray(), n: 3);
        byte[] samples = { 255, 0, 0, 0, 255, 0 };   // 2 px interleaved RGB
        byte[]? plane = r.TryIccImageToProofCmyk(srgbStream, samples, 3, 2);
        Assert.NotNull(plane);
        Assert.Equal(8, plane!.Length);
        double[]? red = r.TryIccToProofCmyk(srgbStream, new[] { 1.0, 0.0, 0.0 });
        for (var i = 0; i < 4; i++) Assert.True(Math.Abs(plane[i] / 255.0 - red![i]) < 0.01);
    }

    [Fact]
    public void Bad_profile_bytes_return_null_not_throw()
    {
        var r = new ProofCmykResolver(null);
        Assert.Null(r.TryIccToProofCmyk(MakeIccStream(new byte[] { 1, 2, 3 }, n: 3), new[] { 0.1, 0.2, 0.3 }));
    }
}
