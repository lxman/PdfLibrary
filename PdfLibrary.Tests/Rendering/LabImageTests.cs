using System;
using System.Collections.Generic;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Images in a Lab colour space (ISO 32000-1 §8.6.5.4) must decode and render.
///
/// <para>
/// Previously <see cref="PdfImageToRgba.ToRgba"/> had no Lab branch, so it returned null and the
/// image was silently DROPPED — nothing drawn, no diagnostic. Lab fills and shadings already worked,
/// so this was a gap in the image path alone.
/// </para>
///
/// <para>
/// Lab samples do not map linearly to their component range the way RGB does. Per §8.9.5.2 the
/// default <c>/Decode</c> for a Lab image is <c>[0 100 amin amax bmin bmax]</c>, where the a/b limits
/// come from the colour space's <c>/Range</c> (itself defaulting to <c>[-100 100 -100 100]</c>) — so
/// the range must be read from the colour space, not assumed.
/// </para>
/// </summary>
public class LabImageTests
{
    private static readonly double[] D50 = [0.9642, 1.0, 0.8249];

    private static PdfArray LabArray(double[]? range = null, double[]? whitePoint = null)
    {
        var wp = new PdfArray();
        foreach (double v in whitePoint ?? D50) wp.Add(new PdfReal(v));

        var labDict = new PdfDictionary { [new PdfName("WhitePoint")] = wp };
        if (range is not null)
        {
            var r = new PdfArray();
            foreach (double v in range) r.Add(new PdfReal(v));
            labDict[new PdfName("Range")] = r;
        }

        return new PdfArray(new PdfName("Lab"), labDict);
    }

