using JpegCodec;

namespace ImageUtility.Codecs;

/// <summary>
/// JPEG codec backed by the in-house JpegCodec library.
/// Supports baseline + progressive JPEG, 1/3/4 components (grayscale, YCbCr, CMYK/YCCK).
/// </summary>
public class CustomJpegCodec : IImageCodec
{
    public string Name => "Custom JPEG Codec (JpegCodec)";
    public string[] Extensions => [".jpg", ".jpeg"];
    public bool CanDecode => true;
    public bool CanEncode => true;

    public bool CanHandle(ReadOnlySpan<byte> header)
    {
        return header.Length >= 3
            && header[0] == 0xFF
            && header[1] == 0xD8
            && header[2] == 0xFF;
    }

    public ImageData Decode(byte[] data)
    {
        var decoder = new JpegStreamDecoder();
        JpegDecodeResult result = decoder.Decode(data);

        byte[] pixels;
        PixelFormat format;
        switch (result.NumberOfComponents)
        {
            case 1:
                pixels = result.ComponentData;
                format = PixelFormat.Gray8;
                break;
            case 3:
                pixels = YCbCrToRgb(result.ComponentData);
                format = PixelFormat.Rgb24;
                break;
            case 4:
                pixels = CmykOrYcck(result);
                format = PixelFormat.Cmyk32;
                break;
            default:
                throw new NotSupportedException(
                    $"Unsupported JPEG component count: {result.NumberOfComponents}");
        }

        return new ImageData
        {
            Width = result.Width,
            Height = result.Height,
            PixelFormat = format,
            Data = pixels,
            DpiX = 96.0,
            DpiY = 96.0,
            Metadata =
            {
                ["JpegPrecision"] = result.Precision,
                ["HasAdobeMarker"] = result.HasAdobeMarker,
                ["AdobeColorTransform"] = result.AdobeColorTransform,
            },
        };
    }

    public byte[] Encode(ImageData imageData, CodecOptions? options = null)
    {
        var quality = 95;
        if (options?.Options.TryGetValue("Quality", out object? q) == true)
            quality = Convert.ToInt32(q);

        byte[] componentData;
        int numberOfComponents;
        switch (imageData.PixelFormat)
        {
            case PixelFormat.Gray8:
                componentData = imageData.Data;
                numberOfComponents = 1;
                break;
            case PixelFormat.Rgb24:
                componentData = RgbToYCbCr(imageData.Data);
                numberOfComponents = 3;
                break;
            case PixelFormat.Bgr24:
                componentData = BgrToYCbCr(imageData.Data);
                numberOfComponents = 3;
                break;
            case PixelFormat.Rgba32:
                componentData = RgbaToYCbCr(imageData.Data);
                numberOfComponents = 3;
                break;
            case PixelFormat.Bgra32:
                componentData = BgraToYCbCr(imageData.Data);
                numberOfComponents = 3;
                break;
            case PixelFormat.Cmyk32:
                componentData = imageData.Data;
                numberOfComponents = 4;
                break;
            default:
                throw new NotSupportedException(
                    $"Unsupported pixel format for JPEG encode: {imageData.PixelFormat}");
        }

        var encoder = new JpegStreamEncoder();
        return encoder.Encode(componentData, new JpegEncodeOptions
        {
            Width = imageData.Width,
            Height = imageData.Height,
            NumberOfComponents = numberOfComponents,
            Quality = quality,
            EmitJfif = numberOfComponents != 4,
            EmitAdobeMarker = numberOfComponents == 4,
            AdobeColorTransform = 0,
        });
    }

    private static byte[] YCbCrToRgb(byte[] ycbcr)
    {
        int pixelCount = ycbcr.Length / 3;
        var rgb = new byte[pixelCount * 3];
        for (var i = 0; i < pixelCount; i++)
        {
            int off = i * 3;
            byte y = ycbcr[off];
            byte cb = ycbcr[off + 1];
            byte cr = ycbcr[off + 2];
            var r = (int)(y + 1.402f * (cr - 128));
            var g = (int)(y - 0.34414f * (cb - 128) - 0.71414f * (cr - 128));
            var b = (int)(y + 1.772f * (cb - 128));
            rgb[off]     = (byte)Math.Clamp(r, 0, 255);
            rgb[off + 1] = (byte)Math.Clamp(g, 0, 255);
            rgb[off + 2] = (byte)Math.Clamp(b, 0, 255);
        }
        return rgb;
    }

