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
            if (data == null || data.Length == 0)
            {
                width = 0;
                height = 0;
                return [];
            }

            var decoder = new JBIG2StreamDecoder();
            return decoder.DecodeJBIG2(data, out width, out height);
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
            if (data == null || data.Length == 0)
            {
                width = 0;
                height = 0;
                return [];
            }

            var decoder = new JBIG2StreamDecoder();

            if (globals != null && globals.Length > 0)
            {
                decoder.SetGlobalData(globals);
            }

            return decoder.DecodeJBIG2(data, out width, out height);
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
        public static byte[] DecompressToBitmap(byte[] data, byte[]? globals, out int width, out int height)
        {
            byte[] rgb = Decompress(data, globals, out width, out height);

            if (rgb.Length == 0)
            {
                return [];
            }

            // Convert RGB to 1-bit bitmap
            // Assume RGB data is 3 bytes per pixel
            int pixelCount = width * height;
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
