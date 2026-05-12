using JpegCodec;

namespace ImageUtility.Codecs.Examples;

/// <summary>
/// Example showing how to implement separate JPEG decoder and encoder codecs
/// over the in-house JpegCodec library. Demonstrates the decoder-only +
/// encoder-only registration pattern: each class participates only in the
/// half of the pipeline its CanDecode/CanEncode flags claim.
/// </summary>

// DECODER EXAMPLE - decode-only IImageCodec backed by JpegCodec.JpegStreamDecoder.
public class JpegCodecExample : IImageCodec
{
    public string Name => "JPEG Decoder (JpegCodec)";
    public string[] Extensions => [".jpg", ".jpeg"];
    public bool CanDecode => true;
    public bool CanEncode => false;

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

        // The codec returns raw component bytes in JPEG-native colour:
        //   1 component → grayscale luma
        //   3 components → YCbCr (ITU-R BT.601, JFIF default)
        //   4 components → CMYK or YCCK depending on APP14
        // ImageUtility expects RGB / grayscale / CMYK at the ImageData
        // boundary, so we convert here.
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
        => throw new NotSupportedException($"{Name} can only decode JPEG images.");

    // ITU-R BT.601 YCbCr → RGB. Interleaved input, interleaved RGB output.
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

    // 4-component JPEG handling, mirrors PdfLibrary.Filters.DctDecodeFilter:
    //   APP14 transform=2 → Adobe YCCK (Y/Cb/Cr encode inverted C/M/Y; K as-is)
    //   APP14 transform=0 → inverted CMYK (Photoshop default)
    //   no APP14 → raw CMYK
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

    private static byte ClampToByte(float v) => (byte)Math.Max(0, Math.Min(255, Math.Round(v)));
}

// ENCODER EXAMPLE - encode-only IImageCodec backed by JpegCodec.JpegStreamEncoder.
public class JpegEncoderExample : IImageCodec
{
    public string Name => "JPEG Encoder (JpegCodec)";
    public string[] Extensions => [".jpg", ".jpeg"];
    public bool CanDecode => false;
    public bool CanEncode => true;

    public bool CanHandle(ReadOnlySpan<byte> header) => false;

    public ImageData Decode(byte[] data)
        => throw new NotSupportedException($"{Name} can only encode JPEG images.");

    public byte[] Encode(ImageData imageData, CodecOptions? options = null)
    {
        var quality = 95;
        if (options?.Options.TryGetValue("Quality", out object? q) == true)
            quality = Convert.ToInt32(q);

        // Convert from ImageData's pixel format into the codec's expected
        // raw-component layout. The encoder uses standard YCbCr storage
        // for 3-channel images; we convert RGB→YCbCr before encoding.
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
            case PixelFormat.Cmyk32:
                componentData = imageData.Data;
                numberOfComponents = 4;
                break;
            case PixelFormat.Rgba32:
            case PixelFormat.Bgra32:
                throw new NotSupportedException(
                    "JPEG does not support an alpha channel. Drop or composite the alpha first.");
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
            AdobeColorTransform = 0,  // raw/inverted CMYK marker for 4-channel
        });
    }

    private static byte[] RgbToYCbCr(byte[] rgb)
    {
        int pixelCount = rgb.Length / 3;
        var ycbcr = new byte[pixelCount * 3];
        for (var i = 0; i < pixelCount; i++)
        {
            int off = i * 3;
            byte r = rgb[off], g = rgb[off + 1], b = rgb[off + 2];
            var y  = (int)( 0.299f * r + 0.587f * g + 0.114f * b);
            var cb = (int)(-0.168736f * r - 0.331264f * g + 0.5f * b + 128);
            var cr = (int)( 0.5f * r - 0.418688f * g - 0.081312f * b + 128);
            ycbcr[off]     = (byte)Math.Clamp(y,  0, 255);
            ycbcr[off + 1] = (byte)Math.Clamp(cb, 0, 255);
            ycbcr[off + 2] = (byte)Math.Clamp(cr, 0, 255);
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
            var y  = (int)( 0.299f * r + 0.587f * g + 0.114f * b);
            var cb = (int)(-0.168736f * r - 0.331264f * g + 0.5f * b + 128);
            var cr = (int)( 0.5f * r - 0.418688f * g - 0.081312f * b + 128);
            ycbcr[off]     = (byte)Math.Clamp(y,  0, 255);
            ycbcr[off + 1] = (byte)Math.Clamp(cb, 0, 255);
            ycbcr[off + 2] = (byte)Math.Clamp(cr, 0, 255);
        }
        return ycbcr;
    }
}

// REGISTRATION EXAMPLE
// In CodecRegistry.RegisterBuiltInCodecs(), register both:
//
//   Register(new JpegCodecExample());     // Used for decoding .jpg files
//   Register(new JpegEncoderExample());   // Used for encoding .jpg files
//
// CodecRegistry.Instance.DecodeFile("image.jpg") considers only codecs
// where CanDecode == true → picks JpegCodecExample.
// CodecRegistry.Instance.EncodeFile(imageData, "output.jpg") considers only
// codecs where CanEncode == true → picks JpegEncoderExample.
