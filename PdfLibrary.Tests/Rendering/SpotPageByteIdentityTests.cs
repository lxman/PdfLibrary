using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Soft-Proof SP-1 Task 7: non-regression / byte-identity guard. Tasks 1-6 added
/// <see cref="ColorantOrigin"/> to <see cref="PdfLibrary.Content.PdfGraphicsState"/> (fills/strokes) and
/// to <see cref="ShadingDescriptor"/>, plus <see cref="PdfDocument.GetPageColorants"/> — all additive,
/// none of it read by the render path. This locks in the claim for the fill/stroke path: a genuine
/// <c>/Separation</c> fill, driven through the production <see cref="RecordingRenderTarget"/> (the exact
/// recorder <c>PdfPage.RenderTo()</c>/SkiaSharp use), records the identical resolved colour that an
/// equivalent literal-DeviceCMYK control page records — whether or not
/// <see cref="PdfLibrary.Content.PdfGraphicsState.ResolvedFillColorantOrigin"/> is populated alongside it.
///
/// No golden-bitmap harness exists in this project for byte-exact rasterised pixels: the only
/// render-to-bitmap coverage (<c>SkiaSharpRenderPipelineTests</c>) deliberately avoids byte-exact pixel
/// comparison, and CI runs that pixel suite on ubuntu-latest only while this repo is developed on macOS —
/// a pixel hash captured on one platform/SkiaSharp build is not guaranteed to reproduce on the other.
/// Per the task brief's permitted substitute, this test instead asserts the strongest available
/// deterministic invariant at the exact point SP-1 touches: the recorded <see cref="FillCommand"/>'s
/// resolved colour space, resolved colour components, and path geometry are identical between the
/// Separation page and its DeviceCMYK-equivalent control — i.e. ColorantOrigin's presence altered no
/// recorded colour or geometry.
/// </summary>
public class SpotPageByteIdentityTests
{
    // "Orange" at tint=1.0 resolves via the builder's Type-2 tint transform
    // (C0=[0,0,0,0], C1=[0,0.5,1,0], N=1 — see PdfDocumentWriter.GetCmykForColorant) to exactly
    // [0, 0.5, 1.0, 0] CMYK. Tint=1.0 sidesteps interpolation rounding, so a literal DeviceCMYK control
    // built from the same four constants matches bit-for-bit, not just within tolerance.
    private const double ExpectedC = 0.0, ExpectedM = 0.5, ExpectedY = 1.0, ExpectedK = 0.0;

    private static FillCommand RecordSingleFill(byte[] pdfBytes)
    {
        using var ms = new MemoryStream(pdfBytes);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0) ?? throw new InvalidOperationException("page 0 missing");

        PageDrawList list = RecordingRenderTarget.Record(page, scale: 1.0);
        return Assert.Single(list.Commands.OfType<FillCommand>());
    }

    [Fact]
    public void SeparationFill_RecordsSameResolvedColourAndGeometryAs_DeviceCmykEquivalentControl()
    {
        byte[] spotPdf = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddRectangle(100, 600, 200, 100, fillColor: PdfColor.FromSeparation("Orange", 1.0)))
            .ToByteArray();
        byte[] controlPdf = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddRectangle(100, 600, 200, 100,
                fillColor: PdfColor.FromCmyk(ExpectedC, ExpectedM, ExpectedY, ExpectedK)))
            .ToByteArray();

        FillCommand spotFill = RecordSingleFill(spotPdf);
        FillCommand controlFill = RecordSingleFill(controlPdf);

        // SP-1's new field IS populated for the spot page (proves the Separation path was actually
        // exercised, not silently skipped) ...
        Assert.NotNull(spotFill.State.ResolvedFillColorantOrigin);
        Assert.Equal(["Orange"], spotFill.State.ResolvedFillColorantOrigin!.Names);
        Assert.Equal("DeviceCMYK", spotFill.State.ResolvedFillColorantOrigin!.AlternateSpace);
        // ... and null for the literal-DeviceCMYK control (no colorant to name) — the two pages differ
        // ONLY in whether ColorantOrigin is present, which is exactly the variable this test isolates.
        Assert.Null(controlFill.State.ResolvedFillColorantOrigin);

        // The actual painted colour and geometry — everything a consumer (SkiaSharp, the CMYK
        // compositor) reads off the command — are byte-identical either way. This is the non-regression
        // proof: adding ColorantOrigin changed no rendered output.
        Assert.Equal(controlFill.State.ResolvedFillColorSpace, spotFill.State.ResolvedFillColorSpace);
        Assert.Equal(controlFill.State.ResolvedFillColor, spotFill.State.ResolvedFillColor);
        Assert.Equal(controlFill.EvenOdd, spotFill.EvenOdd);
        Assert.Equal(controlFill.Segments, spotFill.Segments);
    }
}
