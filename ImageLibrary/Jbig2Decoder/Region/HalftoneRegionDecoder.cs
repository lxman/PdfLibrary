using System;
using CcittCodec;
using Jbig2Decoder.Image;
using Jbig2Decoder.Mq;

namespace Jbig2Decoder.Region
{
    /// <summary>
    /// Halftone region decoder (T.88 §6.6 + Annex C.5).
    ///
    /// Decodes a HGW × HGH grid of gray values via Gray-coded bitplanes (the
    /// highest-order plane is decoded first, then each lower plane is XOR-folded
    /// against the next higher plane to recover the binary value), then composites
    /// the indexed pattern from a referenced pattern dictionary onto the region
    /// at each grid cell using the 8.8-fixed-point grid origin (HGX, HGY) and
    /// vector (HRX, HRY).
    /// </summary>
    internal sealed class HalftoneRegionDecoder
    {
        public void Decode(HalftoneRegionParams p, byte[] data, int offset, int length, Bitmap output)
        {
            if (p.Patterns is null)
                throw new InvalidOperationException("Halftone region requires a pattern dictionary");
            int hNumPats = p.Patterns.Patterns.Length;
            if (hNumPats < 1)
                throw new InvalidOperationException("Pattern dictionary contains no patterns");

            // 6.6.5 step 1: fill output with HDEFPIXEL.
            byte fill = p.HDefPixel == 1 ? (byte)0xFF : (byte)0x00;
            if (fill != 0)
            {
                for (var i = 0; i < output.Data.Length; i++)
                    output.Data[i] = fill;
            }

            var hgw = (int)p.Hgw;
            var hgh = (int)p.Hgh;
            if (hgw <= 0 || hgh <= 0) return;

            // 6.6.5 step 2: HSKIP map (only when HENABLESKIP = 1). One bit per cell:
            // skip the cell when its composited pattern would lie entirely outside
            // the region bounds (so the gray-scale decoder pre-fills it as 0).
            Bitmap? hskip = null;
            int hpw = p.Patterns.PatternWidth;
            int hph = p.Patterns.PatternHeight;
            if (p.HEnableSkip)
            {
                hskip = new Bitmap(hgw, hgh);
                for (var mg = 0; mg < hgh; mg++)
                {
                    for (var ng = 0; ng < hgw; ng++)
                    {
                        long x = (p.Hgx + (long)mg * p.Hry + (long)ng * p.Hrx) >> 8;
                        long y = (p.Hgy + (long)mg * p.Hrx - (long)ng * p.Hry) >> 8;
                        bool offEdge = x + hpw <= 0 || x >= output.Width || y + hph <= 0 || y >= output.Height;
                        hskip.SetPixel(ng, mg, offEdge ? 1 : 0);
                    }
                }
            }

            // 6.6.5 step 3: HBPP = ceil(log2(HNUMPATS)).
            var hbpp = 0;
            while (hNumPats > 1 << ++hbpp) { /* loop body intentionally empty */ }
            if (hbpp > 16)
                throw new InvalidOperationException($"HBPP {hbpp} > 16 not supported");

            // 6.6.5 step 4: decode the gray-scale values.
            int[,] gsvals = DecodeGrayScaleImage(p, data, offset, length, hgw, hgh, hbpp, hskip);

            // 6.6.5 step 5: place patterns.
            for (var mg = 0; mg < hgh; mg++)
            {
                for (var ng = 0; ng < hgw; ng++)
                {
                    long x = (p.Hgx + (long)mg * p.Hry + (long)ng * p.Hrx) >> 8;
                    long y = (p.Hgy + (long)mg * p.Hrx - (long)ng * p.Hry) >> 8;

                    int grayVal = gsvals[ng, mg];
                    if (grayVal >= hNumPats) grayVal = hNumPats - 1;
                    if (grayVal < 0) grayVal = 0;

                    Bitmap pat = p.Patterns.Patterns[grayVal];
                    Composite(output, pat, (int)x, (int)y, p.HCombOp);
                }
            }
        }

