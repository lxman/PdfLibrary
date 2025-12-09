using System;
using System.IO;
using Xunit;

namespace Compressors.Jbig2.Tests
{
    public class Jbig2DecoderTests
    {
        [Fact]
        public void Decompress_EmptyData_ReturnsEmptyArray()
        {
            byte[] result = Jbig2.Decompress([], out int width, out int height);

            Assert.Empty(result);
            Assert.Equal(0, width);
            Assert.Equal(0, height);
        }

        [Fact]
        public void Decompress_NullData_ReturnsEmptyArray()
        {
            byte[] result = Jbig2.Decompress(null!, out int width, out int height);

            Assert.Empty(result);
            Assert.Equal(0, width);
            Assert.Equal(0, height);
        }

        [Fact]
        public void DecompressToBitmap_EmptyData_ReturnsEmptyArray()
        {
            byte[] result = Jbig2.DecompressToBitmap([], out int width, out int height);

            Assert.Empty(result);
            Assert.Equal(0, width);
            Assert.Equal(0, height);
        }

        [Fact]
        public void Decompress_WithGlobals_EmptyData_ReturnsEmptyArray()
        {
            var globals = new byte[] { 0x00, 0x01, 0x02 };
            byte[] result = Jbig2.Decompress([], globals, out int width, out int height);

            Assert.Empty(result);
            Assert.Equal(0, width);
            Assert.Equal(0, height);
        }

        /// <summary>
        /// Tests decoding real-world JBIG2 data extracted from PDF (object 1964).
        /// This is a 603x696 image without globals.
        /// </summary>
        [Fact]
        public void Decompress_RealWorld_Object1964_DecodesSuccessfully()
        {
            // Load test data
            string testDataPath = Path.Combine("TestData", "jbig2_1964.jb2");
            byte[] data = File.ReadAllBytes(testDataPath);

            // Decode - should not crash
            byte[] result = Jbig2.Decompress(data, out int width, out int height);

            // Verify dimensions
            Assert.Equal(603, width);
            Assert.Equal(696, height);

            // Verify we got RGB data
            Assert.NotEmpty(result);
            Assert.Equal(603 * 696 * 3, result.Length);
        }

        /// <summary>
        /// Tests decoding real-world JBIG2 data extracted from PDF (object 2005).
        /// This is a small 583x707 image (102 bytes compressed).
        /// </summary>
        [Fact]
        public void Decompress_RealWorld_Object2005_DecodesSuccessfully()
        {
            string testDataPath = Path.Combine("TestData", "jbig2_2005.jb2");
            byte[] data = File.ReadAllBytes(testDataPath);

            byte[] result = Jbig2.Decompress(data, out int width, out int height);

            Assert.Equal(583, width);
            Assert.Equal(707, height);
            Assert.NotEmpty(result);
            Assert.Equal(583 * 707 * 3, result.Length);
        }

        /// <summary>
        /// CRITICAL TEST: Object 2006 previously crashed with IndexOutOfRangeException
        /// in tolerance mode due to missing segments. This test verifies the fix in
        /// TextRegionSegment.cs that reads ALL symbol code lengths from the bitstream
        /// instead of stopping at noOfSymbols.
        ///
        /// This is a 532x622 image with JBIG2Globals reference.
        /// </summary>
        [Fact]
        public void Decompress_RealWorld_Object2006_WithGlobals_DoesNotCrash()
        {
            // Load image data and globals
            string dataPath = Path.Combine("TestData", "jbig2_2006.jb2");
            string globalsPath = Path.Combine("TestData", "jbig2_2007_globals.jb2");

            byte[] data = File.ReadAllBytes(dataPath);
            byte[] globals = File.ReadAllBytes(globalsPath);

            // This previously crashed with IndexOutOfRangeException
            // The fix reads all symbol code lengths from bitstream, not just noOfSymbols
            byte[] result = Jbig2.Decompress(data, globals, out int width, out int height);

            // If we get here without exception, the fix works!
            Assert.Equal(532, width);
            Assert.Equal(622, height);
            Assert.NotEmpty(result);
            Assert.Equal(532 * 622 * 3, result.Length);
        }

        /// <summary>
        /// Tests decoding real-world JBIG2 data extracted from PDF (object 2042).
        /// This is a 513x722 image without globals.
        /// </summary>
        [Fact]
        public void Decompress_RealWorld_Object2042_DecodesSuccessfully()
        {
            string testDataPath = Path.Combine("TestData", "jbig2_2042.jb2");
            byte[] data = File.ReadAllBytes(testDataPath);

            byte[] result = Jbig2.Decompress(data, out int width, out int height);

            Assert.Equal(513, width);
            Assert.Equal(722, height);
            Assert.NotEmpty(result);
            Assert.Equal(513 * 722 * 3, result.Length);
        }

        /// <summary>
        /// Tests decoding another real-world JBIG2 with globals (object 2043).
        /// This is a 473x472 image that references globals object 2044.
        /// </summary>
        [Fact]
        public void Decompress_RealWorld_Object2043_WithGlobals_DecodesSuccessfully()
        {
            string dataPath = Path.Combine("TestData", "jbig2_2043.jb2");
            string globalsPath = Path.Combine("TestData", "jbig2_2044_globals.jb2");

            byte[] data = File.ReadAllBytes(dataPath);
            byte[] globals = File.ReadAllBytes(globalsPath);

            byte[] result = Jbig2.Decompress(data, globals, out int width, out int height);

            Assert.Equal(473, width);
            Assert.Equal(472, height);
            Assert.NotEmpty(result);
            Assert.Equal(473 * 472 * 3, result.Length);
        }
    }
}
