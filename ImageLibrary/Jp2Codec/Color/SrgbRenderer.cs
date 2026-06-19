using System;
using Wacton.Unicolour;
using Wacton.Unicolour.Icc;

namespace Jp2Codec.Color
{
    /// <summary>
    /// Optional color-management layer for <see cref="Jp2DecodeResult"/>.
    /// Produces a flat sRGB byte raster (interleaved R, G, B per pixel, row-major)
    /// from the per-component decoded samples by applying whatever colour
    /// information the JP2 wrapper carried:
    /// <list type="bullet">
    ///   <item><see cref="Jp2ColorSpace.Greyscale"/> → identity grey.</item>
    ///   <item><see cref="Jp2ColorSpace.Srgb"/> → pass-through (input is already sRGB).</item>
    ///   <item><see cref="Jp2ColorSpace.SrgbYcc"/> → digital YCbCr → sRGB via Unicolour.</item>
    ///   <item><see cref="Jp2ColorSpace.RestrictedIcc"/> /
    ///         <see cref="Jp2ColorSpace.AnyIcc"/> → Unicolour ICC profile lookup.</item>
    ///   <item><see cref="Jp2ColorSpace.Unspecified"/> → naive: 1 component as grey,
    ///         3+ as RGB pass-through.</item>
    /// </list>
    /// <para>
    /// Per-pixel conversion via Unicolour is not the fast path — a 768×512
    /// image takes a few seconds in Debug. Used for visual confirmation and
    /// for callers (e.g. PDF renderers) that don't already have their own
    /// colour pipeline.
    /// </para>
    /// </summary>
    public static class SrgbRenderer
    {
        /// <summary>
        /// Render <paramref name="result"/> to a flat sRGB byte raster of length
        /// <c>Width · Height · 3</c> (interleaved R, G, B). Throws when the
        /// declared colour space requires data the wrapper didn't carry
        /// (e.g. ICC profile bytes for an ICC-tagged image).
        /// </summary>
        public static byte[] RenderToSrgb(Jp2DecodeResult result)
        {
            if (result is null) throw new ArgumentNullException(nameof(result));

            int width = result.Width;
            int height = result.Height;
            int nc = result.NumberOfComponents;
            var output = new byte[width * height * 3];

            // Subsampled inputs (e.g. file3's 4:2:0 chroma) need their
            // components inflated to the reference grid before colour mapping —
            // each output pixel must pull from all channels at the same (x, y).
            int[][] components = NeedsUpsampling(result)
                ? ChromaUpsampler.UpsampleAll(result)
                : result.ComponentArrays;

            switch (result.ColorSpace)
            {
                case Jp2ColorSpace.Greyscale:
                    RenderGreyscale(components, result.ComponentPrecisionArray, output);
                    break;

                case Jp2ColorSpace.Srgb:
                case Jp2ColorSpace.Unspecified when nc >= 3:
                    RenderRgbPassthrough(result, components, output);
                    break;

                case Jp2ColorSpace.Unspecified when nc == 1:
                    RenderGreyscale(components, result.ComponentPrecisionArray, output);
                    break;

                case Jp2ColorSpace.SrgbYcc:
                    RenderSrgbYcc(result, components, output);
                    break;

                case Jp2ColorSpace.RestrictedIcc:
                case Jp2ColorSpace.AnyIcc:
                    if (result.IccProfile is null)
                        throw new InvalidOperationException(
                            $"ColorSpace={result.ColorSpace} but no ICC profile bytes were captured " +
                            "from the JP2 wrapper — Jp2FileParser should have populated " +
                            $"{nameof(Jp2DecodeResult)}.{nameof(Jp2DecodeResult.IccProfile)}.");
                    RenderWithIccProfile(result, components, output);
                    break;

                default:
                    throw new NotSupportedException(
                        $"sRGB rendering of color space {result.ColorSpace} with {nc} components is not implemented.");
            }
            return output;
        }

        private static bool NeedsUpsampling(Jp2DecodeResult r)
        {
            for (var c = 1; c < r.NumberOfComponents; c++)
            {
                if (r.ComponentWidth[c] != r.Width) return true;
                if (r.ComponentHeight[c] != r.Height) return true;
            }
            // Even the first component might be smaller than the reference grid
            // (degenerate but legal); check the luma too.
            if (r.NumberOfComponents > 0
                && (r.ComponentWidth[0] != r.Width || r.ComponentHeight[0] != r.Height))
                return true;
            return false;
        }

        // ---- per-strategy implementations ---------------------------------

