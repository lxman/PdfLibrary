using System;
using Xunit;

namespace Compressors.Jbig2.Tests
{
    public class Jbig2DecoderTests
    {
        [Fact]
        public void Decompress_EmptyData_ReturnsEmptyArray()
        {
            var result = Jbig2.Decompress(Array.Empty<byte>(), out int width, out int height);

            Assert.Empty(result);
            Assert.Equal(0, width);
            Assert.Equal(0, height);
        }

        [Fact]
        public void Decompress_NullData_ReturnsEmptyArray()
        {
            var result = Jbig2.Decompress(null!, out int width, out int height);

            Assert.Empty(result);
            Assert.Equal(0, width);
            Assert.Equal(0, height);
        }

        [Fact]
        public void DecompressToBitmap_EmptyData_ReturnsEmptyArray()
        {
            var result = Jbig2.DecompressToBitmap(Array.Empty<byte>(), out int width, out int height);

            Assert.Empty(result);
            Assert.Equal(0, width);
            Assert.Equal(0, height);
        }

        [Fact]
        public void Decompress_WithGlobals_EmptyData_ReturnsEmptyArray()
        {
            var globals = new byte[] { 0x00, 0x01, 0x02 };
            var result = Jbig2.Decompress(Array.Empty<byte>(), globals, out int width, out int height);

            Assert.Empty(result);
            Assert.Equal(0, width);
            Assert.Equal(0, height);
        }
    }
}
