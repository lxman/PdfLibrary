using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Pins the arity rule <see cref="ColorSpaceResolver.OriginForColorSpaceObject"/> alone (among the
/// five migrated members) enforces — Count >= 4, expressed post-migration as
/// <c>SpotColorSpace.HasTintTransform</c> (and, since the eager-deref fix, as
/// <c>SpotColorSpace.TryParse</c>'s <c>minimumElements: 4</c>) — plus the cases that exercise a LIVE
/// <see cref="PdfDocument"/> rather than the null-document short-circuit every prior
/// characterization test used. Task 4 review found that gating on <c>TintTransformObject is null</c>
/// would deref a possibly-corrupt indirect tint transform object before the name checks even ran.
/// These tests do NOT, by themselves, distinguish that gate from the correct one: <c>Deref</c> never
/// returns null, so <c>TintTransformObject is null</c> and the arity check are exactly equivalent in
/// VALUE and differ only in side effect, which no assertion here can observe. What actually prevents a
/// regression back to that shape is code review and the parser's own documentation (see
/// <c>SpotColorSpace.TryParse</c>'s <c>minimumElements</c> parameter doc) — these tests pin the
/// user-visible arity behaviour, not the internal gating mechanism.
/// </summary>
public class OriginForColorSpaceObjectTests
{
    private const string Tint2 = "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0.5 0 1 0] /N 1 >>";

    private static PdfArray Parse(string pdfArrayLiteral)
    {
        byte[] pdf = ColourConformancePage.Build(pdfArrayLiteral, "1 0 0 rg 0 0 1 1 re f");
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        PdfDictionary colorSpaces = page.GetResources()!.GetColorSpaces()!;
        return (PdfArray)colorSpaces[new PdfName("Cs0")]!;
    }

    /// <summary>
    /// Like <see cref="Parse"/> but keeps the document alive and returns it too, so
    /// <see cref="ColorSpaceResolver.Deref"/> actually resolves an indirect reference instead of
    /// short-circuiting on a null document. Mirrors
    /// <c>SpotColorSpaceTests.ParseWithDoc</c> / <c>ColorSpaceResolverCharacterizationTests.ParseWithResources</c>.
    /// </summary>
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

    [Fact]
    public void TwoElementSeparation_HasNoTintTransform_ReturnsNull()
    {
        // The arity rule this member alone enforces, previously unasserted anywhere: a
        // [/Separation name] array (no alternate, no tint transform) must yield no origin.
        PdfArray cs = Parse("[/Separation /Spot1]");

        Assert.Null(ColorSpaceResolver.OriginForColorSpaceObject(cs, [1.0], null));
    }

    [Fact]
    public void ThreeElementSeparation_StillHasNoTintTransform_ReturnsNull()
    {
        // The boundary: an alternate is present but the tint transform still is not (Count == 3).
        // HasTintTransform requires Count >= 4, so this must still yield no origin.
        PdfArray cs = Parse("[/Separation /Spot1 /DeviceCMYK]");

        Assert.Null(ColorSpaceResolver.OriginForColorSpaceObject(cs, [1.0], null));
    }

    [Fact]
    public void FourElementSeparation_TintTransformIsIndirectReference_ResolvesThroughLiveDocument()
    {
        // The case the review found could throw: the tint transform (element 3) is an indirect
        // reference, the common real-world shape. Resolved through a LIVE PdfDocument (not null),
        // so Deref actually dereferences object 5 rather than short-circuiting.
        (PdfArray cs, PdfDocument doc) =
            ParseWithDoc("[/Separation /Spot1 /DeviceCMYK 5 0 R]", Tint2);
        using (doc)
        {
            ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(cs, [0.75], doc);

            Assert.NotNull(origin);
            Assert.Equal(["Spot1"], origin!.Names);
            Assert.Equal([0.75], origin.Tints);
            Assert.Equal("DeviceCMYK", origin.AlternateSpace);
        }
    }

    [Fact]
    public void UnrecognisedAlternate_YieldsEmptyAlternateSpace()
    {
        // Element 2 is a bare number: neither a PdfName nor a PdfArray-with-leading-name, so
        // AlternateSpaceName falls to the empty-string default. The tint transform (element 3) is
        // still present and valid, so the origin itself must still be produced.
        PdfArray cs = Parse("[/Separation /Spot1 42 " + Tint2 + "]");

        ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(cs, [1.0], null);

        Assert.NotNull(origin);
        Assert.Equal(string.Empty, origin!.AlternateSpace);
    }

    [Fact]
    public void EmptyDeviceNNamesArray_ReturnsNull()
    {
        // AllNamesResolved is vacuously true for an empty Names list (see SpotColorSpace's doc
        // comment), so the separate Names.Count == 0 guard is what rejects this — not
        // AllNamesResolved. Pins that guard directly against this member.
        PdfArray cs = Parse("[/DeviceN [] /DeviceCMYK " + Tint2 + "]");

        Assert.Null(ColorSpaceResolver.OriginForColorSpaceObject(cs, [1.0], null));
    }
}