        private static void RenderGreyscale(int[][] components, int[] precisions, byte[] output)
        {
            int[] samples = components[0];
            int precision = precisions[0];
            for (var i = 0; i < samples.Length; i++)
            {
                byte v = NormaliseToByte(samples[i], precision);
                int o = i * 3;
                output[o] = v; output[o + 1] = v; output[o + 2] = v;
            }
        }

        private static void RenderRgbPassthrough(Jp2DecodeResult result, int[][] components, byte[] output)
        {
            int rIdx = ResolveChannel(result, association: 1, fallback: 0);
            int gIdx = ResolveChannel(result, association: 2, fallback: 1);
            int bIdx = ResolveChannel(result, association: 3, fallback: 2);
            int[] r = components[rIdx];
            int[] g = components[gIdx];
            int[] b = components[bIdx];
            int pR = result.ComponentPrecision[rIdx];
            int pG = result.ComponentPrecision[gIdx];
            int pB = result.ComponentPrecision[bIdx];
            for (var i = 0; i < r.Length; i++)
            {
                int o = i * 3;
                output[o]     = NormaliseToByte(r[i], pR);
                output[o + 1] = NormaliseToByte(g[i], pG);
                output[o + 2] = NormaliseToByte(b[i], pB);
            }
        }

        // BT.601 sYCC → sRGB matrix constants from IEC 61966-2-1, matching
        // CSJ2K's <c>SYccColorSpaceMapper</c> exactly so the rendered sRGB
        // bytes are bit-identical (within float-32 arithmetic) to the
        // reference decoder. Inputs are codestream samples already level-
        // shifted by Jp2Codec; the matrix runs on centred values, then we
        // re-apply the level shift and clip per component.
        private const float SyccMatrix02 = 1.402f;
        private const float SyccMatrix11 = -0.34413f;
        private const float SyccMatrix12 = -0.71414f;
        private const float SyccMatrix21 = 1.772f;

        private static void RenderSrgbYcc(Jp2DecodeResult result, int[][] components, byte[] output)
        {
            // sYCC per IEC 61966-2-1 Annex F is BT.601 Y'CbCr with the same
            // 8-bit digital range as JPEG (Y' in [0, 255], Cb/Cr in [0, 255]
            // with 128 as the neutral point). Apply CSJ2K's matrix in float-32
            // for bit-exact agreement with the reference decoder. Honour the
            // cdef box (I.5.3.6) when present — file2.jp2's cdef maps
            // codestream component 0 → Cr (Asoc=3), 1 → Cb, 2 → Y, the reverse
            // of the natural order.
            int yIdx  = ResolveChannel(result, association: 1, fallback: 0);
            int cbIdx = ResolveChannel(result, association: 2, fallback: 1);
            int crIdx = ResolveChannel(result, association: 3, fallback: 2);
            int[] y  = components[yIdx];
            int[] cb = components[cbIdx];
            int[] cr = components[crIdx];

            int precY  = result.ComponentPrecision[yIdx];
            int precCb = result.ComponentPrecision[cbIdx];
            int precCr = result.ComponentPrecision[crIdx];
            int shiftY  = 1 << (precY - 1);
            int shiftCb = 1 << (precCb - 1);
            int shiftCr = 1 << (precCr - 1);
            int maxOut = (1 << precY) - 1;

            for (var i = 0; i < y.Length; i++)
            {
                int yc  = y[i]  - shiftY;
                int cbc = cb[i] - shiftCb;
                int crc = cr[i] - shiftCr;
                var r = (int)(yc + SyccMatrix02 * crc);
                var g = (int)(yc + SyccMatrix11 * cbc + SyccMatrix12 * crc);
                var b = (int)(yc + SyccMatrix21 * cbc);
                r += shiftY;
                g += shiftY;
                b += shiftY;
                if (r < 0) r = 0; else if (r > maxOut) r = maxOut;
                if (g < 0) g = 0; else if (g > maxOut) g = maxOut;
                if (b < 0) b = 0; else if (b > maxOut) b = maxOut;

                int o = i * 3;
                if (precY == 8)
                {
                    output[o]     = (byte)r;
                    output[o + 1] = (byte)g;
                    output[o + 2] = (byte)b;
                }
                else
                {
                    // Scale higher precision to byte for sRGB output.
                    output[o]     = (byte)((r * 255 + (maxOut >> 1)) / maxOut);
                    output[o + 1] = (byte)((g * 255 + (maxOut >> 1)) / maxOut);
                    output[o + 2] = (byte)((b * 255 + (maxOut >> 1)) / maxOut);
                }
            }
        }

        private static int ResolveChannel(Jp2DecodeResult result, int association, int fallback)
        {
            int idx = result.GetComponentForAssociation(association);
            return idx >= 0 && idx < result.NumberOfComponents ? idx : fallback;
        }

