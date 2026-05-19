using Jp2Codec.Color;
using Wacton.Unicolour;
using Wacton.Unicolour.Icc;

namespace Jp2Codec.Tests.Color;

/// <summary>
/// Pins the monochrome-ICC LUT fast path to the unoptimised reference
/// pipeline. file8.jp2 is the conformance case: 8-bit greyscale with an
/// embedded sRGB monochrome ICC profile.
/// </summary>
public class MonochromeIccLutTests
{
    [Fact]
    public void Lut_BitExact_VsReference_File8()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine("TestData", "file8.jp2"));
        Jp2DecodeResult result = new Jp2StreamDecoder().Decode(bytes);

        byte[] fastPath = SrgbRenderer.RenderToSrgb(result);
        byte[] referencePath = ReferenceMonochromeIccRender(result);

        Assert.True(fastPath.SequenceEqual(referencePath),
            $"Monochrome ICC LUT diverged from per-pixel reference. " +
            $"First mismatch at byte {FirstMismatch(fastPath, referencePath)}.");
    }

    private static int FirstMismatch(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
            if (a[i] != b[i]) return i;
        return a.Length == b.Length ? -1 : n;
    }

    /// <summary>
    /// Mirror of the pre-optimisation slow path: invoke Unicolour once per
    /// pixel, no LUT, no shortcut. Used purely to pin LUT correctness.
    /// </summary>
    private static byte[] ReferenceMonochromeIccRender(Jp2DecodeResult result)
    {
        if (result.NumberOfComponents != 1)
            throw new InvalidOperationException("Reference path only handles single-component images.");

        var iccConfig = new IccConfiguration(result.IccProfile!, Intent.RelativeColorimetric, "jp2-embedded");
        var config = new Configuration(iccConfig: iccConfig);

        int total = result.Width * result.Height;
        var output = new byte[total * 3];
        var chanBuf = new double[1];
        double max = (1 << result.ComponentPrecision[0]) - 1;
        int[] samples = result.ComponentData[0];

        for (var i = 0; i < total; i++)
        {
            int sample = samples[i];
            if (sample < 0) sample = 0;
            if (sample > max) sample = (int)max;
            chanBuf[0] = sample / max;
            var colour = new Unicolour(config, new Channels((double[])chanBuf.Clone()));
            Rgb255 clip = colour.Rgb.Byte255.Clipped;
            int o = i * 3;
            output[o]     = (byte)clip.R;
            output[o + 1] = (byte)clip.G;
            output[o + 2] = (byte)clip.B;
        }
        return output;
    }
}
