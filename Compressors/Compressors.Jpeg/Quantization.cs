using System;

namespace Compressors.Jpeg;

/// <summary>
/// Handles quantization and dequantization of DCT coefficients.
/// Quantization is the lossy step in JPEG compression that reduces precision
/// of high-frequency components to achieve compression.
/// </summary>
public static class Quantization
{
    /// <summary>
    /// Generates a scaled quantization table based on quality factor.
    /// Quality 50 produces the standard tables from the JPEG spec.
    /// </summary>
    /// <param name="baseTable">Base quantization table (luminance or chrominance)</param>
    /// <param name="quality">Quality factor 1-100 (100 = best quality, least compression)</param>
    /// <returns>Scaled quantization table</returns>
    public static int[] GenerateQuantTable(ReadOnlySpan<byte> baseTable, int quality)
    {
        if (baseTable.Length != 64)
            throw new ArgumentException("Base table must contain exactly 64 elements", nameof(baseTable));

        quality = Math.Clamp(quality, 1, 100);

        // Calculate scale factor
        // Quality 50 = scale 100 (no change)
        // Quality < 50 = scale > 100 (more quantization, lower quality)
        // Quality > 50 = scale < 100 (less quantization, higher quality)
        int scale;
        if (quality < 50)
            scale = 5000 / quality;
        else
            scale = 200 - quality * 2;

        var table = new int[64];
        for (int i = 0; i < 64; i++)
        {
            int value = (baseTable[i] * scale + 50) / 100;
            // Clamp to valid range [1, 255]
            table[i] = Math.Clamp(value, 1, 255);
        }

        return table;
    }

    /// <summary>
    /// Generates a luminance quantization table for the specified quality.
    /// </summary>
    public static int[] GenerateLuminanceQuantTable(int quality)
    {
        return GenerateQuantTable(JpegConstants.LuminanceQuantTable, quality);
    }

    /// <summary>
    /// Generates a chrominance quantization table for the specified quality.
    /// </summary>
    public static int[] GenerateChrominanceQuantTable(int quality)
    {
        return GenerateQuantTable(JpegConstants.ChrominanceQuantTable, quality);
    }

    /// <summary>
    /// Quantizes DCT coefficients by dividing by quantization table values.
    /// Used during encoding.
    /// </summary>
    /// <param name="dctCoeffs">64 DCT coefficients (will be modified in place)</param>
    /// <param name="quantTable">64-element quantization table</param>
    public static void Quantize(Span<float> dctCoeffs, ReadOnlySpan<int> quantTable)
    {
        if (dctCoeffs.Length != 64)
            throw new ArgumentException("DCT coefficients must contain exactly 64 elements", nameof(dctCoeffs));
        if (quantTable.Length != 64)
            throw new ArgumentException("Quantization table must contain exactly 64 elements", nameof(quantTable));

        for (int i = 0; i < 64; i++)
        {
            // Round to nearest integer
            dctCoeffs[i] = MathF.Round(dctCoeffs[i] / quantTable[i]);
        }
    }

    /// <summary>
    /// Quantizes DCT coefficients and outputs as integers.
    /// </summary>
    public static void QuantizeToInt(ReadOnlySpan<float> dctCoeffs, ReadOnlySpan<int> quantTable, Span<int> output)
    {
        if (dctCoeffs.Length != 64)
            throw new ArgumentException("DCT coefficients must contain exactly 64 elements", nameof(dctCoeffs));
        if (quantTable.Length != 64)
            throw new ArgumentException("Quantization table must contain exactly 64 elements", nameof(quantTable));
        if (output.Length != 64)
            throw new ArgumentException("Output must contain exactly 64 elements", nameof(output));

        for (int i = 0; i < 64; i++)
        {
            output[i] = (int)MathF.Round(dctCoeffs[i] / quantTable[i]);
        }
    }

