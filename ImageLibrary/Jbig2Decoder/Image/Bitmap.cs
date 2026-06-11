using System;

namespace Jbig2Decoder.Image
{
    /// <summary>
    /// 1-bit-per-pixel bitmap. Rows are byte-aligned (stride = (width + 7) / 8),
    /// packed MSB-first within each byte (pixel 0 occupies bit 7 of byte 0).
    /// Matches the storage convention used by jbig2dec's <c>Jbig2Image</c>, so
    /// fixtures captured from there can be byte-compared directly.
    /// </summary>
    internal sealed class Bitmap
    {
        public int Width { get; }
        public int Height { get; }
        public int Stride { get; }
        public byte[] Data { get; }

        // Largest backing buffer a single bitmap may allocate. Region/page dimensions come
        // straight from untrusted segment headers (cast from uint); the product is computed in
        // long and capped so a hostile width/height cannot overflow the byte[] length or force a
        // runaway allocation. 1 GiB of packed rows is ~8 gigapixels — far beyond any real page.
        private const long MaxDataBytes = 1L << 30;

        public Bitmap(int width, int height)
        {
            if (width < 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 0) throw new ArgumentOutOfRangeException(nameof(height));
            int stride = (width + 7) / 8;
            long bytes = (long)stride * height;
            if (bytes > MaxDataBytes)
                throw new ArgumentOutOfRangeException(nameof(width),
                    $"JBIG2 bitmap {width}x{height} ({bytes} bytes) exceeds the {MaxDataBytes}-byte limit.");
            Width = width;
            Height = height;
            Stride = stride;
            Data = new byte[(int)bytes];
        }

        public Bitmap(int width, int height, byte[] data)
        {
            if (width < 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 0) throw new ArgumentOutOfRangeException(nameof(height));
            Width = width;
            Height = height;
            Stride = (width + 7) / 8;
            long expected = (long)Stride * height;
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.Length != expected) throw new ArgumentException($"Expected {expected} bytes, got {data.Length}");
            Data = data;
        }

        /// <summary>
        /// Reads one pixel. Out-of-bounds reads return 0 per the JBIG2 spec
        /// (T.88 §6.2.5.4 — pixels outside the image area are treated as 0).
        /// </summary>
        public int GetPixel(int x, int y)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return 0;
            int b = Data[y * Stride + (x >> 3)];
            return (b >> (7 - (x & 7))) & 1;
        }

        public void SetPixel(int x, int y, int bit)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
            int idx = y * Stride + (x >> 3);
            int mask = 1 << (7 - (x & 7));
            if (bit != 0) Data[idx] = (byte)(Data[idx] | mask);
            else          Data[idx] = (byte)(Data[idx] & ~mask);
        }
    }
}
