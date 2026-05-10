using System;
using Jbig2Decoder.Image;
using Jbig2Decoder.Mq;

namespace Jbig2Decoder.Region
{
    /// <summary>
    /// Generic refinement region decoder (T.88 §6.3).
    ///
    /// Refines an existing reference bitmap using a 13- or 10-bit context that
    /// blends pixels from both the in-progress output and the reference,
    /// translated by (GRREFERENCEDX, GRREFERENCEDY). Used by symbol dictionaries
    /// (refinement-mode symbols) and text regions (per-instance refinements),
    /// plus standalone immediate/intermediate refinement segments.
    /// </summary>
    internal sealed class RefinementRegionDecoder
    {
        public static int StatsSizeFor(int grTemplate)
        {
            return grTemplate switch
            {
                0 => 1 << 13, // 13-bit context
                1 => 1 << 10, // 10-bit context
                _ => throw new ArgumentOutOfRangeException(nameof(grTemplate))
            };
        }

        public void Decode(RefinementRegionParams p, MqDecoder mq, byte[] grStats, Bitmap output)
        {
            if (p.TpgrOn)
            {
                DecodeWithTpgr(p, mq, grStats, output);
                return;
            }

            switch (p.GrTemplate)
            {
                case 0: DecodeTemplate0(p, mq, grStats, output); return;
                case 1: DecodeTemplate1(p, mq, grStats, output); return;
                default: throw new NotSupportedException($"GRTEMPLATE {p.GrTemplate} not implemented");
            }
        }

        // T.88 §6.3.5.6 — TPGRON: per-row typical-prediction skipping.
        //   - At row start, decode SLTP bit using a fixed stats slot
        //     (0x100 for template 0, 0x40 for template 1) and toggle LTP.
        //   - With LTP=0: decode every pixel via the normal context.
        //   - With LTP=1: for each pixel, examine the 3x3 reference neighbourhood
        //     at the reference-translated position. If all 9 pixels equal `m`,
        //     emit `m` without consulting the arithmetic coder; otherwise fall
        //     back to the normal context decode.
        private static void DecodeWithTpgr(RefinementRegionParams p, MqDecoder mq, byte[] grStats, Bitmap output)
        {
            int grw = output.Width;
            int grh = output.Height;
            Bitmap @ref = p.Reference;
            int dx = p.ReferenceDx;
            int dy = p.ReferenceDy;
            sbyte[] grat = p.Grat;
            // SLTP context value per T.88 §6.3.5.6 / Figures 14 + 15.
            // The spec's example string "GR0000001000" is template 1 with the
            // single 1-bit on the reference center pixel — so the SLTP slot
            // corresponds to "all template pixels are 0 except the reference
            // pixel at (0,0)". In OUR bit ordering (see DecodeTemplate1 below):
            //   template 0: bit 8 = ref(x-dx, y-dy) → SLTP = 0x100
            //   template 1: bit 7 = ref(x-dx, y-dy) → SLTP = 0x80
            // jbig2dec and ports of it (including our previous code) used 0x40
            // for template 1, which is bit 6 = ref(x-dx+1, y-dy) — the right
            // neighbour of the reference centre, not the centre itself. That
            // disagreement is what makes most decoders fail the Nico Weber
            // SerenityOS test fixtures (bitmap-refine-template1-tpgron etc.).
            int sltpCtx = p.GrTemplate == 0 ? 0x100 : 0x80;
            int ltp = 0;

            for (var y = 0; y < grh; y++)
            {
                int sltp = mq.Decode(ref grStats[sltpCtx]);
                ltp ^= sltp;

                for (var x = 0; x < grw; x++)
                {
                    int implicit_ = -1;
                    if (ltp != 0)
                    {
                        int i = x - dx;
                        int j = y - dy;
                        int m = @ref.GetPixel(i, j);
                        if (@ref.GetPixel(i - 1, j - 1) == m
                            && @ref.GetPixel(i,     j - 1) == m
                            && @ref.GetPixel(i + 1, j - 1) == m
                            && @ref.GetPixel(i - 1, j    ) == m
                            && @ref.GetPixel(i + 1, j    ) == m
                            && @ref.GetPixel(i - 1, j + 1) == m
                            && @ref.GetPixel(i,     j + 1) == m
                            && @ref.GetPixel(i + 1, j + 1) == m)
                        {
                            implicit_ = m;
                        }
                    }

                    if (implicit_ >= 0)
                    {
                        output.SetPixel(x, y, implicit_);
                        continue;
                    }

                    int c;
                    if (p.GrTemplate == 0)
                    {
                        c  = output.GetPixel(x - 1, y    ) << 0;
                        c |= output.GetPixel(x + 1, y - 1) << 1;
                        c |= output.GetPixel(x    , y - 1) << 2;
                        c |= output.GetPixel(x + grat[0], y + grat[1]) << 3;
                        c |= @ref.GetPixel(x - dx + 1, y - dy + 1) << 4;
                        c |= @ref.GetPixel(x - dx    , y - dy + 1) << 5;
                        c |= @ref.GetPixel(x - dx - 1, y - dy + 1) << 6;
                        c |= @ref.GetPixel(x - dx + 1, y - dy    ) << 7;
                        c |= @ref.GetPixel(x - dx    , y - dy    ) << 8;
                        c |= @ref.GetPixel(x - dx - 1, y - dy    ) << 9;
                        c |= @ref.GetPixel(x - dx + 1, y - dy - 1) << 10;
                        c |= @ref.GetPixel(x - dx    , y - dy - 1) << 11;
                        c |= @ref.GetPixel(x - dx + grat[2], y - dy + grat[3]) << 12;
                    }
                    else
                    {
                        c  = output.GetPixel(x - 1, y    ) << 0;
                        c |= output.GetPixel(x + 1, y - 1) << 1;
                        c |= output.GetPixel(x    , y - 1) << 2;
                        c |= output.GetPixel(x - 1, y - 1) << 3;
                        c |= @ref.GetPixel(x - dx + 1, y - dy + 1) << 4;
                        c |= @ref.GetPixel(x - dx    , y - dy + 1) << 5;
                        c |= @ref.GetPixel(x - dx + 1, y - dy    ) << 6;
                        c |= @ref.GetPixel(x - dx    , y - dy    ) << 7;
                        c |= @ref.GetPixel(x - dx - 1, y - dy    ) << 8;
                        c |= @ref.GetPixel(x - dx    , y - dy - 1) << 9;
                    }

                    int bit = mq.Decode(ref grStats[c]);
                    output.SetPixel(x, y, bit);
                }
            }
        }