    /// <summary>
    /// Dequantizes coefficients by multiplying by quantization table values.
    /// Used during decoding.
    /// </summary>
    /// <param name="quantizedCoeffs">64 quantized coefficients (will be modified in place)</param>
    /// <param name="quantTable">64-element quantization table</param>
    public static void Dequantize(Span<float> quantizedCoeffs, ReadOnlySpan<int> quantTable)
    {
        if (quantizedCoeffs.Length != 64)
            throw new ArgumentException("Quantized coefficients must contain exactly 64 elements", nameof(quantizedCoeffs));
        if (quantTable.Length != 64)
            throw new ArgumentException("Quantization table must contain exactly 64 elements", nameof(quantTable));

        for (int i = 0; i < 64; i++)
        {
            quantizedCoeffs[i] *= quantTable[i];
        }
    }

    /// <summary>
    /// Dequantizes integer coefficients to floats.
    /// </summary>
    public static void DequantizeFromInt(ReadOnlySpan<int> quantizedCoeffs, ReadOnlySpan<int> quantTable, Span<float> output)
    {
        if (quantizedCoeffs.Length != 64)
            throw new ArgumentException("Quantized coefficients must contain exactly 64 elements", nameof(quantizedCoeffs));
        if (quantTable.Length != 64)
            throw new ArgumentException("Quantization table must contain exactly 64 elements", nameof(quantTable));
        if (output.Length != 64)
            throw new ArgumentException("Output must contain exactly 64 elements", nameof(output));

        for (int i = 0; i < 64; i++)
        {
            output[i] = quantizedCoeffs[i] * quantTable[i];
        }
    }

    /// <summary>
    /// Reorders coefficients from natural order to zigzag order.
    /// Used after quantization before entropy coding.
    /// </summary>
    public static void ToZigzag(ReadOnlySpan<int> natural, Span<int> zigzag)
    {
        if (natural.Length != 64)
            throw new ArgumentException("Natural order array must contain exactly 64 elements", nameof(natural));
        if (zigzag.Length != 64)
            throw new ArgumentException("Zigzag order array must contain exactly 64 elements", nameof(zigzag));

        for (int i = 0; i < 64; i++)
        {
            zigzag[i] = natural[JpegConstants.ZigzagOrder[i]];
        }
    }

    /// <summary>
    /// Reorders coefficients from zigzag order to natural order.
    /// Used after entropy decoding before dequantization.
    /// </summary>
    public static void FromZigzag(ReadOnlySpan<int> zigzag, Span<int> natural)
    {
        if (zigzag.Length != 64)
            throw new ArgumentException("Zigzag order array must contain exactly 64 elements", nameof(zigzag));
        if (natural.Length != 64)
            throw new ArgumentException("Natural order array must contain exactly 64 elements", nameof(natural));

        for (int i = 0; i < 64; i++)
        {
            natural[JpegConstants.ZigzagOrder[i]] = zigzag[i];
        }
    }

    /// <summary>
    /// Converts a quantization table to zigzag order for storage in JPEG file.
    /// </summary>
    public static byte[] TableToZigzag(ReadOnlySpan<int> table)
    {
        if (table.Length != 64)
            throw new ArgumentException("Table must contain exactly 64 elements", nameof(table));

        var result = new byte[64];
        for (int i = 0; i < 64; i++)
        {
            result[i] = (byte)table[JpegConstants.ZigzagOrder[i]];
        }
        return result;
    }

    /// <summary>
    /// Converts a quantization table from zigzag order (as stored in JPEG) to natural order.
    /// </summary>
    public static int[] TableFromZigzag(ReadOnlySpan<byte> zigzagTable)
    {
        if (zigzagTable.Length != 64)
            throw new ArgumentException("Table must contain exactly 64 elements", nameof(zigzagTable));

        var result = new int[64];
        for (int i = 0; i < 64; i++)
        {
            result[JpegConstants.ZigzagOrder[i]] = zigzagTable[i];
        }
        return result;
    }
}