    private static byte[] CmykOrYcck(JpegDecodeResult result)
    {
        byte[] src = result.ComponentData;
        bool isAdobeYcck = result.HasAdobeMarker && result.AdobeColorTransform == 2;
        bool isInvertedCmyk = result.HasAdobeMarker && result.AdobeColorTransform == 0;
        var cmyk = new byte[src.Length];
        int pixelCount = src.Length / 4;

        if (isAdobeYcck)
        {
            for (var i = 0; i < pixelCount; i++)
            {
                int o = i * 4;
                float y = src[o], cb = src[o + 1], cr = src[o + 2];
                byte k = src[o + 3];
                float rPrime = y + 1.402f * (cr - 128);
                float gPrime = y - 0.344136f * (cb - 128) - 0.714136f * (cr - 128);
                float bPrime = y + 1.772f * (cb - 128);
                cmyk[o]     = ClampToByte(255 - rPrime);
                cmyk[o + 1] = ClampToByte(255 - gPrime);
                cmyk[o + 2] = ClampToByte(255 - bPrime);
                cmyk[o + 3] = k;
            }
        }
        else if (isInvertedCmyk)
        {
            for (var i = 0; i < src.Length; i++) cmyk[i] = (byte)(255 - src[i]);
        }
        else
        {
            Array.Copy(src, cmyk, src.Length);
        }
        return cmyk;
    }

    private static byte[] RgbToYCbCr(byte[] rgb)
    {
        int pixelCount = rgb.Length / 3;
        var ycbcr = new byte[pixelCount * 3];
        for (var i = 0; i < pixelCount; i++)
        {
            int off = i * 3;
            byte r = rgb[off], g = rgb[off + 1], b = rgb[off + 2];
            EncodePixel(r, g, b, ycbcr, off);
        }
        return ycbcr;
    }

    private static byte[] BgrToYCbCr(byte[] bgr)
    {
        int pixelCount = bgr.Length / 3;
        var ycbcr = new byte[pixelCount * 3];
        for (var i = 0; i < pixelCount; i++)
        {
            int off = i * 3;
            byte b = bgr[off], g = bgr[off + 1], r = bgr[off + 2];
            EncodePixel(r, g, b, ycbcr, off);
        }
        return ycbcr;
    }

    private static byte[] RgbaToYCbCr(byte[] rgba)
    {
        int pixelCount = rgba.Length / 4;
        var ycbcr = new byte[pixelCount * 3];
        for (var i = 0; i < pixelCount; i++)
        {
            int src = i * 4;
            int dst = i * 3;
            byte r = rgba[src], g = rgba[src + 1], b = rgba[src + 2];
            EncodePixel(r, g, b, ycbcr, dst);
        }
        return ycbcr;
    }

    private static byte[] BgraToYCbCr(byte[] bgra)
    {
        int pixelCount = bgra.Length / 4;
        var ycbcr = new byte[pixelCount * 3];
        for (var i = 0; i < pixelCount; i++)
        {
            int src = i * 4;
            int dst = i * 3;
            byte b = bgra[src], g = bgra[src + 1], r = bgra[src + 2];
            EncodePixel(r, g, b, ycbcr, dst);
        }
        return ycbcr;
    }

    private static void EncodePixel(byte r, byte g, byte b, byte[] dst, int off)
    {
        var y  = (int)( 0.299f * r + 0.587f * g + 0.114f * b);
        var cb = (int)(-0.168736f * r - 0.331264f * g + 0.5f * b + 128);
        var cr = (int)( 0.5f * r - 0.418688f * g - 0.081312f * b + 128);
        dst[off]     = (byte)Math.Clamp(y,  0, 255);
        dst[off + 1] = (byte)Math.Clamp(cb, 0, 255);
        dst[off + 2] = (byte)Math.Clamp(cr, 0, 255);
    }

    private static byte ClampToByte(float v) => (byte)Math.Max(0, Math.Min(255, Math.Round(v)));
}
