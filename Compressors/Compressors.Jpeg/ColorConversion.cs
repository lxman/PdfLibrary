using System;

namespace Compressors.Jpeg;

/// <summary>
/// Color space conversion routines for JPEG compression.
/// Converts between RGB and YCbCr color spaces using JFIF/JPEG standard formulas.
/// </summary>
public static class ColorConversion
{
    // RGB to YCbCr conversion coefficients (ITU-R BT.601 / JFIF standard)
    // Y  =  0.299 * R + 0.587 * G + 0.114 * B
    // Cb = -0.168736 * R - 0.331264 * G + 0.5 * B + 128
    // Cr =  0.5 * R - 0.418688 * G - 0.081312 * B + 128

    // Pre-computed fixed-point multipliers (scaled by 65536)
    private const int YR = 19595;   // 0.299 * 65536
    private const int YG = 38470;   // 0.587 * 65536
    private const int YB = 7471;    // 0.114 * 65536

    private const int CbR = -11056; // -0.168736 * 65536
    private const int CbG = -21712; // -0.331264 * 65536
    private const int CbB = 32768;  //  0.5 * 65536

    private const int CrR = 32768;  //  0.5 * 65536
    private const int CrG = -27440; // -0.418688 * 65536
    private const int CrB = -5328;  // -0.081312 * 65536

    // YCbCr to RGB conversion coefficients
    // R = Y + 1.402 * (Cr - 128)
    // G = Y - 0.344136 * (Cb - 128) - 0.714136 * (Cr - 128)
    // B = Y + 1.772 * (Cb - 128)

    private const int RCr = 91881;  // 1.402 * 65536
    private const int GCb = -22554; // -0.344136 * 65536
    private const int GCr = -46802; // -0.714136 * 65536
    private const int BCb = 116130; // 1.772 * 65536

    /// <summary>
    /// Converts a single RGB pixel to YCbCr.
    /// </summary>
    public static void RgbToYCbCr(byte r, byte g, byte b, out byte y, out byte cb, out byte cr)
    {
        // Use fixed-point arithmetic for speed
        int ri = r, gi = g, bi = b;

        int yi = (YR * ri + YG * gi + YB * bi + 32768) >> 16;
        int cbi = ((CbR * ri + CbG * gi + CbB * bi + 32768) >> 16) + 128;
        int cri = ((CrR * ri + CrG * gi + CrB * bi + 32768) >> 16) + 128;

        y = (byte)Math.Clamp(yi, 0, 255);
        cb = (byte)Math.Clamp(cbi, 0, 255);
        cr = (byte)Math.Clamp(cri, 0, 255);
    }

    /// <summary>
    /// Converts a single YCbCr pixel to RGB.
    /// </summary>
    public static void YCbCrToRgb(byte y, byte cb, byte cr, out byte r, out byte g, out byte b)
    {
        int yi = y;
        int cbi = cb - 128;
        int cri = cr - 128;

        int ri = yi + ((RCr * cri + 32768) >> 16);
        int gi = yi + ((GCb * cbi + GCr * cri + 32768) >> 16);
        int bi = yi + ((BCb * cbi + 32768) >> 16);

        r = (byte)Math.Clamp(ri, 0, 255);
        g = (byte)Math.Clamp(gi, 0, 255);
        b = (byte)Math.Clamp(bi, 0, 255);
    }

    /// <summary>
    /// Converts an RGB image to YCbCr planes.
    /// </summary>
    /// <param name="rgb">RGB data (3 bytes per pixel: R, G, B)</param>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <param name="yPlane">Output Y plane</param>
    /// <param name="cbPlane">Output Cb plane</param>
    /// <param name="crPlane">Output Cr plane</param>
    public static void RgbToYCbCrPlanes(
        ReadOnlySpan<byte> rgb,
        int width,
        int height,
        Span<byte> yPlane,
        Span<byte> cbPlane,
        Span<byte> crPlane)
    {
        int pixelCount = width * height;
        if (rgb.Length != pixelCount * 3)
            throw new ArgumentException("RGB data length must equal width * height * 3", nameof(rgb));
        if (yPlane.Length != pixelCount)
            throw new ArgumentException("Y plane length must equal width * height", nameof(yPlane));
        if (cbPlane.Length != pixelCount)
            throw new ArgumentException("Cb plane length must equal width * height", nameof(cbPlane));
        if (crPlane.Length != pixelCount)
            throw new ArgumentException("Cr plane length must equal width * height", nameof(crPlane));

        for (var i = 0; i < pixelCount; i++)
        {
            int rgbIndex = i * 3;
            RgbToYCbCr(
                rgb[rgbIndex], rgb[rgbIndex + 1], rgb[rgbIndex + 2],
                out yPlane[i], out cbPlane[i], out crPlane[i]);
        }
    }

