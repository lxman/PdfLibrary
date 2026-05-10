using System;
using Jbig2Decoder.Image;
using Jbig2Decoder.Mq;

namespace Jbig2Decoder.Region
{
    /// <summary>
    /// Decoder for generic region segments (T.88 §6.2 / Annex G arithmetic mode).
    ///
    /// Uses an MQ decoder + a context array (GB_stats) sized to the active template,
    /// and writes a 1-bit bitmap. Currently supports template 0 only; templates 1-3
    /// and TPGDON come in subsequent commits.
    /// </summary>
    internal sealed class GenericRegionDecoder
    {
        public static int StatsSizeFor(int gbTemplate)
        {
            // T.88 §6.2.5.3: number of context bits per template.
            return gbTemplate switch
            {
                0 => 1 << 16,
                1 => 1 << 13,
                2 => 1 << 10,
                3 => 1 << 10,
                _ => throw new ArgumentOutOfRangeException(nameof(gbTemplate))
            };
        }

        /// <summary>
        /// Decode a generic region into <paramref name="output"/>.
        /// <paramref name="gbStats"/> must have <see cref="StatsSizeFor"/> entries.
        /// </summary>
        public void Decode(GenericRegionParams p, MqDecoder mq, byte[] gbStats, Bitmap output)
        {
            if (p.Mmr) throw new NotSupportedException("MMR generic regions handled by the CCITT path");

            if (p.UseSkip)
            {
                if (p.Skip is null)
                    throw new InvalidOperationException("UseSkip set but Skip bitmap is null");
                DecodeWithSkip(p, mq, gbStats, output);
                return;
            }

            if (p.TpgdOn)
            {
                DecodeWithTpgdOn(p, mq, gbStats, output);
                return;
            }

            switch (p.GbTemplate)
            {
                case 0: DecodeTemplate0(p, mq, gbStats, output); return;
                case 1: DecodeTemplate1(p, mq, gbStats, output); return;
                case 2: DecodeTemplate2(p, mq, gbStats, output); return;
                case 3: DecodeTemplate3(p, mq, gbStats, output); return;
                default:
                    throw new NotSupportedException($"GBTEMPLATE {p.GbTemplate} not implemented");
            }
        }

        // T.88 §6.2.5.3 USESKIP path — per-pixel, no row optimisations. Used by
        // halftone bitplane decode when HENABLESKIP is set: skipped grid cells
        // aren't in the arith bitstream, decoder writes 0 there. The MQ decoder
        // is shared across all bitplanes of the gray-scale image, so we must
        // not consume any bits at skipped positions.
        private static void DecodeWithSkip(GenericRegionParams p, MqDecoder mq, byte[] gbStats, Bitmap output)
        {
            int gbw = output.Width;
            int gbh = output.Height;
            sbyte[] gbat = p.Gbat;
            Bitmap skip = p.Skip!;

            for (int y = 0; y < gbh; y++)
            {
                for (int x = 0; x < gbw; x++)
                {
                    if (skip.GetPixel(x, y) != 0)
                    {
                        // Skipped: decoder writes 0, no MQ bits consumed.
                        output.SetPixel(x, y, 0);
                        continue;
                    }

                    int context = ContextFor(p.GbTemplate, gbat, output, x, y);
                    int bit = mq.Decode(ref gbStats[context]);
                    output.SetPixel(x, y, bit);
                }
            }
        }

