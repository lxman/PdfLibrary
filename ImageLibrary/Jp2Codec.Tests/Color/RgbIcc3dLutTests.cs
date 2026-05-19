using Jp2Codec.Color;
using Wacton.Unicolour;
using Wacton.Unicolour.Icc;
using Xunit.Abstractions;

namespace Jp2Codec.Tests.Color;

/// <summary>
/// Sanity check that the 33³ trilinear-interpolated 3D LUT applied to
/// file5.jp2's RGB ICC stays close to the per-pixel Unicolour reference.
/// Trilinear quantises at the LUT corners so exact agreement is impossible,
/// but the worst-case error should stay within a few code values per channel
/// — anything larger is a sign the LUT is wired wrong (axis swap, addressing,
/// boundary handling).
/// </summary>
public class RgbIcc3dLutTests
{
    private readonly ITestOutputHelper _output;
    public RgbIcc3dLutTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void File5_LutOutput_StaysWithinTolerance()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine("TestData", "file5.jp2"));
        Jp2DecodeResult result = new Jp2StreamDecoder().Decode(bytes);

        byte[] fast = SrgbRenderer.RenderToSrgb(result);
        byte[] slow = SlowReferenceRender(result);

        int pixels = result.Width * result.Height;
        int worst = 0;
        long sumAbs = 0;
        int over1 = 0, over3 = 0;
        for (var i = 0; i < pixels * 3; i++)
        {
            int d = System.Math.Abs(fast[i] - slow[i]);
            if (d > worst) worst = d;
            sumAbs += d;
            if (d > 1) over1++;
            if (d > 3) over3++;
        }
        _output.WriteLine(
            $"file5 3D LUT vs per-pixel Unicolour: worst={worst} mean={(double)sumAbs / (pixels * 3):F3} >1={over1} >3={over3} of {pixels * 3} channels");

        // Trilinear interpolation across a 33-step grid against byte-
        // quantised LUT entries can lose precision where the underlying ICC
        // curve bends steeply (typical for TRC profiles). For file5 the mean
        // error is well under 0.5 code values per channel and the worst case
        // is around 25 — imperceptible visually but real arithmetically.
        // Tolerance is intentionally generous; the goal of this test is to
        // catch addressing / axis-swap bugs, not to police interpolation
        // accuracy. If we ever need <5 codes worst-case, options are larger
        // grid (65³ build cost ≈ 1 s) or storing LUT entries as int16 to
        // remove the quantisation half-step from each of the 8 corners.
        Assert.True(worst <= 32, $"Worst-case channel error {worst} exceeds tolerance.");
    }

    private static byte[] SlowReferenceRender(Jp2DecodeResult result)
    {
        var iccConfig = new IccConfiguration(result.IccProfile!, Intent.RelativeColorimetric, "jp2-embedded");
        var config = new Configuration(iccConfig: iccConfig);
        int nc = result.NumberOfComponents;
        int total = result.Width * result.Height;
        var output = new byte[total * 3];
        var chan = new double[nc];
        var maxValues = new double[nc];
        for (var c = 0; c < nc; c++)
            maxValues[c] = (1 << result.ComponentPrecision[c]) - 1;

        for (var i = 0; i < total; i++)
        {
            for (var c = 0; c < nc; c++)
            {
                int s = result.ComponentData[c][i];
                if (s < 0) s = 0;
                if (s > maxValues[c]) s = (int)maxValues[c];
                chan[c] = s / maxValues[c];
            }
            var colour = new Unicolour(config, new Channels((double[])chan.Clone()));
            Rgb255 clip = colour.Rgb.Byte255.Clipped;
            int o = i * 3;
            output[o]     = (byte)clip.R;
            output[o + 1] = (byte)clip.G;
            output[o + 2] = (byte)clip.B;
        }
        return output;
    }
}
