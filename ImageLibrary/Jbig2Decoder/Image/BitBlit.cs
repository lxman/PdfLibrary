namespace Jbig2Decoder.Image
{
    /// <summary>
    /// Byte-aligned 1-bpp compositor shared by region-onto-page and
    /// glyph-onto-text-region composition. Combines <paramref name="src"/> into
    /// <paramref name="dst"/> at (<paramref name="dx"/>, <paramref name="dy"/>)
    /// under the JBIG2 combination operators (T.88 §7.4 Table 5: 0=OR, 1=AND,
    /// 2=XOR, 3=XNOR, 4=REPLACE), processing a whole byte (8 pixels) per
    /// read-modify-write instead of one pixel at a time.
    /// <para>
    /// Both bitmaps share the same MSB-first, byte-aligned-row storage (pixel 0
    /// occupies bit 7 of byte 0). Source pixels that fall outside <paramref name="dst"/>
    /// are clipped on every edge, and source pad bits past the last valid column
    /// never contribute — only columns [0, src.Width) are composited. The result
    /// is byte-for-byte identical to the obvious per-pixel loop (see
    /// BitBlitDifferentialTests), which is what the jbig2dec-parity corpus tests
    /// validate end-to-end.
    /// </para>
    /// </summary>
    internal static class BitBlit
    {
        public static void Compose(Bitmap dst, Bitmap src, int dx, int dy, int op)
        {
            int sw = src.Width;
            int sh = src.Height;
            int dw = dst.Width;
            int dh = dst.Height;
            if (sw == 0 || sh == 0 || dw == 0 || dh == 0) return;

            // Horizontal span of source columns that land inside dst. dx may be
            // negative (glyph placed partly off the left edge); clip both ends.
            int sxStart = dx < 0 ? -dx : 0;
            int sxEnd = sw;
            if (dx + sxEnd > dw) sxEnd = dw - dx;
            if (sxStart >= sxEnd) return; // nothing visible horizontally

            int dColStart = dx + sxStart;       // first dest column written (>= 0)
            int dColEnd = dx + sxEnd;           // one past the last dest column (<= dw)
            int dByteStart = dColStart >> 3;
            int dByteEnd = (dColEnd - 1) >> 3;  // inclusive

            // Partial-coverage masks for the first and last touched dest byte; all
            // bytes between them are fully covered (0xFF). A bit set in the mask
            // means "this dest column is covered by a source column" — the op only
            // touches masked bits, so pad bits and off-edge columns stay untouched.
            var leftMask = (byte)(0xFF >> (dColStart & 7));
            var rightMask = (byte)(0xFF << (7 - ((dColEnd - 1) & 7)));

            int srcStride = src.Stride;
            int dstStride = dst.Stride;
            byte[] sd = src.Data;
            byte[] dd = dst.Data;

            for (var sy = 0; sy < sh; sy++)
            {
                int ty = dy + sy;
                if ((uint)ty >= (uint)dh) continue;

                int srcRow = sy * srcStride;
                int dstRow = ty * dstStride;

                for (int dB = dByteStart; dB <= dByteEnd; dB++)
                {
                    // The 8 source bits that align to this dest byte's columns
                    // [8*dB, 8*dB+7]. Source and dest differ by a constant bit
                    // shift across the row, so this is a windowed read of up to two
                    // adjacent source bytes.
                    int srcBitBase = (dB << 3) - dx;     // source column at dest bit 7 (MSB)
                    int sB = srcBitBase >> 3;            // floor division — correct for negative bases
                    int off = srcBitBase & 7;            // 0..7
                    int sLo = (uint)sB < (uint)srcStride ? sd[srcRow + sB] : 0;
                    int sHi = (uint)(sB + 1) < (uint)srcStride ? sd[srcRow + sB + 1] : 0;
                    var srcAligned = (byte)(((sLo << off) | (sHi >> (8 - off))) & 0xFF);

                    var mask = (byte)0xFF;
                    if (dB == dByteStart) mask &= leftMask;
                    if (dB == dByteEnd) mask &= rightMask;

                    int di = dstRow + dB;
                    int d = dd[di];
                    int sm = srcAligned & mask;
                    int notMask = ~mask & 0xFF;
                    int result = op switch
                    {
                        0 => d | sm,                                      // OR
                        1 => d & (srcAligned | notMask),                  // AND
                        2 => d ^ sm,                                      // XOR
                        3 => (d & notMask) | (~(srcAligned ^ d) & mask),  // XNOR
                        4 => (d & notMask) | sm,                          // REPLACE
                        _ => d | sm,
                    };
                    dd[di] = (byte)result;
                }
            }
        }
    }
}