        private static void RenderWithIccProfile(Jp2DecodeResult result, int[][] components, byte[] output)
        {
            var iccConfig = new IccConfiguration(result.IccProfile!, Intent.RelativeColorimetric, "jp2-embedded");
            var config = new Configuration(iccConfig: iccConfig);

            int nc = result.NumberOfComponents;
            int totalPixels = result.Width * result.Height;

            // Monochrome 8-bit ICC fast path: there are only 256 possible input
            // values, so precompute a per-grey LUT once and lookup per pixel.
            // For file8.jp2 (700×400, monochrome ICC) this drops the render time
            // from ~600 ms to a few ms in Release.
            if (nc == 1 && result.ComponentPrecision[0] <= 8)
            {
                Span<byte> lut = stackalloc byte[256 * 3];
                BuildMonochromeIccLut(config, result.ComponentPrecision[0], lut);
                int[] samples = components[0];
                for (var i = 0; i < totalPixels; i++)
                {
                    int s = samples[i];
                    if (s < 0) s = 0;
                    else if (s > 255) s = 255;
                    int li = s * 3;
                    int o  = i * 3;
                    output[o]     = lut[li];
                    output[o + 1] = lut[li + 1];
                    output[o + 2] = lut[li + 2];
                }
                return;
            }

            // RGB / matrix-TRC ICC fast path: precompute a 33³ trilinear LUT
            // once (≈110 KB) and apply per pixel. For file5.jp2 (RGB ICC,
            // 768×512) this is ~10× faster than per-pixel Unicolour in
            // Release; for larger images the speedup grows because the LUT
            // build cost amortises over more pixels.
            if (nc == 3 && AllPrecisionsEqual(result.ComponentPrecisionArray))
            {
                byte[] lut = BuildRgbIccLut(config);
                ApplyRgbIcc3dLut(components, result.ComponentPrecisionArray, lut, output);
                return;
            }

            // Anything else (CMYK, weird channel counts): per-pixel Unicolour.
            var chanBuf = new double[nc];
            var maxValues = new double[nc];
            for (var c = 0; c < nc; c++)
                maxValues[c] = (1 << result.ComponentPrecision[c]) - 1;

            for (var i = 0; i < totalPixels; i++)
            {
                for (var c = 0; c < nc; c++)
                {
                    int sample = components[c][i];
                    if (sample < 0) sample = 0;
                    if (sample > maxValues[c]) sample = (int)maxValues[c];
                    chanBuf[c] = sample / maxValues[c];
                }
                var colour = new Unicolour(config, new Channels((double[])chanBuf.Clone()));
                Rgb255 clip = colour.Rgb.Byte255.Clipped;
                int o = i * 3;
                output[o]     = (byte)clip.R;
                output[o + 1] = (byte)clip.G;
                output[o + 2] = (byte)clip.B;
            }
        }

        private static bool AllPrecisionsEqual(int[] precisions)
        {
            for (var i = 1; i < precisions.Length; i++)
                if (precisions[i] != precisions[0]) return false;
            return true;
        }

        // ---- 3-channel ICC: 33³ sparse LUT + trilinear interpolation ------

        private const int RgbLutGrid = 33;

        private static byte[] BuildRgbIccLut(Configuration config)
        {
            // Sample the ICC at a 33×33×33 grid of normalised inputs.
            // Layout: lut[((b * G) + g) * G + r) * 3 + {R, G, B}] where G = 33.
            var lut = new byte[RgbLutGrid * RgbLutGrid * RgbLutGrid * 3];
            var chan = new double[3];
            for (var bi = 0; bi < RgbLutGrid; bi++)
            {
                chan[2] = bi / (double)(RgbLutGrid - 1);
                for (var gi = 0; gi < RgbLutGrid; gi++)
                {
                    chan[1] = gi / (double)(RgbLutGrid - 1);
                    for (var ri = 0; ri < RgbLutGrid; ri++)
                    {
                        chan[0] = ri / (double)(RgbLutGrid - 1);
                        var colour = new Unicolour(config, new Channels((double[])chan.Clone()));
                        Rgb255 clip = colour.Rgb.Byte255.Clipped;
                        int idx = ((bi * RgbLutGrid + gi) * RgbLutGrid + ri) * 3;
                        lut[idx]     = (byte)clip.R;
                        lut[idx + 1] = (byte)clip.G;
                        lut[idx + 2] = (byte)clip.B;
                    }
                }
            }
            return lut;
        }

