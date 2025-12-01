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

            // DEBUG: Save raw JP2 data for testing with CoreJ2K.Skia
            if (data.Length > 10000) // Only for larger images
            {
                try
                {
                    string jp2Path = Path.Combine(Path.GetTempPath(), "debug_raw_jp2_data.jp2");
                    File.WriteAllBytes(jp2Path, data);
                    Console.WriteLine($"[JP2 DEBUG] Saved raw JP2 data ({data.Length} bytes) to {jp2Path}");
                }
                catch { }
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

            // Debug: Find min/max values for each component to check bit depth
            int expectedPixelCount = width * height;
            Console.WriteLine($"[JP2 DEBUG] Expected pixel count: {expectedPixelCount} ({width}x{height})");

            for (var c = 0; c < components; c++)
            {
                int min = int.MaxValue, max = int.MinValue;
                foreach (var v in componentData[c])
                {
                    if (v < min) min = v;
                    if (v > max) max = v;
                }
                bool lengthMatch = componentData[c].Length == expectedPixelCount;
                Console.WriteLine($"[JP2 DEBUG] Component {c}: min={min}, max={max}, count={componentData[c].Length}, expected={expectedPixelCount}, MATCH={lengthMatch}");
            }

            // DEBUG: Check component stride by examining how data is organized
            // For a 650x650 image, pixel at (0,1) should be at index 650
            // Sample pixels to verify row organization
            if (width == 650 && height == 650 && components == 3)
            {
                Console.WriteLine($"[JP2 DEBUG] Checking data organization for 650x650 image:");
                // Top-left corner (0,0)
                Console.WriteLine($"[JP2 DEBUG] Pixel (0,0): R={componentData[0][0]}, G={componentData[1][0]}, B={componentData[2][0]}");
                // One pixel right (1,0)
                Console.WriteLine($"[JP2 DEBUG] Pixel (1,0): R={componentData[0][1]}, G={componentData[1][1]}, B={componentData[2][1]}");
                // Second row start (0,1)
                Console.WriteLine($"[JP2 DEBUG] Pixel (0,1): R={componentData[0][650]}, G={componentData[1][650]}, B={componentData[2][650]}");
                // Check pixel at x=433 (where corruption starts visually)
                Console.WriteLine($"[JP2 DEBUG] Pixel (433,0): R={componentData[0][433]}, G={componentData[1][433]}, B={componentData[2][433]}");
                // Center of image
                int centerIdx = 325 * 650 + 325;
                Console.WriteLine($"[JP2 DEBUG] Pixel (325,325): R={componentData[0][centerIdx]}, G={componentData[1][centerIdx]}, B={componentData[2][centerIdx]}");
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

            // DEBUG: For 650x650 image, save raw RGB data and also create a bitmap
            if (width == 650 && height == 650 && components == 3)
            {
                try
                {
                    // Save raw RGB bytes to file for hex inspection
                    string rawPath = Path.Combine(Path.GetTempPath(), "debug_jp2_raw_rgb.bin");
                    File.WriteAllBytes(rawPath, result);
                    Console.WriteLine($"[JP2 DEBUG] Saved raw RGB data ({result.Length} bytes) to {rawPath}");

                    // Sample some pixels from the interleaved result to verify
                    // Pixel at (433, 0) = index 433, offset = 433 * 3 = 1299
                    int p433_0 = 433 * 3;
                    Console.WriteLine($"[JP2 RESULT] Pixel (433,0) from result: R={result[p433_0]}, G={result[p433_0+1]}, B={result[p433_0+2]}");
                    // Pixel at (433, 100) = index (100*650 + 433) = 65433, offset = 65433 * 3 = 196299
                    int p433_100 = (100 * 650 + 433) * 3;
                    Console.WriteLine($"[JP2 RESULT] Pixel (433,100) from result: R={result[p433_100]}, G={result[p433_100+1]}, B={result[p433_100+2]}");
                    // Pixel at (649, 0) = last pixel in first row
                    int p649_0 = 649 * 3;
                    Console.WriteLine($"[JP2 RESULT] Pixel (649,0) from result: R={result[p649_0]}, G={result[p649_0+1]}, B={result[p649_0+2]}");
                    // Pixel at (0, 649) = first pixel in last row
                    int p0_649 = 649 * 650 * 3;
                    Console.WriteLine($"[JP2 RESULT] Pixel (0,649) from result: R={result[p0_649]}, G={result[p0_649+1]}, B={result[p0_649+2]}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[JP2 DEBUG ERROR] {ex.Message}");
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
