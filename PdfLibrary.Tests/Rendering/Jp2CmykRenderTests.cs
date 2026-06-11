using PdfLibrary.Rendering.SkiaSharp.Rendering;
using SkiaSharp;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Covers <see cref="ImageRenderer.ConvertRawBytesToSkBitmap"/> for the 4-component case — the JPEG
/// 2000 render path. A 4-component JP2 embedded in a PDF is CMYK, not RGBA (PDF carries alpha via
/// /SMask, never an inline 4th channel). The previous code copied the bytes straight into an RGBA
/// bitmap, which put the black (K) plate into the alpha channel and rendered the image as garbage.
/// </summary>
public class Jp2CmykRenderTests
{
    [Fact]
    public void Four_component_jp2_is_treated_as_cmyk_not_rgba()
    {
        // Two CMYK pixels: pure process cyan, then pure black (K=255).
        byte[] cmyk =
        [
            255, 0, 0, 0,   // C=255 → cyan
            0, 0, 0, 255    // K=255 → black
        ];

        using SKBitmap bmp = ImageRenderer.ConvertRawBytesToSkBitmap(cmyk, width: 2, height: 1, components: 4);

        SKColor cyan = bmp.GetPixel(0, 0);
        SKColor black = bmp.GetPixel(1, 0);

        // Cyan via (255-c)(255-k)/255: R=0, G=255, B=255 — and OPAQUE. The old RGBA misread produced
        // (R=255, G=0, B=0, A=0): a fully transparent red. These assertions fail on the old behaviour.
        Assert.Equal((byte)0, cyan.Red);
        Assert.Equal((byte)255, cyan.Green);
        Assert.Equal((byte)255, cyan.Blue);
        Assert.Equal((byte)255, cyan.Alpha);

        // K=255 → solid black, fully opaque.
        Assert.Equal((byte)0, black.Red);
        Assert.Equal((byte)0, black.Green);
        Assert.Equal((byte)0, black.Blue);
        Assert.Equal((byte)255, black.Alpha);
    }
}