        private static void ApplyRgbIcc3dLut(int[][] components, int[] precisions, byte[] lut, byte[] output)
        {
            int gridMax = RgbLutGrid - 1;
            double m0 = (1 << precisions[0]) - 1;
            double m1 = (1 << precisions[1]) - 1;
            double m2 = (1 << precisions[2]) - 1;
            int n = components[0].Length;
            int[] c0 = components[0];
            int[] c1 = components[1];
            int[] c2 = components[2];

            for (var p = 0; p < n; p++)
            {
                double t0 = c0[p] / m0 * gridMax;
                double t1 = c1[p] / m1 * gridMax;
                double t2 = c2[p] / m2 * gridMax;
                if (t0 < 0) t0 = 0; else if (t0 > gridMax) t0 = gridMax;
                if (t1 < 0) t1 = 0; else if (t1 > gridMax) t1 = gridMax;
                if (t2 < 0) t2 = 0; else if (t2 > gridMax) t2 = gridMax;
                var i0 = (int)t0; if (i0 >= gridMax) i0 = gridMax - 1;
                var i1 = (int)t1; if (i1 >= gridMax) i1 = gridMax - 1;
                var i2 = (int)t2; if (i2 >= gridMax) i2 = gridMax - 1;
                double f0 = t0 - i0;
                double f1 = t1 - i1;
                double f2 = t2 - i2;

                // 8 corner indices (each * 3 for RGB triplet).
                int b00 = ((i2 * RgbLutGrid + i1) * RgbLutGrid + i0) * 3;
                int b01 = b00 + 3;                   // (i0+1, i1,   i2)
                int b10 = b00 + RgbLutGrid * 3;               // (i0,   i1+1, i2)
                int b11 = b10 + 3;
                int b100 = b00 + RgbLutGrid * RgbLutGrid * 3;          // (i0,   i1,   i2+1)
                int b101 = b100 + 3;
                int b110 = b100 + RgbLutGrid * 3;
                int b111 = b110 + 3;

                // Trilinear interpolation per channel.
                int ro = p * 3;
                output[ro]     = Trilerp(lut[b00],     lut[b01],     lut[b10],     lut[b11],
                                          lut[b100],    lut[b101],    lut[b110],    lut[b111], f0, f1, f2);
                output[ro + 1] = Trilerp(lut[b00 + 1], lut[b01 + 1], lut[b10 + 1], lut[b11 + 1],
                                          lut[b100 + 1],lut[b101 + 1],lut[b110 + 1],lut[b111 + 1], f0, f1, f2);
                output[ro + 2] = Trilerp(lut[b00 + 2], lut[b01 + 2], lut[b10 + 2], lut[b11 + 2],
                                          lut[b100 + 2],lut[b101 + 2],lut[b110 + 2],lut[b111 + 2], f0, f1, f2);
            }
        }

        private static byte Trilerp(byte c000, byte c100, byte c010, byte c110,
                                    byte c001, byte c101, byte c011, byte c111,
                                    double fx, double fy, double fz)
        {
            // Standard trilinear interpolation, byte-quantised result.
            double c00 = c000 + (c100 - c000) * fx;
            double c01 = c001 + (c101 - c001) * fx;
            double c10 = c010 + (c110 - c010) * fx;
            double c11 = c011 + (c111 - c011) * fx;
            double c0 = c00 + (c10 - c00) * fy;
            double c1 = c01 + (c11 - c01) * fy;
            double v = c0 + (c1 - c0) * fz;
            if (v < 0) return 0;
            if (v > 255) return 255;
            return (byte)(v + 0.5);
        }

        private static void BuildMonochromeIccLut(Configuration config, int precision, Span<byte> lut)
        {
            int max = (1 << precision) - 1;
            var chanBuf = new double[1];
            for (var v = 0; v < 256; v++)
            {
                int clamped = v > max ? max : v;
                chanBuf[0] = clamped / (double)max;
                var colour = new Unicolour(config, new Channels((double[])chanBuf.Clone()));
                Rgb255 clip = colour.Rgb.Byte255.Clipped;
                int li = v * 3;
                lut[li]     = (byte)clip.R;
                lut[li + 1] = (byte)clip.G;
                lut[li + 2] = (byte)clip.B;
            }
        }

        // ---- helpers -------------------------------------------------------

        private static byte NormaliseToByte(int sample, int precision)
        {
            if (precision == 8)
            {
                if (sample < 0) return 0;
                if (sample > 255) return 255;
                return (byte)sample;
            }
            // Higher / lower precisions — linear scale into [0, 255].
            int max = (1 << precision) - 1;
            if (sample < 0) sample = 0;
            if (sample > max) sample = max;
            return (byte)((sample * 255 + (max >> 1)) / max);
        }
    }
}
