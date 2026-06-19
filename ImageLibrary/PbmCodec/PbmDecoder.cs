using System;
using System.IO;

namespace PbmCodec;

/// <summary>
/// Decodes Netpbm images (PBM / PGM / PPM, ASCII and binary variants).
/// Produces a <see cref="PbmImage"/> with top-down BGRA32 pixel data.
/// </summary>
public static class PbmDecoder
{
    public static PbmImage Decode(byte[] data) => Decode(data.AsSpan());

    public static PbmImage Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3)
            throw new PbmException("Data too small for a Netpbm header");

        if (data[0] != (byte)'P')
            throw new PbmException($"Invalid Netpbm magic: expected 'P', got 0x{data[0]:X2}");

        var format = (PbmFormat)(data[1] - (byte)'0');
        if (format < PbmFormat.AsciiBitmap || format > PbmFormat.BinaryPixmap)
            throw new PbmException($"Unsupported Netpbm magic: P{(char)data[1]}");

        var reader = new PbmHeaderReader(data, offset: 2);
        int width = reader.ReadInt();
        int height = reader.ReadInt();
        int maxval = format is PbmFormat.AsciiBitmap or PbmFormat.BinaryBitmap
            ? 1
            : reader.ReadInt();

        if (width <= 0 || height <= 0)
            throw new PbmException($"Invalid image dimensions: {width}x{height}");

        // Cap the BGRA allocation (width*height*4) computed in long, so a hostile header can't
        // overflow the int multiply into a small/negative allocation.
        if ((long)width * height * 4 > (1L << 30))
            throw new PbmException($"Image dimensions too large: {width}x{height}");

        if (format is PbmFormat.AsciiGraymap or PbmFormat.AsciiPixmap or PbmFormat.BinaryGraymap or PbmFormat.BinaryPixmap)
        {
            if (maxval is <= 0 or > 65535)
                throw new PbmException($"Invalid maxval: {maxval}");
        }

        // After the header tokens, exactly one whitespace byte separates the header
        // from the raster (for binary formats). The header reader already consumed it.
        int rasterStart = reader.Position;
        ReadOnlySpan<byte> raster = data.Slice(rasterStart);

        var pixels = new byte[width * height * 4];

        switch (format)
        {
            case PbmFormat.AsciiBitmap:
                DecodeAsciiBitmap(raster, width, height, pixels);
                break;
            case PbmFormat.BinaryBitmap:
                DecodeBinaryBitmap(raster, width, height, pixels);
                break;
            case PbmFormat.AsciiGraymap:
                DecodeAsciiGraymap(raster, width, height, maxval, pixels);
                break;
            case PbmFormat.BinaryGraymap:
                DecodeBinaryGraymap(raster, width, height, maxval, pixels);
                break;
            case PbmFormat.AsciiPixmap:
                DecodeAsciiPixmap(raster, width, height, maxval, pixels);
                break;
            case PbmFormat.BinaryPixmap:
                DecodeBinaryPixmap(raster, width, height, maxval, pixels);
                break;
        }

        return new PbmImage(width, height, pixels) { SourceFormat = format };
    }

    public static PbmImage Decode(Stream stream)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Decode(ms.ToArray());
    }

    public static PbmImage Decode(string path)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        return Decode(File.ReadAllBytes(path));
    }

    // ---- bitmap (P1 / P4) ----

    private static void DecodeAsciiBitmap(ReadOnlySpan<byte> raster, int width, int height, byte[] pixels)
    {
        var sampler = new AsciiSampleReader(raster);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int sample = sampler.ReadInt();
                // PBM: 1 = black, 0 = white
                byte g = sample == 0 ? (byte)255 : (byte)0;
                WriteBgra(pixels, x, y, width, g, g, g);
            }
        }
    }

    private static void DecodeBinaryBitmap(ReadOnlySpan<byte> raster, int width, int height, byte[] pixels)
    {
        int rowBytes = (width + 7) / 8;
        int required = rowBytes * height;
        if (raster.Length < required)
            throw new PbmException($"Truncated P4 raster: need {required} bytes, have {raster.Length}");

        for (var y = 0; y < height; y++)
        {
            int rowOffset = y * rowBytes;
            for (var x = 0; x < width; x++)
            {
                int b = raster[rowOffset + (x >> 3)];
                int bit = (b >> (7 - (x & 7))) & 1;
                byte g = bit == 0 ? (byte)255 : (byte)0;
                WriteBgra(pixels, x, y, width, g, g, g);
            }
        }
    }

    // ---- graymap (P2 / P5) ----

    private static void DecodeAsciiGraymap(ReadOnlySpan<byte> raster, int width, int height, int maxval, byte[] pixels)
    {
        var sampler = new AsciiSampleReader(raster);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int sample = sampler.ReadInt();
                byte g = ScaleSample(sample, maxval);
                WriteBgra(pixels, x, y, width, g, g, g);
            }
        }
    }

    private static void DecodeBinaryGraymap(ReadOnlySpan<byte> raster, int width, int height, int maxval, byte[] pixels)
    {
        int bytesPerSample = maxval > 255 ? 2 : 1;
        long required = (long)width * height * bytesPerSample;
        if (raster.Length < required)
            throw new PbmException($"Truncated P5 raster: need {required} bytes, have {raster.Length}");

        var p = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int sample = bytesPerSample == 1
                    ? raster[p]
                    : (raster[p] << 8) | raster[p + 1];
                p += bytesPerSample;
                byte g = ScaleSample(sample, maxval);
                WriteBgra(pixels, x, y, width, g, g, g);
            }
        }
    }

    // ---- pixmap (P3 / P6) ----

    private static void DecodeAsciiPixmap(ReadOnlySpan<byte> raster, int width, int height, int maxval, byte[] pixels)
    {
        var sampler = new AsciiSampleReader(raster);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int r = sampler.ReadInt();
                int g = sampler.ReadInt();
                int b = sampler.ReadInt();
                WriteBgra(pixels, x, y, width,
                    ScaleSample(r, maxval),
                    ScaleSample(g, maxval),
                    ScaleSample(b, maxval));
            }
        }
    }

    private static void DecodeBinaryPixmap(ReadOnlySpan<byte> raster, int width, int height, int maxval, byte[] pixels)
    {
        int bytesPerSample = maxval > 255 ? 2 : 1;
        long required = (long)width * height * 3 * bytesPerSample;
        if (raster.Length < required)
            throw new PbmException($"Truncated P6 raster: need {required} bytes, have {raster.Length}");

        var p = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int r, g, b;
                if (bytesPerSample == 1)
                {
                    r = raster[p];
                    g = raster[p + 1];
                    b = raster[p + 2];
                    p += 3;
                }
                else
                {
                    r = (raster[p]     << 8) | raster[p + 1];
                    g = (raster[p + 2] << 8) | raster[p + 3];
                    b = (raster[p + 4] << 8) | raster[p + 5];
                    p += 6;
                }
                WriteBgra(pixels, x, y, width,
                    ScaleSample(r, maxval),
                    ScaleSample(g, maxval),
                    ScaleSample(b, maxval));
            }
        }
    }

    // ---- helpers ----

    private static void WriteBgra(byte[] pixels, int x, int y, int width, byte r, byte g, byte b)
    {
        int offset = (y * width + x) * 4;
        pixels[offset]     = b;
        pixels[offset + 1] = g;
        pixels[offset + 2] = r;
        pixels[offset + 3] = 255;
    }

    private static byte ScaleSample(int sample, int maxval)
    {
        if (sample < 0) sample = 0;
        if (sample > maxval) sample = maxval;
        if (maxval == 255) return (byte)sample;
        return (byte)((sample * 255 + maxval / 2) / maxval);
    }

    /// <summary>
    /// Parses Netpbm header tokens: integers separated by whitespace, with
    /// '#'-to-EOL comments anywhere. After the last header token a single
    /// whitespace byte is consumed; for binary formats the raster begins
    /// at <see cref="Position"/>.
    /// </summary>
    private ref struct PbmHeaderReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _pos;

        public PbmHeaderReader(ReadOnlySpan<byte> data, int offset)
        {
            _data = data;
            _pos = offset;
        }

        public int Position => _pos;

        public int ReadInt()
        {
            SkipWhitespaceAndComments();
            if (_pos >= _data.Length)
                throw new PbmException("Unexpected end of Netpbm header");

            int start = _pos;
            while (_pos < _data.Length && _data[_pos] >= '0' && _data[_pos] <= '9')
                _pos++;

            if (_pos == start)
                throw new PbmException($"Expected integer in Netpbm header at offset {start} (got 0x{_data[start]:X2})");

            var value = 0;
            for (int i = start; i < _pos; i++)
            {
                int digit = _data[i] - '0';
                // Guard BEFORE the multiply: checking afterwards lets the last digit overflow first.
                if (value > (int.MaxValue - digit) / 10)
                    throw new PbmException("Netpbm header integer overflow");
                value = value * 10 + digit;
            }

            // Consume the single trailing whitespace byte that terminates this token.
            // For binary formats this is also the separator before the raster, so
            // exactly one byte must be consumed here.
            if (_pos < _data.Length && IsWhitespace(_data[_pos]))
                _pos++;

            return value;
        }

        private void SkipWhitespaceAndComments()
        {
            while (_pos < _data.Length)
            {
                byte b = _data[_pos];
                if (IsWhitespace(b))
                {
                    _pos++;
                }
                else if (b == (byte)'#')
                {
                    while (_pos < _data.Length && _data[_pos] != '\n' && _data[_pos] != '\r')
                        _pos++;
                }
                else
                {
                    return;
                }
            }
        }

        private static bool IsWhitespace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\v' or (byte)'\f';
    }

    /// <summary>
    /// Reads whitespace-separated integers from an ASCII raster (P1/P2/P3),
    /// skipping '#' comments.
    /// </summary>
    private ref struct AsciiSampleReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _pos;

        public AsciiSampleReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _pos = 0;
        }

        public int ReadInt()
        {
            while (_pos < _data.Length)
            {
                byte b = _data[_pos];
                if (b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\v' or (byte)'\f')
                {
                    _pos++;
                }
                else if (b == (byte)'#')
                {
                    while (_pos < _data.Length && _data[_pos] != '\n' && _data[_pos] != '\r')
                        _pos++;
                }
                else
                {
                    break;
                }
            }

            if (_pos >= _data.Length)
                throw new PbmException("Unexpected end of Netpbm raster");

            int start = _pos;
            while (_pos < _data.Length && _data[_pos] >= '0' && _data[_pos] <= '9')
                _pos++;

            if (_pos == start)
                throw new PbmException($"Expected integer in Netpbm raster at offset {start}");

            var value = 0;
            for (int i = start; i < _pos; i++)
            {
                int digit = _data[i] - '0';
                if (value > (int.MaxValue - digit) / 10)
                    throw new PbmException("Netpbm raster integer overflow");
                value = value * 10 + digit;
            }
            return value;
        }
    }
}
