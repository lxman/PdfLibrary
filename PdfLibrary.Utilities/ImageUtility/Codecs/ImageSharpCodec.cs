using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Pbm;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageUtility.Codecs;

/// <summary>
/// Fallback codec using SixLabors.ImageSharp for common image formats.
/// Supports JPEG, PNG, BMP, GIF, and TIFF.
/// </summary>
public class ImageSharpCodec : IImageCodec
{
    private readonly string _formatName;
    private readonly string[] _extensions;
    private readonly IImageFormat _imageFormat;
    private readonly IImageEncoder _encoder;

    private ImageSharpCodec(string formatName, string[] extensions, IImageFormat imageFormat, IImageEncoder encoder)
    {
        _formatName = formatName;
        _extensions = extensions;
        _imageFormat = imageFormat;
        _encoder = encoder;
    }

    public string Name => $"ImageSharp {_formatName}";
    public string[] Extensions => _extensions;
    public bool CanDecode => true;
    public bool CanEncode => true;

    public bool CanHandle(ReadOnlySpan<byte> header)
    {
        // Use ImageSharp's format detection
        try
        {
            var detectedFormat = Image.DetectFormat(header);
            return detectedFormat?.Name == _imageFormat.Name;
        }
        catch
        {
            return false;
        }
    }

    public ImageData Decode(byte[] data)
    {
        using var image = Image.Load<Rgb24>(data);

        int width = image.Width;
        int height = image.Height;
        byte[] pixelData = new byte[width * height * 3]; // RGB24

        // Extract pixel data using indexer
        int offset = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var pixel = image[x, y];
                pixelData[offset++] = pixel.R;
                pixelData[offset++] = pixel.G;
                pixelData[offset++] = pixel.B;
            }
        }

        return new ImageData
        {
            Data = pixelData,
            Width = width,
            Height = height,
            PixelFormat = PixelFormat.Rgb24,
            DpiX = image.Metadata.HorizontalResolution,
            DpiY = image.Metadata.VerticalResolution
        };
    }

    public byte[] Encode(ImageData imageData, CodecOptions? options = null)
    {
        // Convert ImageData to ImageSharp format
        Image<Rgb24> image;

        if (imageData.PixelFormat == PixelFormat.Rgb24)
        {
            image = Image.LoadPixelData<Rgb24>(imageData.Data, imageData.Width, imageData.Height);
        }
        else if (imageData.PixelFormat == PixelFormat.Rgba32)
        {
            // Convert RGBA32 to RGB24
            var rgbaImage = Image.LoadPixelData<Rgba32>(imageData.Data, imageData.Width, imageData.Height);
            image = new Image<Rgb24>(imageData.Width, imageData.Height);
            for (int y = 0; y < imageData.Height; y++)
            {
                for (int x = 0; x < imageData.Width; x++)
                {
                    var srcPixel = rgbaImage[x, y];
                    image[x, y] = new Rgb24(srcPixel.R, srcPixel.G, srcPixel.B);
                }
            }
        }
        else
        {
            throw new NotSupportedException($"Pixel format {imageData.PixelFormat} not supported for encoding");
        }

        // Set DPI
        image.Metadata.HorizontalResolution = imageData.DpiX;
        image.Metadata.VerticalResolution = imageData.DpiY;

        // Encode to byte array
        using var stream = new MemoryStream();
        image.Save(stream, _encoder);
        return stream.ToArray();
    }

    // Factory methods for different formats

    public static ImageSharpCodec CreateJpegCodec()
    {
        return new ImageSharpCodec(
            "JPEG",
            new[] { ".jpg", ".jpeg" },
            JpegFormat.Instance,
            new JpegEncoder { Quality = 95 });
    }

    public static ImageSharpCodec CreatePngCodec()
    {
        return new ImageSharpCodec(
            "PNG",
            new[] { ".png" },
            PngFormat.Instance,
            new PngEncoder());
    }

    public static ImageSharpCodec CreateBmpCodec()
    {
        return new ImageSharpCodec(
            "BMP",
            new[] { ".bmp" },
            BmpFormat.Instance,
            new BmpEncoder());
    }

    public static ImageSharpCodec CreateGifCodec()
    {
        return new ImageSharpCodec(
            "GIF",
            new[] { ".gif" },
            GifFormat.Instance,
            new GifEncoder());
    }

    public static ImageSharpCodec CreateTiffCodec()
    {
        return new ImageSharpCodec(
            "TIFF",
            new[] { ".tif", ".tiff" },
            TiffFormat.Instance,
            new TiffEncoder());
    }

    public static ImageSharpCodec CreateTgaCodec()
    {
        return new ImageSharpCodec(
            "TGA",
            new[] { ".tga" },
            TgaFormat.Instance,
            new TgaEncoder());
    }

    public static ImageSharpCodec CreateWebPCodec()
    {
        return new ImageSharpCodec(
            "WebP",
            new[] { ".webp" },
            WebpFormat.Instance,
            new WebpEncoder());
    }

    public static ImageSharpCodec CreatePbmCodec()
    {
        return new ImageSharpCodec(
            "PBM",
            new[] { ".pbm" },
            PbmFormat.Instance,
            new PbmEncoder());
    }
}