        // Context computation per template (T.88 §6.2.5 + Tables 6/7/8/9).
        // Slow path — used by the skip-aware decoder. Optimised non-skip
        // decoders inline this with row-level prefetching.
        private static int ContextFor(int gbTemplate, sbyte[] gbat, Bitmap output, int x, int y)
        {
            switch (gbTemplate)
            {
                case 0:
                {
                    int c = 0;
                    c |= output.GetPixel(x - 1, y) << 0;
                    c |= output.GetPixel(x - 2, y) << 1;
                    c |= output.GetPixel(x - 3, y) << 2;
                    c |= output.GetPixel(x - 4, y) << 3;
                    c |= output.GetPixel(x + gbat[0], y + gbat[1]) << 4;
                    c |= output.GetPixel(x + 2, y - 1) << 5;
                    c |= output.GetPixel(x + 1, y - 1) << 6;
                    c |= output.GetPixel(x, y - 1) << 7;
                    c |= output.GetPixel(x - 1, y - 1) << 8;
                    c |= output.GetPixel(x - 2, y - 1) << 9;
                    c |= output.GetPixel(x + gbat[2], y + gbat[3]) << 10;
                    c |= output.GetPixel(x + gbat[4], y + gbat[5]) << 11;
                    c |= output.GetPixel(x + 1, y - 2) << 12;
                    c |= output.GetPixel(x, y - 2) << 13;
                    c |= output.GetPixel(x - 1, y - 2) << 14;
                    c |= output.GetPixel(x + gbat[6], y + gbat[7]) << 15;
                    return c;
                }
                case 1:
                {
                    int c = 0;
                    c |= output.GetPixel(x - 1, y) << 0;
                    c |= output.GetPixel(x - 2, y) << 1;
                    c |= output.GetPixel(x - 3, y) << 2;
                    c |= output.GetPixel(x + gbat[0], y + gbat[1]) << 3;
                    c |= output.GetPixel(x + 2, y - 1) << 4;
                    c |= output.GetPixel(x + 1, y - 1) << 5;
                    c |= output.GetPixel(x, y - 1) << 6;
                    c |= output.GetPixel(x - 1, y - 1) << 7;
                    c |= output.GetPixel(x - 2, y - 1) << 8;
                    c |= output.GetPixel(x + 2, y - 2) << 9;
                    c |= output.GetPixel(x + 1, y - 2) << 10;
                    c |= output.GetPixel(x, y - 2) << 11;
                    c |= output.GetPixel(x - 1, y - 2) << 12;
                    return c;
                }
                case 2:
                {
                    int c = 0;
                    c |= output.GetPixel(x - 1, y) << 0;
                    c |= output.GetPixel(x - 2, y) << 1;
                    c |= output.GetPixel(x + gbat[0], y + gbat[1]) << 2;
                    c |= output.GetPixel(x + 1, y - 1) << 3;
                    c |= output.GetPixel(x, y - 1) << 4;
                    c |= output.GetPixel(x - 1, y - 1) << 5;
                    c |= output.GetPixel(x - 2, y - 1) << 6;
                    c |= output.GetPixel(x + 1, y - 2) << 7;
                    c |= output.GetPixel(x, y - 2) << 8;
                    c |= output.GetPixel(x - 1, y - 2) << 9;
                    return c;
                }
                case 3:
                {
                    int c = 0;
                    c |= output.GetPixel(x - 1, y) << 0;
                    c |= output.GetPixel(x - 2, y) << 1;
                    c |= output.GetPixel(x - 3, y) << 2;
                    c |= output.GetPixel(x - 4, y) << 3;
                    c |= output.GetPixel(x + gbat[0], y + gbat[1]) << 4;
                    c |= output.GetPixel(x + 1, y - 1) << 5;
                    c |= output.GetPixel(x, y - 1) << 6;
                    c |= output.GetPixel(x - 1, y - 1) << 7;
                    c |= output.GetPixel(x - 2, y - 1) << 8;
                    c |= output.GetPixel(x - 3, y - 1) << 9;
                    return c;
                }
                default:
                    throw new NotSupportedException($"GBTEMPLATE {gbTemplate} not implemented");
            }
        }

