using Jp2Codec;
using Jp2Codec.Color;

namespace Jp2Codec.Tests.Color;

public class ChromaUpsamplerTests
{
    [Fact]
    public void Full_Res_Component_Returned_Unchanged()
    {
        var data = new int[] { 1, 2, 3, 4, 5, 6 }; // 3x2
        var result = MakeResult(
            width: 3, height: 2,
            components: new[] { (data, 3, 2, 8, false) });

        int[][] upsampled = ChromaUpsampler.UpsampleAll(result);

        Assert.Same(data, upsampled[0]); // reference identity — no copy
    }

    [Fact]
    public void Half_Res_Chroma_Replicates_2x2()
    {
        // Luma 4x4 (full image), chroma 2x2 (half in both axes).
        var luma   = new int[] { 100, 101, 102, 103,
                                 104, 105, 106, 107,
                                 108, 109, 110, 111,
                                 112, 113, 114, 115 };
        var cb     = new int[] { 200, 210,
                                 220, 230 };
        var cr     = new int[] { 50, 60,
                                 70, 80 };

        var result = MakeResult(
            width: 4, height: 4,
            components: new[]
            {
                (luma, 4, 4, 8, false),
                (cb,   2, 2, 8, false),
                (cr,   2, 2, 8, false),
            });

        int[][] up = ChromaUpsampler.UpsampleAll(result);
        Assert.Same(luma, up[0]);

        // cb expected: each 2x2 block of input replicates to a 2x2 block.
        int[] expectedCb = {
            200, 200, 210, 210,
            200, 200, 210, 210,
            220, 220, 230, 230,
            220, 220, 230, 230,
        };
        int[] expectedCr = {
            50, 50, 60, 60,
            50, 50, 60, 60,
            70, 70, 80, 80,
            70, 70, 80, 80,
        };
        Assert.Equal(expectedCb, up[1]);
        Assert.Equal(expectedCr, up[2]);
    }

    [Fact]
    public void Quarter_Res_Chroma_Replicates_4x4()
    {
        // Luma 4x4, chroma 1x1 (full image average — degenerate case).
        var luma = new int[16];
        for (var i = 0; i < 16; i++) luma[i] = i;
        var chroma = new int[] { 42 };

        var result = MakeResult(
            width: 4, height: 4,
            components: new[]
            {
                (luma, 4, 4, 8, false),
                (chroma, 1, 1, 8, false),
            });

        int[][] up = ChromaUpsampler.UpsampleAll(result);
        for (var i = 0; i < 16; i++)
            Assert.Equal(42, up[1][i]);
    }

    [Fact]
    public void Odd_Image_Width_Truncates_Replication_At_Right_Edge()
    {
        // Image 3x2, chroma 2x1: factorX = ceil(3/2) = 2, but the third
        // output column maps to xIn=1 (clamped, not xIn=2 out-of-range).
        var luma = new int[] { 1, 2, 3, 4, 5, 6 };
        var cb   = new int[] { 10, 20 };

        var result = MakeResult(
            width: 3, height: 2,
            components: new[]
            {
                (luma, 3, 2, 8, false),
                (cb,   2, 1, 8, false),
            });
        int[][] up = ChromaUpsampler.UpsampleAll(result);
        Assert.Equal(new[] { 10, 10, 20,  // row 0
                             10, 10, 20 }, // row 1
                     up[1]);
    }

    private static Jp2DecodeResult MakeResult(
        int width, int height,
        (int[] data, int w, int h, int precision, bool signed)[] components)
    {
        int n = components.Length;
        var data = new int[n][];
        var widths = new int[n];
        var heights = new int[n];
        var precisions = new int[n];
        var signed = new bool[n];
        for (var i = 0; i < n; i++)
        {
            data[i] = components[i].data;
            widths[i] = components[i].w;
            heights[i] = components[i].h;
            precisions[i] = components[i].precision;
            signed[i] = components[i].signed;
        }
        return new Jp2DecodeResult(
            width, height, data, widths, heights, precisions, signed,
            Jp2ColorSpace.Unspecified);
    }
}
