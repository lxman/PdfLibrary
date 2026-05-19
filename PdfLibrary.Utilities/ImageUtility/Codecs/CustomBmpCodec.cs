using BmpCodec;

namespace ImageUtility.Codecs;

/// <summary>
/// BMP codec backed by the in-house BmpCodec library.
/// </summary>
public class CustomBmpCodec : IImageCodec
{
    public string Name => "Custom BMP Codec (BmpCodec)";
    public string[] Extensions => [".bmp"];
    public bool CanDecode => true;
    public bool CanEncode => true;

    public bool CanHandle(ReadOnlySpan<byte> header)
    {
        return header.Length >= 2 && header[0] == 'B' && header[1] == 'M';
    }

    public ImageData Decode(byte[] data)
    {
        BmpImage image = BmpDecoder.Decode(data);

        double dpiX = image.XPixelsPerMeter > 0 ? image.XPixelsPerMeter * 0.0254 : 96.0;
        double dpiY = image.YPixelsPerMeter > 0 ? image.YPixelsPerMeter * 0.0254 : 96.0;

        return new ImageData
        {
            Width = image.Width,
            Height = image.Height,
            PixelFormat = PixelFormat.Bgra32,
            Data = image.PixelData,
            DpiX = dpiX,
            DpiY = dpiY,
            Metadata =
            {
                ["BitsPerPixel"] = image.BitsPerPixel,
            },
        };
    }

    public byte[] Encode(ImageData imageData, CodecOptions? options = null)
    {
        byte[] bgra = PixelConverter.ToBgra32(imageData.Data, imageData.Width, imageData.Height, imageData.PixelFormat);
        var image = new BmpImage(imageData.Width, imageData.Height, 32, bgra)
        {
            XPixelsPerMeter = (int)Math.Round(imageData.DpiX / 0.0254),
            YPixelsPerMeter = (int)Math.Round(imageData.DpiY / 0.0254),
        };

        int bitsPerPixel = PixelConverter.HasAlpha(imageData.PixelFormat) ? 32 : 24;
        return BmpEncoder.Encode(image, bitsPerPixel);
    }
}
