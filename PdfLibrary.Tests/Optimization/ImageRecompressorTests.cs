using PdfLibrary.Core.Primitives;
using PdfLibrary.Optimization;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Optimization;

/// <summary>
/// Table-driven tests for ImageRecompressor.IsImageRecompressible.
/// Each test covers one rejection reason or the two acceptance cases.
/// </summary>
public class ImageRecompressorTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a minimal eligible DeviceRGB FlateDecode image stream (256x256).
    /// Callers mutate the dictionary to create rejection cases.
    /// </summary>
    private static PdfStream MakeEligibleRgb(int w = 256, int h = 256)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Subtype")]         = new PdfName("Image"),
            [PdfName.Filter]                  = new PdfName("FlateDecode"),
            [PdfName.Width]                   = new PdfInteger(w),
            [PdfName.Height]                  = new PdfInteger(h),
            [PdfName.ColorSpace]              = new PdfName("DeviceRGB"),
            [PdfName.BitsPerComponent]        = new PdfInteger(8),
        };
        // Data length doesn't need to be real for predicate testing.
        return new PdfStream(dict, new byte[w * h * 3]);
    }

    private static PdfStream MakeEligibleGray(int w = 256, int h = 256)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Subtype")]   = new PdfName("Image"),
            [PdfName.Filter]            = new PdfName("FlateDecode"),
            [PdfName.Width]             = new PdfInteger(w),
            [PdfName.Height]            = new PdfInteger(h),
            [PdfName.ColorSpace]        = new PdfName("DeviceGray"),
            [PdfName.BitsPerComponent]  = new PdfInteger(8),
        };
        return new PdfStream(dict, new byte[w * h]);
    }

    // ── Acceptance cases ───────────────────────────────────────────────────────

    [Fact]
    public void Accept_DeviceRGB_FlateDecode()
    {
        PdfStream s = MakeEligibleRgb();
        Assert.True(ImageRecompressor.IsImageRecompressible(s, null));
    }

    [Fact]
    public void Accept_DeviceRGB_DCTDecode()
    {
        PdfStream s = MakeEligibleRgb();
        s.Dictionary[PdfName.Filter] = new PdfName("DCTDecode");
        s.Dictionary[PdfName.ColorSpace] = new PdfName("DeviceRGB");
        Assert.True(ImageRecompressor.IsImageRecompressible(s, null));
    }

    [Fact]
    public void Accept_RGB_Alias()
    {
        PdfStream s = MakeEligibleRgb();
        s.Dictionary[PdfName.ColorSpace] = new PdfName("RGB");
        Assert.True(ImageRecompressor.IsImageRecompressible(s, null));
    }

    [Fact]
    public void Accept_DeviceGray_FlateDecode()
    {
        PdfStream s = MakeEligibleGray();
        Assert.True(ImageRecompressor.IsImageRecompressible(s, null));
    }

    [Fact]
    public void Accept_G_Alias()
    {
        PdfStream s = MakeEligibleGray();
        s.Dictionary[PdfName.ColorSpace] = new PdfName("G");
        Assert.True(ImageRecompressor.IsImageRecompressible(s, null));
    }

    // ── Rejection: not an image XObject ────────────────────────────────────────

    [Fact]
    public void Reject_NotImageXObject_MissingSubtype()
    {
        PdfStream s = MakeEligibleRgb();
        s.Dictionary.Remove(new PdfName("Subtype"));
        Assert.False(ImageRecompressor.IsImageRecompressible(s, null));
    }

    [Fact]
    public void Reject_NotImageXObject_WrongSubtype()
    {
        PdfStream s = MakeEligibleRgb();
        s.Dictionary[new PdfName("Subtype")] = new PdfName("Form");
        Assert.False(ImageRecompressor.IsImageRecompressible(s, null));
    }

    // ── Rejection: bits per component ─────────────────────────────────────────

    [Fact]
    public void Reject_BitsPerComponent_Not8()
    {
        PdfStream s = MakeEligibleRgb();
        s.Dictionary[PdfName.BitsPerComponent] = new PdfInteger(1);
        Assert.False(ImageRecompressor.IsImageRecompressible(s, null));
    }

    [Fact]
    public void Reject_BitsPerComponent_16()
    {
        PdfStream s = MakeEligibleRgb();
        s.Dictionary[PdfName.BitsPerComponent] = new PdfInteger(16);
        Assert.False(ImageRecompressor.IsImageRecompressible(s, null));
    }

    // ── Rejection: filter ─────────────────────────────────────────────────────

    [Fact]
    public void Reject_NoFilter()
    {
        PdfStream s = MakeEligibleRgb();
        s.Dictionary.Remove(PdfName.Filter);
        Assert.False(ImageRecompressor.IsImageRecompressible(s, null));
    }

    [Fact]
    public void Reject_FilterArray()
    {
        PdfStream s = MakeEligibleRgb();
        var arr = new PdfArray { new PdfName("FlateDecode") };
        s.Dictionary[PdfName.Filter] = arr;
        Assert.False(ImageRecompressor.IsImageRecompressible(s, null));
    }

    [Fact]
    public void Reject_UnsupportedFilter_JBIG2()
    {
        PdfStream s = MakeEligibleRgb();
        s.Dictionary[PdfName.Filter] = new PdfName("JBIG2Decode");
        Assert.False(ImageRecompressor.IsImageRecompressible(s, null));
    }

    [Fact]
    public void Reject_UnsupportedFilter_JPX()
    {
        PdfStream s = MakeEligibleRgb();
        s.Dictionary[PdfName.Filter] = new PdfName("JPXDecode");
        Assert.False(ImageRecompressor.IsImageRecompressible(s, null));
    }

    // ── Rejection: color space ────────────────────────────────────────────────

    [Fact]
    public void Reject_CMYK_ColorSpace()
    {
        PdfStream s = MakeEligibleRgb();
        s.Dictionary[PdfName.ColorSpace] = new PdfName("DeviceCMYK");
        Assert.False(ImageRecompressor.IsImageRecompressible(s, null));
    }

    [Fact]
    public void Reject_ArrayColorSpace_ICCBased()
    {
        PdfStream s = MakeEligibleRgb();
        var arr = new PdfArray { new PdfName("ICCBased"), new PdfDictionary() };
        s.Dictionary[PdfName.ColorSpace] = arr;
        Assert.False(ImageRecompressor.IsImageRecompressible(s, null));
    }

    [Fact]
    public void Reject_ArrayColorSpace_Indexed()
    {
        PdfStream s = MakeEligibleRgb();
        var arr = new PdfArray { new PdfName("Indexed") };
        s.Dictionary[PdfName.ColorSpace] = arr;
        Assert.False(ImageRecompressor.IsImageRecompressible(s, null));
    }

    // ── Rejection: soft mask / image mask / decode ────────────────────────────

    [Fact]
    public void Reject_HasSMask()
    {
        PdfStream s = MakeEligibleRgb();
        s.Dictionary[new PdfName("SMask")] = new PdfInteger(5); // mock reference
        Assert.False(ImageRecompressor.IsImageRecompressible(s, null));
    }

    [Fact]
    public void Reject_HasImageMask()
    {
        PdfStream s = MakeEligibleRgb();
        s.Dictionary[new PdfName("ImageMask")] = PdfBoolean.True;
        Assert.False(ImageRecompressor.IsImageRecompressible(s, null));
    }

    [Fact]
    public void Reject_HasDecode()
    {
        PdfStream s = MakeEligibleRgb();
        s.Dictionary[new PdfName("Decode")] = new PdfArray { new PdfInteger(0), new PdfInteger(1) };
        Assert.False(ImageRecompressor.IsImageRecompressible(s, null));
    }

    // ── Rejection: pixel count too small ──────────────────────────────────────

    [Fact]
    public void Reject_TooSmall_127x128()
    {
        // 127*128 = 16256 < 16384
        PdfStream s = MakeEligibleRgb(127, 128);
        Assert.False(ImageRecompressor.IsImageRecompressible(s, null));
    }

    [Fact]
    public void Accept_ExactlyAtMinimum_128x128()
    {
        // 128*128 = 16384 >= 16384
        PdfStream s = MakeEligibleRgb(128, 128);
        Assert.True(ImageRecompressor.IsImageRecompressible(s, null));
    }
}
