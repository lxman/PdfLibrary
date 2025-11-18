using System.IO;
using System.IO.Compression;

namespace FontParser.Tables.WOFF2.Brotli
{
    public static class BrotliUtility
    {
        public static byte[] Decompress(byte[] data)
        {
            using var inputStream = new MemoryStream(data);
            using var outputStream = new MemoryStream();
            using var decompressStream = new BrotliStream(inputStream, CompressionMode.Decompress);
            decompressStream.CopyTo(outputStream);
            return outputStream.ToArray();
        }

        public static byte[] Compress(byte[] data)
        {
            using var memoryStream = new MemoryStream();
            using var brotliStream = new BrotliStream(memoryStream, CompressionLevel.Optimal);
            brotliStream.Write(data, 0, data.Length);
            return memoryStream.ToArray();
        }
    }
}