        /// <summary>
        /// TPGDON (typical prediction for generic direct) wrapper.
        /// For each row, first decode an SLTP bit using a template-specific fixed
        /// context. If it flips LTP on, the row is identical to the previous row
        /// (or all zeros for row 0). Otherwise, decode the row normally.
        /// </summary>
        private static void DecodeWithTpgdOn(GenericRegionParams p, MqDecoder mq, byte[] gbStats, Bitmap output)
        {
            // SLTP context indices per T.88 §6.2.5.7.
            int sltpCtx = p.GbTemplate switch
            {
                0 => 0x9B25,
                1 => 0x0795,
                2 => 0x00E5,
                3 => 0x0195,
                _ => throw new NotSupportedException($"GBTEMPLATE {p.GbTemplate} not implemented"),
            };

            int gbw = output.Width;
            int gbh = output.Height;
            int stride = output.Stride;
            var ltp = 0;

            for (var y = 0; y < gbh; y++)
            {
                int sltp = mq.Decode(ref gbStats[sltpCtx]);
                ltp ^= sltp;

                if (ltp != 0)
                {
                    // Row is a copy of the previous row (or all zeros for the first row).
                    int dst = y * stride;
                    if (y == 0)
                    {
                        for (var b = 0; b < stride; b++) output.Data[dst + b] = 0;
                    }
                    else
                    {
                        int src = (y - 1) * stride;
                        Buffer.BlockCopy(output.Data, src, output.Data, dst, stride);
                    }
                }
                else
                {
                    // Decode this single row using the template's normal path.
                    DecodeSingleRow(p, mq, gbStats, output, y);
                }
            }
        }

        private static void DecodeSingleRow(GenericRegionParams p, MqDecoder mq, byte[] gbStats, Bitmap output, int y)
        {
            switch (p.GbTemplate)
            {
                case 0: DecodeTemplate0Row(p, mq, gbStats, output, y); return;
                case 1: DecodeTemplate1Row(p, mq, gbStats, output, y); return;
                case 2: DecodeTemplate2Row(p, mq, gbStats, output, y); return;
                case 3: DecodeTemplate3Row(p, mq, gbStats, output, y); return;
                default: throw new NotSupportedException($"GBTEMPLATE {p.GbTemplate} not implemented");
            }
        }

        // Port of jbig2_decode_generic_template1_unopt — 13-bit context, 1 AT pixel,
        // 5 m1 + 4 m2 surround pixels, 3 bits from current row.
        private static void DecodeTemplate1(GenericRegionParams p, MqDecoder mq, byte[] gbStats, Bitmap output)
        {
            for (var y = 0; y < output.Height; y++)
                DecodeTemplate1Row(p, mq, gbStats, output, y);
        }

        private static void DecodeTemplate1Row(GenericRegionParams p, MqDecoder mq, byte[] gbStats, Bitmap output, int y)
        {
            int gbw = output.Width;
            sbyte[] gbat = p.Gbat;

            var outByte = 0;
            var outBitsToGo = 8;
            int dst = y * output.Stride;
            var dstByte = 0;

            uint pd = 0, ppd = 0;
            var plineByteIdx = 2;
            bool hasPline = y >= 1;
            bool hasPpline = y >= 2;

            if (hasPline)
            {
                int basePline = (y - 1) * output.Stride;
                pd = (uint)output.Data[basePline] << 8;
                if (gbw > 8) pd |= output.Data[basePline + 1];
            }
            if (hasPpline)
            {
                int basePpline = (y - 2) * output.Stride;
                ppd = (uint)output.Data[basePpline] << 8;
                if (gbw > 8) ppd |= output.Data[basePpline + 1];
            }

            for (var x = 0; x < gbw; x++)
            {
                int context = outByte & 0x0007;
                context |= output.GetPixel(x + gbat[0], y + gbat[1]) << 3;
                context |= (int)((pd >> 9) & 0x01F0);
                context |= (int)((ppd >> 4) & 0x1E00);

                int bit = mq.Decode(ref gbStats[context]);

                pd <<= 1;
                ppd <<= 1;
                outByte = (outByte << 1) | bit;
                outBitsToGo--;
                output.Data[dst + dstByte] = (byte)(outByte << outBitsToGo);

                if (outBitsToGo == 0)
                {
                    outBitsToGo = 8;
                    dstByte++;

                    if (x + 9 < gbw && hasPline)
                    {
                        int basePline = (y - 1) * output.Stride;
                        if (plineByteIdx < output.Stride)
                            pd |= output.Data[basePline + plineByteIdx];
                        if (hasPpline)
                        {
                            int basePpline = (y - 2) * output.Stride;
                            if (plineByteIdx < output.Stride)
                                ppd |= output.Data[basePpline + plineByteIdx];
                        }
                        plineByteIdx++;
                    }
                }
            }
        }

