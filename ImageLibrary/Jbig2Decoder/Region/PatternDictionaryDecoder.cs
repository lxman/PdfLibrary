using System;
using CcittCodec;
using Jbig2Decoder.Image;
using Jbig2Decoder.Mq;

namespace Jbig2Decoder.Region
{
    /// <summary>
    /// Pattern dictionary decoder (T.88 §6.7).
    ///
    /// Decodes a single collective bitmap of size <c>HDPW * (GRAYMAX + 1)</c> wide
    /// by <c>HDPH</c> tall using either the arithmetic generic-region decoder (with
    /// fixed AT pixels per §6.7.5) or MMR Group 4. Then slices the bitmap column-
    /// wise into the individual <c>GRAYMAX + 1</c> patterns.
    /// </summary>
    internal sealed class PatternDictionaryDecoder
    {
        public PatternDictionary Decode(PatternDictionaryParams p, byte[] data, int offset, int length)
        {
            if (p.HdPw <= 0)
                throw new InvalidOperationException("HDPW must be positive");
            if (p.HdPh <= 0)
                throw new InvalidOperationException("HDPH must be positive");

            uint n = p.GrayMax + 1;
            if (n == 0 || n > int.MaxValue / (uint)p.HdPw)
                throw new InvalidOperationException("Pattern count overflow");

            int totalWidth = p.HdPw * (int)n;
            var collective = new Bitmap(totalWidth, p.HdPh);

            if (p.HdMmr)
            {
                // MMR collective bitmap — Group 4 with EOFB.
                var slice = new byte[length];
                Buffer.BlockCopy(data, offset, slice, 0, length);
                var mmr = new CcittDecoder(new CcittOptions
                {
                    Group = CcittGroup.Group4,
                    K = -1,
                    Width = totalWidth,
                    Height = p.HdPh,
                    BlackIs1 = true,
                    EndOfBlock = true,
                });
                byte[] decoded = mmr.Decode(slice);
                int needed = collective.Data.Length;
                if (decoded.Length < needed)
                    throw new InvalidOperationException(
                        $"Pattern dictionary MMR produced {decoded.Length} bytes, expected {needed} ({totalWidth}x{p.HdPh})");
                Buffer.BlockCopy(decoded, 0, collective.Data, 0, needed);
            }
            else
            {
                // §6.7.5 fixes the AT pixels independent of HDTEMPLATE.
                var gbat = new sbyte[8]
                {
                    (sbyte)-p.HdPw, 0,
                    -3, -1,
                    2, -2,
                    -2, -2,
                };

                var rp = new GenericRegionParams
                {
                    GbTemplate = p.HdTemplate,
                    TpgdOn = false,
                    UseSkip = false,
                    Gbat = gbat,
                };
                var mq = new MqDecoder(data, offset, length);
                var stats = new byte[GenericRegionDecoder.StatsSizeFor(p.HdTemplate)];
                new GenericRegionDecoder().Decode(rp, mq, stats, collective);
            }

            // Slice into per-pattern bitmaps. Pattern i sits at column i*HDPW of the
            // collective bitmap, width HDPW, height HDPH.
            var patterns = new Bitmap[n];
            for (uint i = 0; i < n; i++)
            {
                var pat = new Bitmap(p.HdPw, p.HdPh);
                int xCursor = (int)i * p.HdPw;
                for (var y = 0; y < p.HdPh; y++)
                    for (var x = 0; x < p.HdPw; x++)
                        pat.SetPixel(x, y, collective.GetPixel(xCursor + x, y));
                patterns[i] = pat;
            }

            return new PatternDictionary(patterns, p.HdPw, p.HdPh);
        }
    }
}
