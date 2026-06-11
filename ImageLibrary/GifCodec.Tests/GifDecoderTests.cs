using Xunit;

namespace GifCodec.Tests;

/// <summary>
/// Regression coverage built from hand-assembled GIF bytes: the minimum-code-size-1 acceptance fix
/// and two malformed-input guards (sub-block overrun, oversized frame).
/// </summary>
public class GifDecoderTests
{
    [Fact]
    public void MinCodeSize1_two_colour_gif_decodes()
    {
        // 1x1 GIF, 2-colour global table (red, green), LZW minimum code size = 1. This was rejected
        // outright before. The LZW stream is clear(2), code 0, end(3) packed LSB-first into 0x32.
        byte[] gif =
        [
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61,                 // "GIF89a"
            0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00,           // screen: 1x1, GCT present (2 colours)
            0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00,                 // GCT: red, green
            0x2C,                                               // image separator
            0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, // descriptor: 0,0, 1x1, packed 0
            0x01,                                               // LZW minimum code size = 1
            0x01, 0x32, 0x00,                                   // sub-block: len 1, data 0x32, terminator
            0x3B                                                // trailer
        ];

        GifFile file = GifDecoder.Decode(gif);

        Assert.Single(file.Frames);
        (byte R, byte G, byte B, byte A) p = file.Frames[0].GetPixel(0, 0);
        Assert.Equal(((byte)255, (byte)0, (byte)0), (p.R, p.G, p.B)); // colour index 0 = red
    }

    [Fact]
    public void Subblock_running_past_end_throws()
    {
        // The image-data sub-block claims 255 bytes but only 2 follow; the scan must reject this
        // rather than letting the end index run past the buffer.
        byte[] gif =
        [
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61,
            0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00,
            0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00,
            0x2C,
            0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
            0x02,                                               // min code size
            0xFF, 0x01, 0x02                                    // sub-block size 255, only 2 bytes left
        ];

        Assert.Throws<GifException>(() => GifDecoder.Decode(gif));
    }

    [Fact]
    public void Oversize_frame_dimensions_throw()
    {
        // 60000x60000 frame → width*height*4 overflows int; the guard computes in long and throws.
        byte[] gif =
        [
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61,
            0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00,           // screen 1x1, no GCT
            0x2C,
            0x00, 0x00, 0x00, 0x00, 0x60, 0xEA, 0x60, 0xEA, 0x00 // frame 60000x60000 (0xEA60)
        ];

        Assert.Throws<GifException>(() => GifDecoder.Decode(gif));
    }
}