        // Port of jbig2_decode_generic_template2_unopt — 10-bit context, 1 AT pixel,
        // 4 m1 + 3 m2 surround pixels, 2 bits from current row.
        private static void DecodeTemplate2(GenericRegionParams p, MqDecoder mq, byte[] gbStats, Bitmap output)
        {
            for (var y = 0; y < output.Height; y++)
                DecodeTemplate2Row(p, mq, gbStats, output, y);
        }

        private static void DecodeTemplate2Row(GenericRegionParams p, MqDecoder mq, byte[] gbStats, Bitmap output, int y)
        {
            int gbw = output.Width;
            sbyte[] gbat = p.Gbat;

            var outByte = 0;
            var outBitsToGo = 8;
            int dst = y * output.Stride;
            var dstByte = 0;

            uint pd = 0, ppd = 0;
            var plineByteIdx = 2;
            bool hasPline = y >= 1;
            bool hasPpline = y >= 2;

            if (hasPline)
            {
                int basePline = (y - 1) * output.Stride;
                pd = (uint)output.Data[basePline] << 8;
                if (gbw > 8) pd |= output.Data[basePline + 1];
            }
            if (hasPpline)
            {
                int basePpline = (y - 2) * output.Stride;
                ppd = (uint)output.Data[basePpline] << 8;
                if (gbw > 8) ppd |= output.Data[basePpline + 1];
            }

            for (var x = 0; x < gbw; x++)
            {
                int context = outByte & 0x003;
                context |= output.GetPixel(x + gbat[0], y + gbat[1]) << 2;
                context |= (int)((pd >> 11) & 0x078);
                context |= (int)((ppd >> 7) & 0x380);

                int bit = mq.Decode(ref gbStats[context]);

                pd <<= 1;
                ppd <<= 1;
                outByte = (outByte << 1) | bit;
                outBitsToGo--;
                output.Data[dst + dstByte] = (byte)(outByte << outBitsToGo);

                if (outBitsToGo == 0)
                {
                    outBitsToGo = 8;
                    dstByte++;

                    if (x + 9 < gbw && hasPline)
                    {
                        int basePline = (y - 1) * output.Stride;
                        if (plineByteIdx < output.Stride)
                            pd |= output.Data[basePline + plineByteIdx];
                        if (hasPpline)
                        {
                            int basePpline = (y - 2) * output.Stride;
                            if (plineByteIdx < output.Stride)
                                ppd |= output.Data[basePpline + plineByteIdx];
                        }
                        plineByteIdx++;
                    }
                }
            }
        }

        // Port of jbig2_decode_generic_template3_unopt — 10-bit context, single-row reach
        // (no m2): 1 AT pixel, 5 m1 surround pixels, 4 bits from current row.
        private static void DecodeTemplate3(GenericRegionParams p, MqDecoder mq, byte[] gbStats, Bitmap output)
        {
            for (var y = 0; y < output.Height; y++)
                DecodeTemplate3Row(p, mq, gbStats, output, y);
        }

