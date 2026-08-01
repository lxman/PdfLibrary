using ICCSharp;
using ICCSharp.Profile;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering.Icc;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

public class ProofCmykResolverTests
{
    private static PdfName N(string s) => new(s);

    private static PdfStream MakeIccStream(byte[] bytes, int n) =>
        new(new PdfDictionary { [N("N")] = new PdfInteger(n) }, bytes);

    // Windows-bundled CMYK profile whose A2B0 (Perceptual) and A2B1 (RelativeColorimetric) tables
    // are genuinely distinct LUTs (unlike the bundled default SWOP_TR003_coated_3.icc, whose ICC tag
    // table points A2B0/A2B1/A2B2 and B2A0/B2A1/B2A2 at the *same* byte offsets — verified both by
    // reading its tag table directly and against the lcms2 oracle (tools/lcms_reference.py-equivalent
    // driver): Perceptual and RelativeColorimetric are bit-identical for every probe colour tried,
    // including the saturated Lab(50,60,-40) the brief's escape hatch suggested. That default profile
    // cannot demonstrate per-intent wiring at all, so these differs-from-default tests build a document
    // with an /OutputIntents CMYK profile that does differentiate, instead of the brief's literal
    // `new ProofCmykResolver(null)`. Guarded like ICCSharp.Tests.IccTransformTests' SrgbPath: skip
    // (return) rather than fail when the profile isn't present on the machine running the test.
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

    [Fact]
    public void Lab_perceptual_intent_differs_from_default_relative()
    {
        if (!File.Exists(RswopIccPath)) return;
        var r = new ProofCmykResolver(DocWithCmykOutputIntent(File.ReadAllBytes(RswopIccPath)));
        // Lab(30,70,20): verified against the lcms2 oracle to show a real (~0.05 K) per-intent
        // difference for RSWOP.icm's genuinely-distinct A2B0/A2B1 tables (see class-level comment).
        double[]? rel = r.TryLabToProofCmyk(30, 70, 20);
        double[]? per = r.TryLabToProofCmyk(30, 70, 20, "Perceptual");
        Assert.NotNull(rel);
        Assert.NotNull(per);
        double maxDelta = rel!.Zip(per!, (a, b) => Math.Abs(a - b)).Max();
        Assert.True(maxDelta > 0.005, $"expected per-intent difference, max channel delta was {maxDelta}");
    }

    [Fact]
    public void Icc_perceptual_intent_differs_from_default_relative()
    {
        if (!File.Exists(RswopIccPath)) return;
        var r = new ProofCmykResolver(DocWithCmykOutputIntent(File.ReadAllBytes(RswopIccPath)));
        PdfStream srgb = MakeIccStream(BuiltInProfiles.Srgb.Bytes.ToArray(), n: 3);
        // sRGB blue (0,0,1): verified against the lcms2 oracle to show a real per-intent difference
        // through RSWOP.icm.
        double[]? rel = r.TryIccToProofCmyk(srgb, new[] { 0.0, 0.0, 1.0 });
        double[]? per = r.TryIccToProofCmyk(srgb, new[] { 0.0, 0.0, 1.0 }, "Perceptual");
        Assert.NotNull(rel);
        Assert.NotNull(per);
        double maxDelta = rel!.Zip(per!, (a, b) => Math.Abs(a - b)).Max();
        Assert.True(maxDelta > 0.005, $"expected per-intent difference, max channel delta was {maxDelta}");
    }

    [Fact]
    public void Per_intent_caches_do_not_collide()
    {
        if (!File.Exists(RswopIccPath)) return;
        var r = new ProofCmykResolver(DocWithCmykOutputIntent(File.ReadAllBytes(RswopIccPath)));
        PdfStream srgb = MakeIccStream(BuiltInProfiles.Srgb.Bytes.ToArray(), n: 3);
        double[]? per1 = r.TryIccToProofCmyk(srgb, new[] { 0.0, 0.0, 1.0 }, "Perceptual");
        double[]? rel = r.TryIccToProofCmyk(srgb, new[] { 0.0, 0.0, 1.0 });
        double[]? per2 = r.TryIccToProofCmyk(srgb, new[] { 0.0, 0.0, 1.0 }, "Perceptual");
        Assert.Equal(per1, per2);            // same intent → same cached transform, bit-identical
        Assert.NotEqual(per1, rel);          // different intent → different transform
    }

    [Fact]
    public void Unknown_intent_matches_default()
    {
        var r = new ProofCmykResolver(null);
        Assert.Equal(r.TryLabToProofCmyk(60, 10, -10), r.TryLabToProofCmyk(60, 10, -10, "Bogus"));
    }
}
