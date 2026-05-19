using CoreJ2K;
using CoreJ2K.Util;
using Jp2Codec.Color;

namespace Jp2Codec.Tests.Color;

/// <summary>
/// Pins <see cref="SrgbRenderer"/>'s sYCC path bit-exact against CSJ2K's
/// SYccColorSpaceMapper output. Both decoders apply the same BT.601 matrix
/// (coefficients 1.402, -0.34413, -0.71414, 1.772) in float-32 and round
/// the same way (truncate toward zero); the only previous source of
/// divergence — Unicolour's double-precision Ycbcr conversion — has been
/// removed.
/// </summary>
public class SyccBitExactTests
{
    [Fact]
    public void File2_SrgbRenderer_BitExact_VsCsj2k()
    {
        AssertBitExact("file2.jp2");
    }

    [Fact]
    public void File3_SrgbRenderer_BitExact_VsCsj2k_WithChromaUpsampling()
    {
        // file3 carries 4:2:0 chroma; our SrgbRenderer calls ChromaUpsampler
        // before applying the sYCC matrix. CSJ2K runs its Resampler then the
        // same matrix. If our upsampler matches CSJ2K's pixel replication
        // (it does — both use floor(yOut/2) row-replication and 2-copy column
        // expansion), the rendered sRGB output should also be bit-exact.
        AssertBitExact("file3.jp2");
    }

    private static void AssertBitExact(string name)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine("TestData", name));
        Jp2DecodeResult ours = new Jp2StreamDecoder().Decode(bytes);
        byte[] oursSrgb = SrgbRenderer.RenderToSrgb(ours);

        using var ms = new MemoryStream(bytes);
        PortableImage img = J2kImage.FromStream(ms);
        int[] refR = img.GetComponent(0);
        int[] refG = img.GetComponent(1);
        int[] refB = img.GetComponent(2);

        int pixels = ours.Width * ours.Height;
        int firstDiff = -1;
        for (var i = 0; i < pixels; i++)
        {
            if (oursSrgb[i * 3]     != (byte)refR[i] ||
                oursSrgb[i * 3 + 1] != (byte)refG[i] ||
                oursSrgb[i * 3 + 2] != (byte)refB[i])
            {
                firstDiff = i;
                break;
            }
        }
        if (firstDiff >= 0)
        {
            Assert.Fail(
                $"{name} sYCC output diverged from CSJ2K at pixel {firstDiff}: " +
                $"ours=({oursSrgb[firstDiff * 3]},{oursSrgb[firstDiff * 3 + 1]},{oursSrgb[firstDiff * 3 + 2]}) " +
                $"ref=({refR[firstDiff]},{refG[firstDiff]},{refB[firstDiff]}).");
        }
    }
}