        private static void DecodeTemplate3Row(GenericRegionParams p, MqDecoder mq, byte[] gbStats, Bitmap output, int y)
        {
            int gbw = output.Width;
            sbyte[] gbat = p.Gbat;

            var outByte = 0;
            var outBitsToGo = 8;
            int dst = y * output.Stride;
            var dstByte = 0;

            uint pd = 0;
            var plineByteIdx = 2;
            bool hasPline = y >= 1;

            if (hasPline)
            {
                int basePline = (y - 1) * output.Stride;
                pd = (uint)output.Data[basePline] << 8;
                if (gbw > 8) pd |= output.Data[basePline + 1];
            }

            for (var x = 0; x < gbw; x++)
            {
                int context = outByte & 0x00F;
                context |= output.GetPixel(x + gbat[0], y + gbat[1]) << 4;
                context |= (int)((pd >> 9) & 0x3E0);

                int bit = mq.Decode(ref gbStats[context]);

                pd <<= 1;
                outByte = (outByte << 1) | bit;
                outBitsToGo--;
                output.Data[dst + dstByte] = (byte)(outByte << outBitsToGo);

                if (outBitsToGo == 0)
                {
                    outBitsToGo = 8;
                    dstByte++;

                    if (x + 9 < gbw && hasPline)
                    {
                        int basePline = (y - 1) * output.Stride;
                        if (plineByteIdx < output.Stride)
                            pd |= output.Data[basePline + plineByteIdx];
                        plineByteIdx++;
                    }
                }
            }
        }

        // Port of jbig2_decode_generic_template0_unopt — the general (non-AT-fixed) path.
        private static void DecodeTemplate0(GenericRegionParams p, MqDecoder mq, byte[] gbStats, Bitmap output)
        {
            for (var y = 0; y < output.Height; y++)
                DecodeTemplate0Row(p, mq, gbStats, output, y);
        }

        private static void DecodeTemplate0Row(GenericRegionParams p, MqDecoder mq, byte[] gbStats, Bitmap output, int y)
        {
            int gbw = output.Width;
            sbyte[] gbat = p.Gbat;

            var outByte = 0;
            var outBitsToGo = 8;
            int dst = y * output.Stride;
            var dstByte = 0;

            uint pd = 0, ppd = 0;
            var plineByteIdx = 2;
            bool hasPline = y >= 1;
            bool hasPpline = y >= 2;

            if (hasPline)
            {
                int basePline = (y - 1) * output.Stride;
                pd = (uint)output.Data[basePline] << 8;
                if (gbw > 8) pd |= output.Data[basePline + 1];
            }
            if (hasPpline)
            {
                int basePpline = (y - 2) * output.Stride;
                ppd = (uint)output.Data[basePpline] << 8;
                if (gbw > 8) ppd |= output.Data[basePpline + 1];
            }

            for (var x = 0; x < gbw; x++)
            {
                int context = outByte & 0x000F;
                context |= output.GetPixel(x + gbat[0], y + gbat[1]) << 4;
                context |= (int)((pd >> 8) & 0x03E0);
                context |= output.GetPixel(x + gbat[2], y + gbat[3]) << 10;
                context |= output.GetPixel(x + gbat[4], y + gbat[5]) << 11;
                context |= (int)((ppd >> 2) & 0x7000);
                context |= output.GetPixel(x + gbat[6], y + gbat[7]) << 15;

                int bit = mq.Decode(ref gbStats[context]);

                pd <<= 1;
                ppd <<= 1;
                outByte = (outByte << 1) | bit;
                outBitsToGo--;
                output.Data[dst + dstByte] = (byte)(outByte << outBitsToGo);

                if (outBitsToGo == 0)
                {
                    outBitsToGo = 8;
                    dstByte++;

                    if (x + 9 < gbw && hasPline)
                    {
                        int basePline = (y - 1) * output.Stride;
                        if (plineByteIdx < output.Stride)
                            pd |= output.Data[basePline + plineByteIdx];
                        if (hasPpline)
                        {
                            int basePpline = (y - 2) * output.Stride;
                            if (plineByteIdx < output.Stride)
                                ppd |= output.Data[basePpline + plineByteIdx];
                        }
                        plineByteIdx++;
                    }
                }
            }
        }
    }
}