        // Annex C.5 — gray-scale image decode.
        // Returns GSVALS[ng, mg] in [0, 2^hbpp).
        private static int[,] DecodeGrayScaleImage(
            HalftoneRegionParams p, byte[] data, int offset, int length,
            int gsw, int gsh, int gsbpp, Bitmap? gskip)
        {
            // Per Annex C.5, the GBAT for the bitplane generic regions is:
            //   GBAT[0]=(GSTEMPLATE<=1?3:2), GBAT[1]=-1
            //   GBAT[2]=-3, GBAT[3]=-1, GBAT[4]=2, GBAT[5]=-2, GBAT[6]=-2, GBAT[7]=-2
            var gbat = new sbyte[8]
            {
                (sbyte)(p.HTemplate <= 1 ? 3 : 2), -1,
                -3, -1,
                2, -2,
                -2, -2,
            };

            var planes = new Bitmap[gsbpp];
            for (var i = 0; i < gsbpp; i++)
                planes[i] = new Bitmap(gsw, gsh);

            if (p.HMmr)
            {
                // C.5 step 1 + 3a (MMR variant): each plane is its own EOFB-terminated
                // Group-4 stream. Track bytes consumed per plane to advance through
                // the segment data correctly.
                int cursor = offset;
                int remaining = length;
                for (int j = gsbpp - 1; j >= 0; j--)
                {
                    var slice = new byte[remaining];
                    Buffer.BlockCopy(data, cursor, slice, 0, remaining);
                    var dec = new CcittDecoder(new CcittOptions
                    {
                        Group = CcittGroup.Group4,
                        K = -1,
                        Width = gsw,
                        Height = gsh,
                        BlackIs1 = true,
                        EndOfBlock = true,
                    });
                    int consumed;
                    byte[] decoded = dec.DecodeWithConsumed(slice, out consumed);
                    int needed = planes[j].Data.Length;
                    if (decoded.Length < needed)
                        throw new InvalidOperationException(
                            $"Halftone MMR plane {j} produced {decoded.Length} bytes, expected {needed}");
                    Buffer.BlockCopy(decoded, 0, planes[j].Data, 0, needed);

                    // C.5 step 3b: XOR with the next-higher plane (Gray decode).
                    if (j < gsbpp - 1)
                    {
                        byte[] hi = planes[j + 1].Data;
                        byte[] lo = planes[j].Data;
                        for (var i = 0; i < lo.Length; i++) lo[i] ^= hi[i];
                    }

                    if (consumed > remaining) consumed = remaining;
                    cursor += consumed;
                    remaining -= consumed;
                }
            }
            else
            {
                // C.5 (arithmetic): all planes share one MQ decoder and one stats
                // array. The stats are zero-initialised once and persist across
                // every bitplane decode. When HENABLESKIP=1 the skip mask must
                // flow through to the bitplane decoder — the encoder doesn't
                // emit bits for skipped grid cells, so omitting the skip mask
                // would consume bits meant for later planes and desync.
                var mq = new MqDecoder(data, offset, length);
                var stats = new byte[GenericRegionDecoder.StatsSizeFor(p.HTemplate)];
                var gen = new GenericRegionDecoder();
                var rp = new GenericRegionParams
                {
                    GbTemplate = p.HTemplate,
                    TpgdOn = false,
                    UseSkip = gskip != null,
                    Skip = gskip,
                    Gbat = gbat,
                };

                // C.5 step 1: decode plane GSBPP-1.
                gen.Decode(rp, mq, stats, planes[gsbpp - 1]);

                // C.5 step 3: decode each lower plane and XOR with the higher plane.
                for (int j = gsbpp - 2; j >= 0; j--)
                {
                    gen.Decode(rp, mq, stats, planes[j]);
                    byte[] hi = planes[j + 1].Data;
                    byte[] lo = planes[j].Data;
                    for (var i = 0; i < lo.Length; i++) lo[i] ^= hi[i];
                }
            }

            // C.5 step 4: combine planes into gray-scale values.
            var vals = new int[gsw, gsh];
            for (var x = 0; x < gsw; x++)
            {
                for (var y = 0; y < gsh; y++)
                {
                    var v = 0;
                    for (var j = 0; j < gsbpp; j++)
                        v |= planes[j].GetPixel(x, y) << j;
                    vals[x, y] = v;
                }
            }
            return vals;
        }

        // 6.6.5 step 5 — pixelwise composite of `pat` onto `dst` at (dx, dy)
        // using JBIG2 combine operator (T.88 §7.4 Table 5).
        private static void Composite(Bitmap dst, Bitmap pat, int dx, int dy, int op)
        {
            int w = pat.Width;
            int h = pat.Height;
            for (var py = 0; py < h; py++)
            {
                int oy = dy + py;
                if ((uint)oy >= (uint)dst.Height) continue;
                for (var px = 0; px < w; px++)
                {
                    int ox = dx + px;
                    if ((uint)ox >= (uint)dst.Width) continue;

                    int s = pat.GetPixel(px, py);
                    int d = dst.GetPixel(ox, oy);
                    int n = op switch
                    {
                        0 => s | d,
                        1 => s & d,
                        2 => s ^ d,
                        3 => 1 - (s ^ d),
                        4 => s,
                        _ => s | d,
                    };
                    dst.SetPixel(ox, oy, n);
                }
            }
        }
    }
}
