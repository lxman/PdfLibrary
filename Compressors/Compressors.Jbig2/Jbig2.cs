using System;
using JBig2Decoder.NETStandard;

namespace Compressors.Jbig2
{
    /// <summary>
    /// Provides JBIG2 decompression for PDF streams.
    /// Note: Encoding is not supported - use CCITT Group 4 for creating bi-level images.
    /// </summary>
    public static class Jbig2
    {
        /// <summary>
        /// Decodes JBIG2 compressed data.
        /// </summary>
        /// <param name="data">The JBIG2 compressed data.</param>
        /// <param name="width">Output: the image width in pixels.</param>
        /// <param name="height">Output: the image height in pixels.</param>
        /// <returns>Decoded RGB pixel data.</returns>
        public static byte[] Decompress(byte[]? data, out int width, out int height)
        {
            return Decompress(data, null, out width, out height);
        }

        /// <summary>
        /// Decodes JBIG2 compressed data with a global symbols dictionary.
        /// Used when PDF specifies /JBIG2Globals.
        /// </summary>
        /// <param name="data">The JBIG2 compressed data.</param>
        /// <param name="globals">The global symbols dictionary data.</param>
        /// <param name="width">Output: the image width in pixels.</param>
        /// <param name="height">Output: the image height in pixels.</param>
        /// <returns>Decoded RGB pixel data.</returns>
        public static byte[] Decompress(byte[]? data, byte[]? globals, out int width, out int height)
        {
            width = 0;
            height = 0;

            // Validate input
            if (data == null || data.Length == 0)
            {
                return [];
            }

            try
            {
                var decoder = new JBIG2StreamDecoder
                {
                    // Enable tolerance mode to match Chrome/Acrobat behavior
                    // This allows decoding JBIG2 streams with forward references
                    // (invalid per ITU-T T.88 but common in real-world PDFs)
                    TolerateMissingSegments = true
                };

                // Set globals if provided (JBIG2Globals stream from PDF)
                if (globals != null && globals.Length > 0)
                {
                    decoder.SetGlobalData(globals);
                }

                // The DecodeJBIG2 method can throw NullReferenceException on malformed data
                // Catch all exceptions to prevent crashes
                byte[] rgb = decoder.DecodeJBIG2(data, out width, out height);

                // Validate output
                if (rgb == null || rgb.Length == 0)
                {
                    width = 0;
                    height = 0;
                    return [];
                }

                // Validate dimensions
                if (width <= 0 || height <= 0)
                {
                    width = 0;
                    height = 0;
                    return [];
                }

                // Validate data length
                int expectedLength = width * height * 3;
                if (rgb.Length == expectedLength) return rgb;
                width = 0;
                height = 0;
                return [];

            }
            catch (NullReferenceException)
            {
                // Common error from the decoder library when data is malformed
                width = 0;
                height = 0;
                return [];
            }
            catch (IndexOutOfRangeException)
            {
                // Another common error with malformed JBIG2 data
                width = 0;
                height = 0;
                return [];
            }
            catch (Exception)
            {
                // Catch any other exceptions from the decoder
                width = 0;
                height = 0;
                return [];
            }
        }

        /// <summary>
        /// Decodes JBIG2 compressed data to a 1-bit-per-pixel bitmap.
        /// </summary>
        /// <param name="data">The JBIG2 compressed data.</param>
        /// <param name="width">Output: the image width in pixels.</param>
        /// <param name="height">Output: the image height in pixels.</param>
        /// <returns>Decoded bitmap data (1 bit per pixel, packed into bytes, MSB first).</returns>
        public static byte[] DecompressToBitmap(byte[] data, out int width, out int height)
        {
            return DecompressToBitmap(data, null, out width, out height);
        }

        /// <summary>
        /// Decodes JBIG2 compressed data to a 1-bit-per-pixel bitmap.
        /// </summary>
        /// <param name="data">The JBIG2 compressed data.</param>
        /// <param name="globals">The global symbols dictionary data (optional).</param>
        /// <param name="width">Output: the image width in pixels.</param>
        /// <param name="height">Output: the image height in pixels.</param>
        /// <returns>Decoded bitmap data (1 bit per pixel, packed into bytes, MSB first).</returns>
        public static byte[] DecompressToBitmap(byte[]? data, byte[]? globals, out int width, out int height)
        {
            if (data == null || data.Length == 0)
            {
                width = 0;
                height = 0;
                return [];
            }

            byte[] rgb = Decompress(data, globals, out width, out height);

            if (rgb.Length == 0)
            {
                return [];
            }

            // Convert RGB to 1-bit bitmap
            int bytesPerRow = (width + 7) / 8;
            var bitmap = new byte[bytesPerRow * height];

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    int rgbIndex = (y * width + x) * 3;

                    // Check if pixel is black (R=0, G=0, B=0)
                    bool isBlack = rgbIndex + 2 < rgb.Length &&
                                   rgb[rgbIndex] == 0 &&
                                   rgb[rgbIndex + 1] == 0 &&
                                   rgb[rgbIndex + 2] == 0;

                    if (!isBlack) continue;
                    int byteIndex = y * bytesPerRow + x / 8;
                    int bitIndex = 7 - (x % 8);
                    bitmap[byteIndex] |= (byte)(1 << bitIndex);
                }
            }

            return bitmap;
        }
    }
}
