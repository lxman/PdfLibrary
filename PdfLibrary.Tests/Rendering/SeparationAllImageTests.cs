using SkiaSharp;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// ISO 32000-2 §8.6.6.4 rows 4-7 and 4-10 for IMAGES. The fill path already ignores the tint transform
/// for <c>/All</c> and applies the additive complement; the image path did not, because
/// <c>BuildTintToRgb</c> evaluates the transform for every colourant name. An <c>/All</c> image
/// therefore painted a different colour from an identical <c>/All</c> fill — the inconsistency this
/// closes.
///
/// <para>
/// The space's transform ramps to RED at full tint. Evaluating it paints red; obeying the clause paints
/// the complement of tint 1, which is black. Red and black differ in every channel, so the assertion
/// cannot be satisfied by accident.
/// </para>
/// </summary>
public class SeparationAllImageTests
{
    [Fact]
    public void SeparationAll_Image_IgnoresTintTransformAndPaintsTheComplement()
    {
        // 2x2 8-bit single-component image, every sample at full tint (0xFF) → complement 0 → black.
        const string img = "<< /Type /XObject /Subtype /Image /Width 2 /Height 2 " +
                           "/ColorSpace [/Separation /All /DeviceRGB " +
                           "<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [1 0 0] /N 1 >>] " +
                           "/BitsPerComponent 8 /Length 4 >>\r\nstream\r\n" +
                           "ÿÿÿÿ\r\nendstream";
        const string content = "q 200 0 0 200 100 400 cm /Im0 Do Q";

        byte[] pdf = ColourConformancePage.Build("/DeviceRGB", content, withFont: false,
            extraResources: " /XObject << /Im0 5 0 R >>", extraObjects: img);

        SKColor c = ColourConformancePage.RenderCentre(pdf);

        Assert.True(c.Red < 20 && c.Green < 20 && c.Blue < 20,
            $"/All image at tint 1 painted RGB({c.Red},{c.Green},{c.Blue}); §8.6.6.4 requires the tint " +
            "transform to be ignored and the complement (black) applied to all colourants");
    }
}
