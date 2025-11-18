using System.IO;
using System.IO.Compression;

namespace FontParser
{
    public static class Gzipper
    {
        public static bool IsCompressed(this byte[] data)
        {
            return data.Length >= 2 && data[0] == 0x1F && data[1] == 0x8B;
        }

        public static byte[] Compress(this byte[] data)
        {
            using var memoryStream = new MemoryStream();
            using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress))
            {
                gzipStream.Write(data, 0, data.Length);
            }

            return memoryStream.ToArray();
        }

        public static byte[] Decompress(this byte[] data)
        {
            using var memoryStream = new MemoryStream(data);
            using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress);
            var decompressedData = new MemoryStream();
            gzipStream.CopyTo(decompressedData);
            return decompressedData.ToArray();
        }
    }
}