using System;
using Jbig2Decoder.Image;
using Jbig2Decoder.Mq;
using Jbig2Decoder.Stream;

namespace Jbig2Decoder
{
    /// <summary>
    /// Decoder for JBIG2 bitstreams as defined by ITU-T T.88 / ISO/IEC 14492.
    ///
    /// Public surface mirrors JBig2Decoder.NETStandard so this assembly can be
    /// dropped into existing consumers (e.g. PDF /JBIG2Decode filter wrappers)
    /// with only a namespace change.
    /// </summary>
    public class JBIG2StreamDecoder
    {
        private byte[]? _globalData;

        /// <summary>
        /// When true, the decoder attempts to recover from malformed streams
        /// (e.g. forward references, missing segments) commonly emitted by
        /// non-strict producers. Mirrors Chrome / Acrobat behaviour.
        /// </summary>
        public bool TolerateMissingSegments { get; set; }

        /// <summary>
        /// Provide the global symbol/Huffman segment data (PDF /JBIG2Globals stream).
        /// Must be called before <see cref="DecodeJBIG2"/> if the main bitstream
        /// references global segments.
        /// </summary>
        public void SetGlobalData(byte[] globals)
        {
            _globalData = globals ?? throw new ArgumentNullException(nameof(globals));
        }

        /// <summary>
        /// Open a side-by-side MQ trace at the given path. Every subsequent
        /// MQ-decoder operation (across the entire process, until
        /// <see cref="CloseMqTrace"/>) appends a line capturing the pre-decode
        /// context byte, the decoded bit, and the post-decode A/C/CT register
        /// state. Diff against a matching trace from jbig2dec to localise an
        /// MQ-state desync to a specific decode index.
        /// </summary>
        public static void OpenMqTrace(string path) => MqTrace.Open(path);

        /// <summary>Closes the trace opened by <see cref="OpenMqTrace"/>.</summary>
        public static void CloseMqTrace() => MqTrace.Close();

        /// <summary>
        /// Decode a JBIG2 bitstream to a packed 1-bit-per-pixel bitmap (rows
        /// stride-aligned to (width+7)/8 bytes, MSB-first, 1 = black). Skips
        /// the RGB expansion that <see cref="DecodeJBIG2"/> performs — for a
        /// 3300×4700 page that's ~46 MB of avoided allocation+writeback when
        /// the consumer was going to repack to 1 bit anyway (e.g. PDF's
        /// JBIG2Decode filter, which inverts the result for DeviceGray).
        /// </summary>
        public byte[] DecodeJBIG2ToPacked(byte[] data, out int width, out int height)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            _ = TolerateMissingSegments;

            var reader = new Jbig2StreamReader(data, _globalData);
            Bitmap page = reader.Decode();
            width = page.Width;
            height = page.Height;
            return page.Data;
        }

        /// <summary>
        /// Decode a JBIG2 bitstream to RGB pixel data.
        /// </summary>
        /// <param name="data">JBIG2 bitstream (sequential or random-access organisation).</param>
        /// <param name="width">Resulting image width in pixels.</param>
        /// <param name="height">Resulting image height in pixels.</param>
        /// <returns>RGB pixel buffer, length = width × height × 3. Black pixels are (0,0,0); white pixels are (255,255,255).</returns>
        public byte[] DecodeJBIG2(byte[] data, out int width, out int height)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            _ = TolerateMissingSegments;

            var reader = new Jbig2StreamReader(data, _globalData);
            Bitmap page = reader.Decode();

            width = page.Width;
            height = page.Height;

            // The wrapper contract (matching JBig2Decoder.NETStandard) returns RGB:
            // black pixels -> (0,0,0), white pixels -> (255,255,255).
            var rgb = new byte[width * height * 3];
            var outIdx = 0;
            for (var y = 0; y < height; y++)
            {
                int rowBase = y * page.Stride;
                for (var x = 0; x < width; x++)
                {
                    int bit = (page.Data[rowBase + (x >> 3)] >> (7 - (x & 7))) & 1;
                    byte v = bit == 1 ? (byte)0 : (byte)255;
                    rgb[outIdx++] = v;
                    rgb[outIdx++] = v;
                    rgb[outIdx++] = v;
                }
            }
            return rgb;
        }
    }
}
