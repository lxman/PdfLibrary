using Jbig2Decoder.Image;

namespace Jbig2Decoder.Tests;

/// <summary>
/// Proves the byte-aligned <see cref="BitBlit.Compose"/> is byte-for-byte
/// identical to the obvious per-pixel compositor it replaced, across the full
/// edge-case space: every combination operator, every source/dest bit
/// alignment, negative placement offsets, clipping on all four edges, and
/// random source pad bits (which must never leak into the result). The
/// jbig2dec-parity corpus tests cover real pages; this covers the corners they
/// don't reliably hit.
/// </summary>
public class BitBlitDifferentialTests
{
    /// <summary>
    /// Per-pixel reference compositor — a faithful copy of the original
    /// <c>TextRegionDecoder.Compose</c> / <c>ComposeOntoPage</c> inner loop,
    /// kept here as the oracle the optimised blit must match.
    /// </summary>
    private static void ComposeReference(Bitmap dst, Bitmap src, int dx, int dy, int op)
    {
        for (var sy = 0; sy < src.Height; sy++)
        {
            int ty = dy + sy;
            if ((uint)ty >= (uint)dst.Height) continue;
            for (var sx = 0; sx < src.Width; sx++)
            {
                int tx = dx + sx;
                if ((uint)tx >= (uint)dst.Width) continue;

                int s = src.GetPixel(sx, sy);
                int d = dst.GetPixel(tx, ty);
                int n = op switch
                {
                    0 => s | d,
                    1 => s & d,
                    2 => s ^ d,
                    3 => 1 - (s ^ d),
                    4 => s,
                    _ => s | d,
                };
                dst.SetPixel(tx, ty, n);
            }
        }
    }

    private static Bitmap MakeBitmap(int w, int h, byte[] data) => new(w, h, (byte[])data.Clone());

    private static byte[] RandomRows(Random rnd, int w, int h)
    {
        int stride = (w + 7) / 8;
        var data = new byte[stride * h];
        rnd.NextBytes(data); // fills pad bits too — the blit must ignore them
        return data;
    }

    [Fact]
    public void Fast_blit_matches_per_pixel_reference_across_fuzzed_configs()
    {
        var rnd = new Random(0xB17B11);
        for (var iter = 0; iter < 40_000; iter++)
        {
            int sw = rnd.Next(1, 41);
            int sh = rnd.Next(1, 13);
            int dw = rnd.Next(1, 49);
            int dh = rnd.Next(1, 17);
            int dx = rnd.Next(-12, dw + 5);
            int dy = rnd.Next(-4, dh + 5);
            int op = rnd.Next(0, 6); // 0..4 plus 5 to exercise the default arm

            byte[] srcData = RandomRows(rnd, sw, sh);
            byte[] dstData = RandomRows(rnd, dw, dh);

            Bitmap src = MakeBitmap(sw, sh, srcData);
            Bitmap dstRef = MakeBitmap(dw, dh, dstData);
            Bitmap dstFast = MakeBitmap(dw, dh, dstData);

            ComposeReference(dstRef, src, dx, dy, op);
            BitBlit.Compose(dstFast, src, dx, dy, op);

            if (!dstRef.Data.AsSpan().SequenceEqual(dstFast.Data))
            {
                Assert.Fail(
                    $"mismatch iter={iter} src={sw}x{sh} dst={dw}x{dh} dx={dx} dy={dy} op={op}\n" +
                    $"  ref ={BitConverter.ToString(dstRef.Data)}\n" +
                    $"  fast={BitConverter.ToString(dstFast.Data)}");
            }
        }
    }

    [Theory]
    [InlineData(0)] // OR
    [InlineData(1)] // AND
    [InlineData(2)] // XOR
    [InlineData(3)] // XNOR
    [InlineData(4)] // REPLACE
    public void Every_bit_alignment_matches_reference(int op)
    {
        var rnd = new Random(0x5A11 + op);
        // Walk every source-to-dest bit offset (dx mod 8) at a fixed wide source so
        // the windowed two-byte read is exercised at every shift, including off=0.
        for (var dx = 0; dx < 16; dx++)
        {
            const int sw = 19, sh = 5, dw = 40, dh = 7;
            byte[] srcData = RandomRows(rnd, sw, sh);
            byte[] dstData = RandomRows(rnd, dw, dh);

            Bitmap src = MakeBitmap(sw, sh, srcData);
            Bitmap dstRef = MakeBitmap(dw, dh, dstData);
            Bitmap dstFast = MakeBitmap(dw, dh, dstData);

            ComposeReference(dstRef, src, dx, 1, op);
            BitBlit.Compose(dstFast, src, dx, 1, op);

            Assert.Equal(BitConverter.ToString(dstRef.Data), BitConverter.ToString(dstFast.Data));
        }
    }

    [Fact]
    public void Fully_clipped_placements_are_no_ops()
    {
        var rnd = new Random(0xC11);
        byte[] dstData = RandomRows(rnd, 24, 8);
        Bitmap src = MakeBitmap(10, 4, RandomRows(rnd, 10, 4));

        foreach ((int dx, int dy) in new[] { (-10, 0), (24, 0), (0, -4), (0, 8), (100, 100) })
        {
            Bitmap dst = MakeBitmap(24, 8, dstData);
            BitBlit.Compose(dst, src, dx, dy, 0);
            Assert.Equal(BitConverter.ToString(dstData), BitConverter.ToString(dst.Data));
        }
    }
}