        // T.88 §6.3.5.3 figure 12 — 13-bit context, 4 image-side + 9 reference-side pixels.
        // 4 reference-side positions are AT (grat[0..3]); the others are fixed.
        private static void DecodeTemplate0(RefinementRegionParams p, MqDecoder mq, byte[] grStats, Bitmap output)
        {
            int grw = output.Width;
            int grh = output.Height;
            Bitmap @ref = p.Reference;
            int dx = p.ReferenceDx;
            int dy = p.ReferenceDy;
            sbyte[] grat = p.Grat;

            for (var y = 0; y < grh; y++)
            {
                for (var x = 0; x < grw; x++)
                {
                    var c = 0;
                    c |= output.GetPixel(x - 1, y    ) << 0;
                    c |= output.GetPixel(x + 1, y - 1) << 1;
                    c |= output.GetPixel(x    , y - 1) << 2;
                    c |= output.GetPixel(x + grat[0], y + grat[1]) << 3;
                    c |= @ref.GetPixel(x - dx + 1, y - dy + 1) << 4;
                    c |= @ref.GetPixel(x - dx    , y - dy + 1) << 5;
                    c |= @ref.GetPixel(x - dx - 1, y - dy + 1) << 6;
                    c |= @ref.GetPixel(x - dx + 1, y - dy    ) << 7;
                    c |= @ref.GetPixel(x - dx    , y - dy    ) << 8;
                    c |= @ref.GetPixel(x - dx - 1, y - dy    ) << 9;
                    c |= @ref.GetPixel(x - dx + 1, y - dy - 1) << 10;
                    c |= @ref.GetPixel(x - dx    , y - dy - 1) << 11;
                    c |= @ref.GetPixel(x - dx + grat[2], y - dy + grat[3]) << 12;

                    int bit = mq.Decode(ref grStats[c]);
                    output.SetPixel(x, y, bit);
                }
            }
        }

        // T.88 §6.3.5.3 figure 13 — 10-bit context, 4 image-side + 6 reference-side pixels.
        // No adaptive template pixels (template 1 has fixed positions only).
        private static void DecodeTemplate1(RefinementRegionParams p, MqDecoder mq, byte[] grStats, Bitmap output)
        {
            int grw = output.Width;
            int grh = output.Height;
            Bitmap @ref = p.Reference;
            int dx = p.ReferenceDx;
            int dy = p.ReferenceDy;

            for (var y = 0; y < grh; y++)
            {
                for (var x = 0; x < grw; x++)
                {
                    var c = 0;
                    c |= output.GetPixel(x - 1, y    ) << 0;
                    c |= output.GetPixel(x + 1, y - 1) << 1;
                    c |= output.GetPixel(x    , y - 1) << 2;
                    c |= output.GetPixel(x - 1, y - 1) << 3;
                    c |= @ref.GetPixel(x - dx + 1, y - dy + 1) << 4;
                    c |= @ref.GetPixel(x - dx    , y - dy + 1) << 5;
                    c |= @ref.GetPixel(x - dx + 1, y - dy    ) << 6;
                    c |= @ref.GetPixel(x - dx    , y - dy    ) << 7;
                    c |= @ref.GetPixel(x - dx - 1, y - dy    ) << 8;
                    c |= @ref.GetPixel(x - dx    , y - dy - 1) << 9;

                    int bit = mq.Decode(ref grStats[c]);
                    output.SetPixel(x, y, bit);
                }
            }
        }
    }
}
