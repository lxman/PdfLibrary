using System;
using System.IO;
using System.IO.Compression;

namespace FontParser.Tables.Woff.Zlib
{
    public static class ZlibUtility
    {
        // https://yal.cc/cs-deflatestream-zlib/
        public static byte[] Deflate(byte[] data, CompressionLevel? level = null)
        {
            byte[] newData;
            using (var memStream = new MemoryStream())
            {
                // write header:
                memStream.WriteByte(0x78);
                switch (level)
                {
                    case CompressionLevel.NoCompression:
                        memStream.WriteByte(0x01);
                        break;

                    case CompressionLevel.Fastest:
                        memStream.WriteByte(0x5E);
                        break;

                    case CompressionLevel.Optimal:
                        memStream.WriteByte(0xDA);
                        break;

                    default:
                        memStream.WriteByte(0x9C);
                        break;
                }
                // write compressed data (with Deflate headers):
                using (DeflateStream dflStream = level.HasValue
                           ? new DeflateStream(memStream, level.Value)
                           : new DeflateStream(memStream, CompressionMode.Compress
                           )) dflStream.Write(data, 0, data.Length);
                //
                newData = memStream.ToArray();
            }
            // compute Adler-32:
            uint a1 = 1, a2 = 0;
            foreach (byte b in data)
            {
                a1 = (a1 + b) % 65521;
                a2 = (a2 + a1) % 65521;
            }
            // append the checksum-trailer:
            int adlerPos = newData.Length;
            Array.Resize(ref newData, adlerPos + 4);
            newData[adlerPos] = (byte)(a2 >> 8);
            newData[adlerPos + 1] = (byte)a2;
            newData[adlerPos + 2] = (byte)(a1 >> 8);
            newData[adlerPos + 3] = (byte)a1;
            return newData;
        }

        // https://stackoverflow.com/questions/185690/how-to-inflate-a-file-with-zlib-net/33855097#33855097
        public static byte[] Inflate(byte[] input)
        {
            using var output = new MemoryStream();
            using var data = new MemoryStream(input);
            using var deflateStream = new DeflateStream(data, CompressionMode.Decompress, true);
            deflateStream.CopyTo(output);
            return output.ToArray();
        }
    }
}