    /// <summary>
    /// Converts YCbCr planes to an RGB image.
    /// </summary>
    public static void YCbCrPlanesToRgb(
        ReadOnlySpan<byte> yPlane,
        ReadOnlySpan<byte> cbPlane,
        ReadOnlySpan<byte> crPlane,
        int width,
        int height,
        Span<byte> rgb)
    {
        int pixelCount = width * height;
        if (yPlane.Length != pixelCount)
            throw new ArgumentException("Y plane length must equal width * height", nameof(yPlane));
        if (cbPlane.Length != pixelCount)
            throw new ArgumentException("Cb plane length must equal width * height", nameof(cbPlane));
        if (crPlane.Length != pixelCount)
            throw new ArgumentException("Cr plane length must equal width * height", nameof(crPlane));
        if (rgb.Length != pixelCount * 3)
            throw new ArgumentException("RGB data length must equal width * height * 3", nameof(rgb));

        for (var i = 0; i < pixelCount; i++)
        {
            int rgbIndex = i * 3;
            YCbCrToRgb(
                yPlane[i], cbPlane[i], crPlane[i],
                out rgb[rgbIndex], out rgb[rgbIndex + 1], out rgb[rgbIndex + 2]);
        }
    }

    /// <summary>
    /// Converts RGBA image to YCbCr planes (alpha channel is discarded).
    /// </summary>
    public static void RgbaToYCbCrPlanes(
        ReadOnlySpan<byte> rgba,
        int width,
        int height,
        Span<byte> yPlane,
        Span<byte> cbPlane,
        Span<byte> crPlane)
    {
        int pixelCount = width * height;
        if (rgba.Length != pixelCount * 4)
            throw new ArgumentException("RGBA data length must equal width * height * 4", nameof(rgba));
        if (yPlane.Length != pixelCount)
            throw new ArgumentException("Y plane length must equal width * height", nameof(yPlane));
        if (cbPlane.Length != pixelCount)
            throw new ArgumentException("Cb plane length must equal width * height", nameof(cbPlane));
        if (crPlane.Length != pixelCount)
            throw new ArgumentException("Cr plane length must equal width * height", nameof(crPlane));

        for (var i = 0; i < pixelCount; i++)
        {
            int rgbaIndex = i * 4;
            RgbToYCbCr(
                rgba[rgbaIndex], rgba[rgbaIndex + 1], rgba[rgbaIndex + 2],
                out yPlane[i], out cbPlane[i], out crPlane[i]);
        }
    }

    /// <summary>
    /// Downsamples a chroma plane by averaging 2x2 blocks (4:2:0 subsampling).
    /// </summary>
    /// <param name="input">Full-resolution chroma plane</param>
    /// <param name="inputWidth">Width of input plane</param>
    /// <param name="inputHeight">Height of input plane</param>
    /// <param name="output">Half-resolution output plane</param>
    public static void Downsample420(
        ReadOnlySpan<byte> input,
        int inputWidth,
        int inputHeight,
        Span<byte> output)
    {
        int outputWidth = (inputWidth + 1) / 2;
        int outputHeight = (inputHeight + 1) / 2;

        if (output.Length != outputWidth * outputHeight)
            throw new ArgumentException("Output length must equal (width+1)/2 * (height+1)/2", nameof(output));

        for (var oy = 0; oy < outputHeight; oy++)
        {
            int iy = oy * 2;
            int iy2 = Math.Min(iy + 1, inputHeight - 1);

            for (var ox = 0; ox < outputWidth; ox++)
            {
                int ix = ox * 2;
                int ix2 = Math.Min(ix + 1, inputWidth - 1);

                // Average 2x2 block
                int sum = input[iy * inputWidth + ix] +
                          input[iy * inputWidth + ix2] +
                          input[iy2 * inputWidth + ix] +
                          input[iy2 * inputWidth + ix2];

                output[oy * outputWidth + ox] = (byte)((sum + 2) / 4);
            }
        }
    }

