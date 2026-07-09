using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// <see cref="PdfImageToCmyk"/> recovers native DeviceCMYK samples (no lossy CMYK→sRGB round-trip) for
/// images whose colour resolves to DeviceCMYK, so a CMYK compositor can paint them in native ink.
/// </summary>
public class PdfImageToCmykTests
{
    private static PdfImage Image(PdfObject colorSpace, byte[] data, int w, int h, int bpc = 8)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(w),
            [new PdfName("Height")] = new PdfInteger(h),
            [new PdfName("ColorSpace")] = colorSpace,
            [new PdfName("BitsPerComponent")] = new PdfInteger(bpc),
        };
        return new PdfImage(new PdfStream(dict, data));
    }

    [Fact]
    public void Direct_DeviceCMYK_returns_native_samples()
    {
        // Two pixels: 50% magenta (0,128,0,0), pure cyan (255,0,0,0).
        byte[] data = [0, 128, 0, 0, 255, 0, 0, 0];
        PdfImage img = Image(new PdfName("DeviceCMYK"), data, 2, 1);

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out int w, out int h);

        Assert.NotNull(cmyk);
        Assert.Equal(2, w); Assert.Equal(1, h);
        Assert.Equal(data, cmyk);
    }

    [Fact]
    public void Indexed_DeviceCMYK_looks_up_native_palette()
    {
        // Palette: entry0 = 50% magenta, entry1 = pure cyan. Pixels index [1,0,1].
        byte[] palette = [0, 128, 0, 0, 255, 0, 0, 0];
        var cs = new PdfArray(new PdfName("Indexed"), new PdfName("DeviceCMYK"),
            new PdfInteger(1), new PdfString(palette));
        byte[] indices = [1, 0, 1];
        PdfImage img = Image(cs, indices, 3, 1);

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out int w, out int h);

        Assert.NotNull(cmyk);
        Assert.Equal(3, w); Assert.Equal(1, h);
        Assert.Equal(new byte[] { 255, 0, 0, 0, /* cyan */  0, 128, 0, 0, /* magenta */  255, 0, 0, 0 }, cmyk);
    }

    [Fact]
    public void DeviceRGB_image_returns_null()
    {
        PdfImage img = Image(new PdfName("DeviceRGB"), new byte[2 * 1 * 3], 2, 1);
        Assert.Null(PdfImageToCmyk.TryToCmyk(img, null, out _, out _));
    }

    [Fact]
    public void Direct_DeviceCMYK_honours_inverting_decode()
    {
        byte[] data = [0, 128, 0, 0];  // one pixel
        var dict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(1),
            [new PdfName("Height")] = new PdfInteger(1),
            [new PdfName("ColorSpace")] = new PdfName("DeviceCMYK"),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
            [new PdfName("Decode")] = new PdfArray(
                new PdfReal(1), new PdfReal(0), new PdfReal(1), new PdfReal(0),
                new PdfReal(1), new PdfReal(0), new PdfReal(1), new PdfReal(0)),
        };
        var img = new PdfImage(new PdfStream(dict, data));

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out _, out _);

        Assert.NotNull(cmyk);
        Assert.Equal(new byte[] { 255, 127, 255, 255 }, cmyk); // inverted: 255-s
    }

    [Fact]
    public void DeviceGray_8bpc_separates_to_K_only_ink()
    {
        // black, mid, white → K-only ink (C=M=Y=0, K = 1 − grey). A grey image then lands on the same
        // black plate as an adjacent DeviceCMYK(0,0,0,k) box instead of an RGB→CMYK round-trip (GWG173).
        byte[] data = [0, 128, 255];
        PdfImage img = Image(new PdfName("DeviceGray"), data, 3, 1);

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out int w, out int h);

        Assert.NotNull(cmyk);
        Assert.Equal(3, w); Assert.Equal(1, h);
        Assert.Equal(new byte[] { 0, 0, 0, 255,  0, 0, 0, 127,  0, 0, 0, 0 }, cmyk);
    }

    [Fact]
    public void DeviceGray_1bpc_unpacks_to_K_only_ink()
    {
        // 8 pixels in one byte, MSB-first: 1 0 1 0 0 0 0 0 → white,black,white,black×5 (bit 1 = white).
        byte[] data = [0b1010_0000];
        PdfImage img = Image(new PdfName("DeviceGray"), data, 8, 1, bpc: 1);

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out int w, out int h);

        Assert.NotNull(cmyk);
        Assert.Equal(8, w); Assert.Equal(1, h);
        byte[] expectedK = [0, 255, 0, 255, 255, 255, 255, 255];
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(0, cmyk![i * 4]);          // C
            Assert.Equal(0, cmyk[i * 4 + 1]);       // M
            Assert.Equal(0, cmyk[i * 4 + 2]);       // Y
            Assert.Equal(expectedK[i], cmyk[i * 4 + 3]); // K
        }
    }

    [Fact]
    public void DeviceGray_16bpc_keeps_high_byte_as_K_only_ink()
    {
        // Two 16-bit big-endian samples: 0x0000 (black) and 0xFFFF (white). High byte drives K, so
        // K = 255 then 0 — same K path an 8-bit tile of the same image takes (GWG183 8-bit vs 16-bit).
        byte[] data = [0x00, 0x00, 0xFF, 0xFF];
        PdfImage img = Image(new PdfName("DeviceGray"), data, 2, 1, bpc: 16);

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out int w, out int h);

        Assert.NotNull(cmyk);
        Assert.Equal(2, w); Assert.Equal(1, h);
        Assert.Equal(new byte[] { 0, 0, 0, 255,  0, 0, 0, 0 }, cmyk);
    }

    [Fact]
    public void DeviceGray_honours_inverting_decode()
    {
        byte[] data = [0]; // black sample; [1 0] decode flips it to white → K = 0
        var dict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(1),
            [new PdfName("Height")] = new PdfInteger(1),
            [new PdfName("ColorSpace")] = new PdfName("DeviceGray"),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
            [new PdfName("Decode")] = new PdfArray(new PdfReal(1), new PdfReal(0)),
        };
        var img = new PdfImage(new PdfStream(dict, data));

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out _, out _);

        Assert.NotNull(cmyk);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, cmyk);
    }
}
