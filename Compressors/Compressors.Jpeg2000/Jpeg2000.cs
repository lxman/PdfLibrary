using System;
using System.IO;
using CoreJ2K;
using CoreJ2K.Util;

namespace Compressors.Jpeg2000
{
    /// <summary>
    /// JPEG 2000 compression and decompression using CoreJ2K
    /// </summary>
    public static class Jpeg2000
    {
        /// <summary>
        /// Decompress JPEG 2000 data to raw image bytes
        /// </summary>
        /// <param name="data">JPEG 2000 encoded data</param>
        /// <param name="width">Output image width</param>
        /// <param name="height">Output image height</param>
        /// <param name="components">Number of color components (1=gray, 3=RGB, 4=RGBA)</param>
        /// <returns>Raw image bytes in component-interleaved format</returns>
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

            // Decode JPEG 2000 data
            using var inputStream = new MemoryStream(data);
            PortableImage portableImage = J2kImage.FromStream(inputStream);

            width = portableImage.Width;
            height = portableImage.Height;
            components = portableImage.NumberOfComponents;

            // Extract raw bytes from components
            int pixelCount = width * height;
            var result = new byte[pixelCount * components];

            // Get each component and interleave into the result
            var componentData = new int[components][];
            for (var c = 0; c < components; c++)
            {
                componentData[c] = portableImage.GetComponent(c);
            }

            // Interleave component data into byte array
            var offset = 0;
            for (var i = 0; i < pixelCount; i++)
            {
                for (var c = 0; c < components; c++)
                {
                    // Clamp to byte range (component values are int)
                    int value = componentData[c][i];
                    result[offset++] = (byte)Math.Clamp(value, 0, 255);
                }
            }

            return result;
        }

        /// <summary>
        /// Compress raw image bytes to JPEG 2000 format
        /// </summary>
        /// <param name="data">Raw image bytes in component-interleaved format</param>
        /// <param name="width">Image width</param>
        /// <param name="height">Image height</param>
        /// <param name="components">Number of color components (1=gray, 3=RGB, 4=RGBA)</param>
        /// <returns>JPEG 2000 encoded data</returns>
        public static byte[] Compress(byte[] data, int width, int height, int components)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));

            if (data.Length == 0)
                return Array.Empty<byte>();

            if (width <= 0 || height <= 0 || components <= 0)
                throw new ArgumentException("Width, height, and components must be positive.");

            int expectedLength = width * height * components;
            if (data.Length < expectedLength)
                throw new ArgumentException($"Data length ({data.Length}) is less than expected ({expectedLength}).");

            // Create an image source from raw data
            var imageReader = new RawImageReader(data, width, height, components);

            // Encode to JPEG 2000
            return J2kImage.ToBytes(imageReader);
        }
    }
}
