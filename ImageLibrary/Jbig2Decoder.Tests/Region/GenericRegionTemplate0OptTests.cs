using Jbig2Decoder.Image;
using Jbig2Decoder.Mq;
using Jbig2Decoder.Region;

namespace Jbig2Decoder.Tests.Region;

/// <summary>
/// Proves the AT-folded fast path (<c>DecodeTemplate0RowOpt</c>) is byte-for-byte
/// identical to the general per-pixel context decoder for nominal template-0 AT
/// pixels. The oracle is the decoder's own USESKIP path with an all-zero skip
/// mask — that drives the fully general <c>ContextFor</c> per-pixel context
/// formation (four AT GetPixel calls), so it is an independent implementation of
/// the same context. Both decode the same arithmetic byte stream from the same
/// zeroed context stats; since the MQ decoder is deterministic, any single
/// context-index mismatch perturbs the adaptive state and cascades into a
/// divergent bitmap, which this test would catch.
/// </summary>
public class GenericRegionTemplate0OptTests
{
    private static readonly sbyte[] NominalAt = [3, -1, -3, -1, 2, -2, -2, -2];

    private static byte[] Decode(int w, int h, byte[] arith, bool viaPerPixelReference)
    {
        var mq = new MqDecoder(arith, 0, arith.Length);
        var output = new Bitmap(w, h);
        var gbStats = new byte[GenericRegionDecoder.StatsSizeFor(0)];
        var p = new GenericRegionParams
        {
            GbTemplate = 0,
            Gbat = (sbyte[])NominalAt.Clone(),
        };
        if (viaPerPixelReference)
        {
            // All-zero skip mask => nothing is actually skipped => every pixel goes
            // through ContextFor (the general 4-AT-GetPixel context). This is the
            // reference the fast path must match.
            p.UseSkip = true;
            p.Skip = new Bitmap(w, h);
        }

        new GenericRegionDecoder().Decode(p, mq, gbStats, output);
        return output.Data;
    }

    [Fact]
    public void Opt_matches_per_pixel_reference_across_random_streams()
    {
        var rnd = new Random(0x0DC0DE);
        for (var t = 0; t < 300; t++)
        {
            int w = rnd.Next(1, 90);
            int h = rnd.Next(1, 50);
            var arith = new byte[rnd.Next(16, 256)];
            rnd.NextBytes(arith);

            byte[] opt = Decode(w, h, arith, viaPerPixelReference: false);
            byte[] reference = Decode(w, h, arith, viaPerPixelReference: true);

            if (!opt.AsSpan().SequenceEqual(reference))
                Assert.Fail($"divergence t={t} w={w} h={h} alen={arith.Length}\n" +
                            $"  opt={BitConverter.ToString(opt)}\n  ref={BitConverter.ToString(reference)}");
        }
    }

    [Theory]
    // Widths straddling the byte boundary and the windowed-refill threshold, where
    // the AT columns (x+3 / x-3 / x+2 / x-2) clip against the left/right edges.
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(64)]
    public void Opt_matches_reference_at_edge_widths(int w)
    {
        var rnd = new Random(0xED6E + w);
        for (var h = 1; h <= 6; h++)
        {
            var arith = new byte[128];
            rnd.NextBytes(arith);
            byte[] opt = Decode(w, h, arith, viaPerPixelReference: false);
            byte[] reference = Decode(w, h, arith, viaPerPixelReference: true);
            Assert.Equal(BitConverter.ToString(reference), BitConverter.ToString(opt));
        }
    }
}
