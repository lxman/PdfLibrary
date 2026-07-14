using PdfLibrary.Content;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// <see cref="RecordingRenderTarget.DrawImage"/> attaches per-spot image ink (Soft-Proof SP-6a): a
/// recorded <see cref="ImageCommand"/> for a Separation/DeviceN spot image carries
/// <see cref="ImageCommand.Spots"/>; a DeviceCMYK image (no spot colorant) leaves it null (unchanged
/// behaviour, same as every existing recording test that never touches an image).
/// </summary>
public class RecordingRenderTargetSpotTests
{
    private static PdfArray Reals(params double[] v)
    {
        var items = new PdfObject[v.Length];
        for (var i = 0; i < v.Length; i++) items[i] = new PdfReal(v[i]);
        return new PdfArray(items);
    }

    // Type 2 exponential (N=1): linear ramp C0..C1. This MUST be a real, evaluable tint function —
    // DrawImage decodes the image via PdfImageToRgba.ToRgba first (to get Rgba/Alpha), and that path
    // evaluates the Separation tint transform. TryToSpotInk's own unit tests get away with a bare
    // "Identity" name placeholder because TryToSpotInk splits by colorant NAME and never evaluates the
    // function — but ToRgba does, so a placeholder here would make DrawImage decode nothing at all.
    private static PdfDictionary Type2(double[] c0, double[] c1)
    {
        var d = new PdfDictionary();
        d.Add(new PdfName("FunctionType"), new PdfInteger(2));
        d.Add(new PdfName("Domain"), Reals(0, 1));
        d.Add(new PdfName("C0"), Reals(c0));
        d.Add(new PdfName("C1"), Reals(c1));
        d.Add(new PdfName("N"), new PdfReal(1));
        return d;
    }

    // [/Separation /GWG Green /DeviceCMYK {t -> [0 0.5t t 0]}] — a spot colorant, not Process.
    private static PdfArray SeparationGwgGreen() => new(
        new PdfName("Separation"), new PdfName("GWG Green"), new PdfName("DeviceCMYK"),
        Type2([0, 0, 0, 0], [0, 0.5, 1, 0]));

    private static PdfImage SpotImage(byte[] data, int w, int h)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(w),
            [new PdfName("Height")] = new PdfInteger(h),
            [new PdfName("ColorSpace")] = SeparationGwgGreen(),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
        };
        return new PdfImage(new PdfStream(dict, data));
    }

    private static PdfImage DeviceCmykImage(byte[] data, int w, int h)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(w),
            [new PdfName("Height")] = new PdfInteger(h),
            [new PdfName("ColorSpace")] = new PdfName("DeviceCMYK"),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
        };
        return new PdfImage(new PdfStream(dict, data));
    }

    [Fact]
    public void DrawImage_records_Spots_for_a_separation_spot_image()
    {
        const int w = 2, h = 1;
        byte[] data = [255, 128];   // 2 px: tint 1.0, tint ~0.5
        PdfImage img = SpotImage(data, w, h);
        var rec = new RecordingRenderTarget(document: null);

        rec.DrawImage(img, new PdfGraphicsState());

        PageDrawList list = rec.TakeSnapshot();
        var spotImageCommand = Assert.IsType<ImageCommand>(Assert.Single(list.Commands));

        // A recorded ImageCommand for a Separation-spot image carries Spots.
        // (Names order == the colour space's spot colorants; TintPlanes/ProcessCmyk sized Width*Height.)
        Assert.NotNull(spotImageCommand.Spots);
        Assert.Equal(new[] { "GWG Green" }, spotImageCommand.Spots!.Names);
        Assert.Equal(w * h, spotImageCommand.Spots.TintPlanes.Length);       // 1 spot plane
        Assert.Equal(w * h * 4, spotImageCommand.Spots.ProcessCmyk.Length);
    }

    [Fact]
    public void DrawImage_leaves_Spots_null_for_a_DeviceCMYK_image()
    {
        byte[] data = [0, 128, 0, 0, 255, 0, 0, 0]; // 2 px direct CMYK, no spot colorant
        PdfImage img = DeviceCmykImage(data, w: 2, h: 1);
        var rec = new RecordingRenderTarget(document: null);

        rec.DrawImage(img, new PdfGraphicsState());

        PageDrawList list = rec.TakeSnapshot();
        var deviceCmykImageCommand = Assert.IsType<ImageCommand>(Assert.Single(list.Commands));

        Assert.Null(deviceCmykImageCommand.Spots);
    }
}
