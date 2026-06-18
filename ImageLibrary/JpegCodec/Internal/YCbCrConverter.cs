using System;

namespace JpegCodec.Internal;

/// <summary>
/// RGB ↔ YCbCr conversions following JPEG T.871 / JFIF conventions:
///   Y  =  0.29900·R + 0.58700·G + 0.11400·B
///   Cb = -0.16874·R - 0.33126·G + 0.50000·B + 128
///   Cr =  0.50000·R - 0.41869·G - 0.08131·B + 128
/// Results clamped to [0, 255].
/// </summary>
internal static class YCbCrConverter
{
    /// <summary>
    /// Convert an interleaved RGB pixel buffer (3 bytes/pixel, row-major) into
    /// an interleaved YCbCr pixel buffer of the same layout.
    /// Used for round-trip PSNR comparisons in tests.
    /// </summary>
    public static byte[] RgbToYCbCrInterleaved(byte[] rgb, int width, int height)
    {
        int n = width * height;
        var ycbcr = new byte[n * 3];
        for (var i = 0; i < n; i++)
        {
            int r = rgb[i * 3];
            int g = rgb[i * 3 + 1];
            int b = rgb[i * 3 + 2];
            int y  = ( 19595 * r + 38470 * g +  7471 * b          ) >> 16;
            int cb = (-11059 * r - 21709 * g + 32768 * b + 8388608) >> 16;
            int cr = ( 32768 * r - 27439 * g -  5329 * b + 8388608) >> 16;
            ycbcr[i * 3]     = (byte)(y  < 0 ? 0 : y  > 255 ? 255 : y);
            ycbcr[i * 3 + 1] = (byte)(cb < 0 ? 0 : cb > 255 ? 255 : cb);
            ycbcr[i * 3 + 2] = (byte)(cr < 0 ? 0 : cr > 255 ? 255 : cr);
        }
        return ycbcr;
    }

    /// <summary>
    /// Convert an interleaved RGB pixel buffer (3 bytes/pixel, row-major) into
    /// three separate planar byte arrays: Y, Cb, Cr.  Each plane has
    /// <paramref name="width"/> × <paramref name="height"/> bytes.
    /// </summary>
    public static void RgbToYCbCrPlanar(
        byte[] rgb,
        int width, int height,
        out byte[] yPlane,
        out byte[] cbPlane,
        out byte[] crPlane)
    {
        int n = width * height;
        yPlane  = new byte[n];
        cbPlane = new byte[n];
        crPlane = new byte[n];

        for (var i = 0; i < n; i++)
        {
            int r = rgb[i * 3];
            int g = rgb[i * 3 + 1];
            int b = rgb[i * 3 + 2];

            // Integer arithmetic: coefficients × 65536, then >> 16.
            // Y: 0.29900 → 19595, 0.58700 → 38470, 0.11400 → 7471
            // Cb: -0.16874 → -11059, -0.33126 → -21709, 0.50000 → 32768
            // Cr:  0.50000 → 32768, -0.41869 → -27439, -0.08131 → -5329
            int y  = ( 19595 * r + 38470 * g +  7471 * b          ) >> 16;
            int cb = (-11059 * r - 21709 * g + 32768 * b + 8388608) >> 16;
            int cr = ( 32768 * r - 27439 * g -  5329 * b + 8388608) >> 16;

            yPlane[i]  = (byte)(y  < 0 ? 0 : y  > 255 ? 255 : y);
            cbPlane[i] = (byte)(cb < 0 ? 0 : cb > 255 ? 255 : cb);
            crPlane[i] = (byte)(cr < 0 ? 0 : cr > 255 ? 255 : cr);
        }
    }
}
