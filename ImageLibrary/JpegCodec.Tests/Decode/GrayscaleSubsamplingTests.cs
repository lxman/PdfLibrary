using JpegCodec;
using JpegCodec.Tests.Corpus;
using PngCodec;

namespace JpegCodec.Tests.Decode;

public class GrayscaleSubsamplingTests
{
    [Fact]
    public void Grayscale_h2v2_blocks_are_placed_in_the_right_order()
    {
        // backhoe-006.jpg is a single-component (grayscale) baseline JPEG with H2V2 sampling. The
        // blocks decoded correctly, but the grayscale interleave placed them at the wrong horizontal
        // positions — it used half the true raster stride when the single component is H=2-subsampled,
        // squishing the image to half width and duplicating it. Verified against ImageSharp's reference
        // decode of the same file (a correct decode matches it to ~0.01 MAE; the bug produced ~60).
        JpegDecodeResult jpeg = new JpegStreamDecoder().Decode(CorpusFiles.Load("grayscale_h2v2/backhoe-006.jpg"));
        PngImage reference = PngDecoder.Decode(CorpusFiles.Load("grayscale_h2v2/backhoe-006.ref.png"));

        Assert.Equal(1, jpeg.NumberOfComponents);
        Assert.Equal(reference.Width, jpeg.Width);
        Assert.Equal(reference.Height, jpeg.Height);

        long sum = 0;
        int count = jpeg.Width * jpeg.Height;
        for (var i = 0; i < count; i++)
            sum += Math.Abs(jpeg.ComponentData[i] - reference.PixelData[i * 4 + 2]); // ref R channel == luma
        double mae = sum / (double)count;

        Assert.True(mae < 2.0, $"grayscale H2V2 decode diverged from reference (MAE={mae:F2}) — blocks likely mis-ordered");
    }
}
