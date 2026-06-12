using System;
using System.IO;
using Jp2Codec.Color;

namespace Jp2Codec
{
    /// <summary>
    /// High-level byte[]→byte[] convenience wrapper around <see cref="Jp2StreamDecoder"/>.
    /// Intended for callers (PDF filter pipelines, image-loading utilities)
    /// that want a flat raster of 8-bit samples rather than the per-component
    /// integer arrays exposed by <see cref="Jp2DecodeResult"/>.
    /// </summary>
    public static class Jpeg2000
    {
        /// <summary>
        /// Decompress JPEG 2000 data to raw image bytes.
        /// <para>
        /// When the JP2 wrapper declares a colour space (sRGB, sYCC, monochrome,
        /// or ICC-tagged) the output is rendered through <see cref="SrgbRenderer"/>
        /// — sYCC matrix, ICC profile lookup, and chroma upsampling are all
        /// applied so the caller receives a flat sRGB raster (1 byte per pixel
        /// for greyscale, 3 bytes per pixel for everything else).
        /// </para>
        /// <para>
        /// For raw J2K codestreams (<see cref="Jp2ColorSpace.Unspecified"/>)
        /// the per-component samples are returned in sample-interleaved order
        /// scaled to 8-bit. The caller is responsible for interpreting them
        /// via whatever metadata accompanies the codestream (PDF
        /// <c>/ColorSpace</c> dictionary, etc.).
        /// </para>
        /// </summary>
        /// <param name="data">JPEG 2000 encoded data (J2K or JP2 format).</param>
        /// <param name="width">Output image width on the reference grid.</param>
        /// <param name="height">Output image height on the reference grid.</param>
        /// <param name="components">
        /// Number of colour components in the returned raster. 1 for greyscale
        /// (managed or raw single-component); 3 for any managed colour image
        /// rendered through <see cref="SrgbRenderer"/>; the codestream's
        /// native component count for raw / <see cref="Jp2ColorSpace.Unspecified"/>
        /// streams.
        /// </param>
        /// <returns>Sample-interleaved 8-bit raster in row-major order.</returns>
        public static byte[] Decompress(byte[] data, out int width, out int height, out int components)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));

            if (data.Length == 0)
            {
                width = 0;
                height = 0;
                components = 0;
                return Array.Empty<byte>();
            }

            try
            {
                Jp2DecodeResult result = new Jp2StreamDecoder().Decode(data);

                width = result.Width;
                height = result.Height;

                // Managed colour spaces — let SrgbRenderer handle upsampling, sYCC
                // conversion, and ICC mapping so PDF consumers don't replicate it.
                switch (result.ColorSpace)
                {
                    case Jp2ColorSpace.Srgb:
                    case Jp2ColorSpace.SrgbYcc:
                    case Jp2ColorSpace.RestrictedIcc:
                    case Jp2ColorSpace.AnyIcc:
                        components = 3;
                        return SrgbRenderer.RenderToSrgb(result);

                    case Jp2ColorSpace.Greyscale:
                        components = 1;
                        return RenderGreyscale(result);
                }

                // Unspecified (raw .j2c, or JP2 with no colr box). Hand back raw
                // component samples — the caller's surrounding metadata is what
                // tells the consumer how to interpret them.
                components = result.NumberOfComponents;
                return InterleaveRawComponents(result);
            }
            catch (InvalidDataException)
            {
                // Already the canonical "corrupt / malformed input" signal — pass through.
                throw;
            }
            catch (Exception ex) when (ex is ArgumentException
                                          or IndexOutOfRangeException
                                          or OverflowException
                                          or FormatException
                                          or EndOfStreamException
                                          or InvalidOperationException)
            {
                // A malformed or hostile codestream drove an internal invariant past a
                // low-level guard (e.g. a negative subband shift reaching CoordMath, a
                // truncated packet header, an out-of-range marker length). Normalise it
                // to the same InvalidDataException contract every other parse failure
                // already uses, so callers can treat "bad JPEG 2000 input" uniformly
                // instead of having raw framework exceptions leak through. The fuzzer /
                // crash-test corpus (gdal_fuzzer_*, *.SIGSEGV.*, issue*-null-image-size)
                // exercises exactly these paths — they must reject cleanly, not throw raw.
                throw new InvalidDataException($"Malformed JPEG 2000 data: {ex.Message}", ex);
            }
        }

        private static byte[] RenderGreyscale(Jp2DecodeResult result)
        {
            int width = result.Width;
            int height = result.Height;
            var output = new byte[width * height];

            ReadOnlySpan<int> samples = result.GetComponentSpan(0);
            int cw = result.ComponentWidth[0];
            int ch = result.ComponentHeight[0];
            int precision = result.ComponentPrecision[0];
            bool needsUpsample = cw != width || ch != height;

            for (var y = 0; y < height; y++)
            {
                int sy = needsUpsample ? y * ch / height : y;
                int rowOff = sy * cw;
                for (var x = 0; x < width; x++)
                {
                    int sx = needsUpsample ? x * cw / width : x;
                    output[y * width + x] = ScaleToByte(samples[rowOff + sx], precision);
                }
            }
            return output;
        }

        private static byte[] InterleaveRawComponents(Jp2DecodeResult result)
        {
            int width = result.Width;
            int height = result.Height;
            int components = result.NumberOfComponents;
            var output = new byte[width * height * components];

            for (var c = 0; c < components; c++)
            {
                ReadOnlySpan<int> samples = result.GetComponentSpan(c);
                int cw = result.ComponentWidth[c];
                int ch = result.ComponentHeight[c];
                int precision = result.ComponentPrecision[c];
                bool needsUpsample = cw != width || ch != height;

                for (var y = 0; y < height; y++)
                {
                    int sy = needsUpsample ? y * ch / height : y;
                    int rowOff = sy * cw;
                    for (var x = 0; x < width; x++)
                    {
                        int sx = needsUpsample ? x * cw / width : x;
                        output[(y * width + x) * components + c] = ScaleToByte(samples[rowOff + sx], precision);
                    }
                }
            }
            return output;
        }

        private static byte ScaleToByte(int value, int precision)
        {
            if (precision == 8)
            {
                if (value < 0) return 0;
                if (value > 255) return 255;
                return (byte)value;
            }

            int max = (1 << precision) - 1;
            if (value < 0) value = 0;
            else if (value > max) value = max;
            return (byte)(value * 255 / max);
        }
    }
}
