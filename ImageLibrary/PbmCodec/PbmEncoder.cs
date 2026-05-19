using System;
using System.IO;
using System.Text;

namespace PbmCodec;

/// <summary>
/// Encodes Netpbm images. Emits binary variants (P4 / P5 / P6) at 8 bits
/// per sample. Defaults to <see cref="PbmFormat.BinaryPixmap"/> (lossless RGB).
/// </summary>
public static class PbmEncoder
{
    /// <summary>
    /// Encode a Netpbm image. Choose the variant via <paramref name="format"/>;
    /// only binary variants (P4, P5, P6) are produced.
    /// </summary>
    public static byte[] Encode(PbmImage image, PbmFormat format = PbmFormat.BinaryPixmap)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));

        return format switch
        {
            PbmFormat.BinaryBitmap  => EncodeBinaryBitmap(image),
            PbmFormat.BinaryGraymap => EncodeBinaryGraymap(image),
            PbmFormat.BinaryPixmap  => EncodeBinaryPixmap(image),
            PbmFormat.AsciiBitmap or PbmFormat.AsciiGraymap or PbmFormat.AsciiPixmap
                => throw new NotSupportedException($"ASCII Netpbm output ({format}) is not implemented; use a binary variant"),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown PbmFormat"),
        };
    }

    public static void Encode(PbmImage image, Stream stream, PbmFormat format = PbmFormat.BinaryPixmap)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        byte[] data = Encode(image, format);
        stream.Write(data, 0, data.Length);
    }

    public static void Encode(PbmImage image, string path, PbmFormat format = PbmFormat.BinaryPixmap)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        File.WriteAllBytes(path, Encode(image, format));
    }

    private static byte[] EncodeBinaryBitmap(PbmImage image)
    {
        int rowBytes = (image.Width + 7) / 8;
        byte[] header = BuildHeader("P4", image.Width, image.Height, maxval: null);
        var output = new byte[header.Length + rowBytes * image.Height];
        Buffer.BlockCopy(header, 0, output, 0, header.Length);

        int dst = header.Length;
        byte[] src = image.PixelData;

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                int srcOffset = (y * image.Width + x) * 4;
                byte luma = Luminance(src[srcOffset + 2], src[srcOffset + 1], src[srcOffset]);
                if (luma < 128)
                {
                    int byteIndex = dst + (x >> 3);
                    output[byteIndex] |= (byte)(1 << (7 - (x & 7)));
                }
            }
            dst += rowBytes;
        }

        return output;
    }

    private static byte[] EncodeBinaryGraymap(PbmImage image)
    {
        byte[] header = BuildHeader("P5", image.Width, image.Height, maxval: 255);
        var output = new byte[header.Length + image.Width * image.Height];
        Buffer.BlockCopy(header, 0, output, 0, header.Length);

        int dst = header.Length;
        byte[] src = image.PixelData;
        int pixelCount = image.Width * image.Height;

        for (int i = 0; i < pixelCount; i++)
        {
            int srcOffset = i * 4;
            output[dst++] = Luminance(src[srcOffset + 2], src[srcOffset + 1], src[srcOffset]);
        }

        return output;
    }

    private static byte[] EncodeBinaryPixmap(PbmImage image)
    {
        byte[] header = BuildHeader("P6", image.Width, image.Height, maxval: 255);
        var output = new byte[header.Length + image.Width * image.Height * 3];
        Buffer.BlockCopy(header, 0, output, 0, header.Length);

        int dst = header.Length;
        byte[] src = image.PixelData;
        int pixelCount = image.Width * image.Height;

        for (int i = 0; i < pixelCount; i++)
        {
            int srcOffset = i * 4;
            output[dst++] = src[srcOffset + 2]; // R
            output[dst++] = src[srcOffset + 1]; // G
            output[dst++] = src[srcOffset];     // B
        }

        return output;
    }

    private static byte[] BuildHeader(string magic, int width, int height, int? maxval)
    {
        var sb = new StringBuilder();
        sb.Append(magic).Append('\n');
        sb.Append(width).Append(' ').Append(height).Append('\n');
        if (maxval.HasValue)
            sb.Append(maxval.Value).Append('\n');
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static byte Luminance(byte r, byte g, byte b)
    {
        // ITU-R BT.601 luma — same coefficients used by JPEG.
        int luma = (299 * r + 587 * g + 114 * b + 500) / 1000;
        if (luma < 0) return 0;
        if (luma > 255) return 255;
        return (byte)luma;
    }
}