    /// <summary>
    /// Upsamples a chroma plane from 4:2:0 to full resolution using bilinear interpolation.
    /// </summary>
    public static void Upsample420(
        ReadOnlySpan<byte> input,
        int inputWidth,
        int inputHeight,
        Span<byte> output,
        int outputWidth,
        int outputHeight)
    {
        if (input.Length != inputWidth * inputHeight)
            throw new ArgumentException("Input length must equal inputWidth * inputHeight", nameof(input));
        if (output.Length != outputWidth * outputHeight)
            throw new ArgumentException("Output length must equal outputWidth * outputHeight", nameof(output));

        for (var oy = 0; oy < outputHeight; oy++)
        {
            // Map output y to input y (with 0.5 offset for center sampling)
            float fy = (oy + 0.5f) * inputHeight / outputHeight - 0.5f;
            int iy0 = Math.Max(0, (int)fy);
            int iy1 = Math.Min(iy0 + 1, inputHeight - 1);
            float wy = fy - iy0;

            for (var ox = 0; ox < outputWidth; ox++)
            {
                float fx = (ox + 0.5f) * inputWidth / outputWidth - 0.5f;
                int ix0 = Math.Max(0, (int)fx);
                int ix1 = Math.Min(ix0 + 1, inputWidth - 1);
                float wx = fx - ix0;

                // Bilinear interpolation
                float v00 = input[iy0 * inputWidth + ix0];
                float v01 = input[iy0 * inputWidth + ix1];
                float v10 = input[iy1 * inputWidth + ix0];
                float v11 = input[iy1 * inputWidth + ix1];

                float v0 = v00 + wx * (v01 - v00);
                float v1 = v10 + wx * (v11 - v10);
                float value = v0 + wy * (v1 - v0);

                output[oy * outputWidth + ox] = (byte)Math.Clamp((int)(value + 0.5f), 0, 255);
            }
        }
    }

    /// <summary>
    /// Downsamples a chroma plane horizontally only (4:2:2 subsampling).
    /// </summary>
    public static void Downsample422(
        ReadOnlySpan<byte> input,
        int inputWidth,
        int inputHeight,
        Span<byte> output)
    {
        int outputWidth = (inputWidth + 1) / 2;

        if (output.Length != outputWidth * inputHeight)
            throw new ArgumentException("Output length must equal (width+1)/2 * height", nameof(output));

        for (var y = 0; y < inputHeight; y++)
        {
            for (var ox = 0; ox < outputWidth; ox++)
            {
                int ix = ox * 2;
                int ix2 = Math.Min(ix + 1, inputWidth - 1);

                // Average horizontal pair
                int sum = input[y * inputWidth + ix] + input[y * inputWidth + ix2];
                output[y * outputWidth + ox] = (byte)((sum + 1) / 2);
            }
        }
    }

    /// <summary>
    /// Converts grayscale to Y plane (direct copy).
    /// </summary>
    public static void GrayscaleToY(ReadOnlySpan<byte> grayscale, Span<byte> yPlane)
    {
        if (grayscale.Length != yPlane.Length)
            throw new ArgumentException("Grayscale and Y plane must have same length");

        grayscale.CopyTo(yPlane);
    }

    /// <summary>
    /// Converts Y plane to grayscale (direct copy).
    /// </summary>
    public static void YToGrayscale(ReadOnlySpan<byte> yPlane, Span<byte> grayscale)
    {
        if (yPlane.Length != grayscale.Length)
            throw new ArgumentException("Y plane and grayscale must have same length");

        yPlane.CopyTo(grayscale);
    }
}
