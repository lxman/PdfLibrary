using ImageLibrary.Png;
using ImageLibrary.Bmp;
using ImageLibrary.Gif;
using ImageLibrary.Tga;
using ImageLibrary.Tiff;

namespace ImageUtility.Codecs;

/// <summary>
/// Codec using ImageLibrary for common image formats.
/// Supports PNG, BMP, GIF, TGA, and TIFF (decode only).
/// </summary>
public class ImageLibraryCodec : IImageCodec
{
    private readonly string _formatName;
    private readonly string[] _extensions;
    private readonly Func<byte[], (int Width, int Height, byte[] Data, PixelFormat Format)> _decoder;

    private ImageLibraryCodec(
        string formatName,
        string[] extensions,
        Func<byte[], (int Width, int Height, byte[] Data, PixelFormat Format)> decoder)
    {
        _formatName = formatName;
        _extensions = extensions;
        _decoder = decoder;
    }

    public string Name => $"ImageLibrary {_formatName}";
    public string[] Extensions => _extensions;
    public bool CanDecode => true;
    public bool CanEncode => false; // Encoding not implemented in ImageLibrary yet

    public bool CanHandle(ReadOnlySpan<byte> header)
    {
        // Check magic bytes based on format
        if (_formatName == "PNG")
        {
            // PNG signature: 89 50 4E 47 0D 0A 1A 0A
            return header.Length >= 8
                && header[0] == 0x89
                && header[1] == 0x50
                && header[2] == 0x4E
                && header[3] == 0x47;
        }
        else if (_formatName == "BMP")
        {
            // BMP signature: 42 4D (BM)
            return header.Length >= 2
                && header[0] == 0x42
                && header[1] == 0x4D;
        }
        else if (_formatName == "GIF")
        {
            // GIF signature: 47 49 46 38 (GIF8)
            return header.Length >= 4
                && header[0] == 0x47
                && header[1] == 0x49
                && header[2] == 0x46
                && header[3] == 0x38;
        }
        else if (_formatName == "TGA")
        {
            // TGA has no reliable magic bytes, check extension
            return false; // Will rely on extension-based matching
        }
        else if (_formatName == "TIFF")
        {
            // TIFF signature: 49 49 2A 00 (little-endian) or 4D 4D 00 2A (big-endian)
            return header.Length >= 4
                && ((header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x2A && header[3] == 0x00)
                 || (header[0] == 0x4D && header[1] == 0x4D && header[2] == 0x00 && header[3] == 0x2A));
        }

        return false;
    }

    public ImageData Decode(byte[] data)
    {
        var (width, height, pixelData, pixelFormat) = _decoder(data);

        return new ImageData
        {
            Data = pixelData,
            Width = width,
            Height = height,
            PixelFormat = pixelFormat,
            DpiX = 96.0,
            DpiY = 96.0,
            Metadata = new Dictionary<string, object>
            {
                { "Format", _formatName }
            }
        };
    }

    public byte[] Encode(ImageData imageData, CodecOptions? options = null)
    {
        throw new NotSupportedException($"{Name} can only decode images. Encoding is not yet implemented.");
    }

    // Factory methods for different formats

    public static ImageLibraryCodec CreatePngCodec()
    {
        return new ImageLibraryCodec(
            "PNG",
            [".png"],
            data =>
            {
                var img = PngDecoder.Decode(data);
                // PNG decoder outputs BGRA32
                return (img.Width, img.Height, img.PixelData, PixelFormat.Bgra32);
            });
    }

    public static ImageLibraryCodec CreateBmpCodec()
    {
        return new ImageLibraryCodec(
            "BMP",
            [".bmp", ".dib"],
            data =>
            {
                var img = BmpDecoder.Decode(data);
                // BMP decoder outputs BGRA32
                return (img.Width, img.Height, img.PixelData, PixelFormat.Bgra32);
            });
    }

    public static ImageLibraryCodec CreateGifCodec()
    {
        return new ImageLibraryCodec(
            "GIF",
            [".gif"],
            data =>
            {
                var gifFile = GifDecoder.Decode(data);
                // GIF decoder outputs BGRA32, use first frame for static GIFs
                var frame = gifFile.FirstFrame ?? throw new InvalidOperationException("GIF file has no frames");
                return (frame.Width, frame.Height, frame.PixelData, PixelFormat.Bgra32);
            });
    }

    public static ImageLibraryCodec CreateTgaCodec()
    {
        return new ImageLibraryCodec(
            "TGA",
            [".tga", ".targa"],
            data =>
            {
                var img = TgaDecoder.Decode(data);
                // TGA decoder outputs BGRA32
                return (img.Width, img.Height, img.PixelData, PixelFormat.Bgra32);
            });
    }

    public static ImageLibraryCodec CreateTiffCodec()
    {
        return new ImageLibraryCodec(
            "TIFF",
            [".tif", ".tiff"],
            data =>
            {
                var img = TiffDecoder.Decode(data);
                // TIFF decoder outputs BGRA32
                return (img.Width, img.Height, img.PixelData, PixelFormat.Bgra32);
            });
    }
}