    /// <summary>A 1×1 8-bit Lab image carrying the given raw samples.</summary>
    private static PdfImage LabImage(byte l, byte a, byte b, double[]? range = null,
        double[]? decode = null, double[]? whitePoint = null)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(1),
            [new PdfName("Height")] = new PdfInteger(1),
            [new PdfName("ColorSpace")] = LabArray(range, whitePoint),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
        };
        if (decode is not null)
        {
            var d = new PdfArray();
            foreach (double v in decode) d.Add(new PdfReal(v));
            dict[new PdfName("Decode")] = d;
        }
        return new PdfImage(new PdfStream(dict, [l, a, b]));
    }

    private static (int R, int G, int B) DecodeImage(PdfImage image)
    {
        PdfImageToRgba.RgbaImage? result = PdfImageToRgba.ToRgba(image, null);
        Assert.NotNull(result);                       // was null before Lab support — image dropped
        byte[] px = result!.Value.Rgba;
        return (px[0], px[1], px[2]);
    }

    /// <summary>The same Lab triple through the FILL path, which already worked.</summary>
    private static (int R, int G, int B) ResolveFill(double l, double a, double b, double[]? range = null)
    {
        var resolver = new ColorSpaceResolver(null);
        var resources = new PdfDictionary { [new PdfName("Cs1")] = LabArray(range) };
        string? name = "Cs1";
        List<double>? color = [l, a, b];
        resolver.ResolveColorSpace(ref name, ref color, resources);
        static int B8(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return (B8(color![0]), B8(color[1]), B8(color[2]));
    }

    /// <summary>Spec sample decode: raw byte → component value over [dMin, dMax].</summary>
    private static double Dec(byte raw, double dMin, double dMax) => dMin + raw * (dMax - dMin) / 255.0;

    [Fact]
    public void Lab_image_is_no_longer_dropped()
    {
        PdfImageToRgba.RgbaImage? result = PdfImageToRgba.ToRgba(LabImage(128, 148, 98), null);
        Assert.NotNull(result);
        Assert.Equal(1, result!.Value.Width);
        Assert.Equal(1, result.Value.Height);
        Assert.Equal(4, result.Value.Rgba.Length);
        Assert.Equal(255, result.Value.Rgba[3]);      // opaque, no SMask
    }

    [Theory]
    [InlineData(128, 148, 98)]     // mid L, +a, -b
    [InlineData(255, 128, 128)]    // L=100, neutral
    [InlineData(0, 128, 128)]      // L=0, neutral
    [InlineData(200, 255, 0)]      // extreme a/b
    public void Image_path_agrees_with_the_fill_path_for_the_same_colour(byte l, byte a, byte b)
    {
        // The strongest invariant available without hard-coding colorimetry: a Lab colour must render
        // the same whether it arrives as an image sample or as a fill. Range defaults to
        // [-100 100 -100 100], so decode the raw bytes over that interval to get the fill's input.
        double[] range = [-100, 100, -100, 100];
        (int R, int G, int B) viaImage = DecodeImage(LabImage(l, a, b));
        (int R, int G, int B) viaFill = ResolveFill(
            Dec(l, 0, 100), Dec(a, range[0], range[1]), Dec(b, range[2], range[3]));

        Assert.True(Math.Abs(viaImage.R - viaFill.R) <= 2
                    && Math.Abs(viaImage.G - viaFill.G) <= 2
                    && Math.Abs(viaImage.B - viaFill.B) <= 2,
            $"image={viaImage} fill={viaFill}");
    }

    [Fact]
    public void Range_from_the_colour_space_is_honoured()
    {
        // Identical raw bytes must decode differently under different /Range values — otherwise the
        // range is being ignored and a/b are silently assumed to span the default interval.
        (int R, int G, int B) narrow = DecodeImage(LabImage(128, 255, 0, [-10, 10, -10, 10]));
        (int R, int G, int B) wide = DecodeImage(LabImage(128, 255, 0, [-128, 127, -128, 127]));

        Assert.True(Math.Abs(narrow.R - wide.R) > 20 || Math.Abs(narrow.B - wide.B) > 20,
            $"/Range ignored: narrow={narrow}, wide={wide}");
    }

    [Fact]
    public void Wide_range_image_agrees_with_the_fill_path()
    {
        double[] range = [-128, 127, -128, 127];
        (int R, int G, int B) viaImage = DecodeImage(LabImage(128, 200, 40, range));
        (int R, int G, int B) viaFill = ResolveFill(
            Dec(128, 0, 100), Dec(200, range[0], range[1]), Dec(40, range[2], range[3]), range);

        Assert.True(Math.Abs(viaImage.R - viaFill.R) <= 2
                    && Math.Abs(viaImage.G - viaFill.G) <= 2
                    && Math.Abs(viaImage.B - viaFill.B) <= 2,
            $"image={viaImage} fill={viaFill}");
    }

    [Fact]
    public void Explicit_Decode_array_overrides_the_range_derived_default()
    {
        // /Decode [0 100 0 0 0 0] forces a and b to zero regardless of the samples: a neutral grey.
        (int R, int G, int B) forced = DecodeImage(
            LabImage(128, 255, 0, [-128, 127, -128, 127], decode: [0, 100, 0, 0, 0, 0]));

        Assert.True(Math.Abs(forced.R - forced.G) <= 2 && Math.Abs(forced.G - forced.B) <= 2,
            $"/Decode ignored — expected neutral, got {forced}");
    }

    [Fact]
    public void Sixteen_bit_lab_reaches_the_same_path_via_high_byte_down_conversion()
    {
        // 16 bpc images are down-converted to their high bytes before the colour-space switch, so a
        // 16-bit Lab image must land on the same branch and agree with its 8-bit equivalent.
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(1),
            [new PdfName("Height")] = new PdfInteger(1),
            [new PdfName("ColorSpace")] = LabArray(),
            [new PdfName("BitsPerComponent")] = new PdfInteger(16),
        };
        // Big-endian pairs whose high bytes are 128, 148, 98.
        var image16 = new PdfImage(new PdfStream(dict, [128, 0xFF, 148, 0xFF, 98, 0xFF]));

        Assert.Equal(DecodeImage(LabImage(128, 148, 98)), DecodeImage(image16));
    }

    [Fact]
    public void Neutral_lab_image_renders_neutral()
    {
        (int R, int G, int B) grey = DecodeImage(LabImage(128, 128, 128));
        Assert.True(Math.Abs(grey.R - grey.G) <= 2 && Math.Abs(grey.G - grey.B) <= 2,
            $"expected neutral, got {grey}");
    }
}
