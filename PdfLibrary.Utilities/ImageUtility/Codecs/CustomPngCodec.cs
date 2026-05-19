using PngCodec;

namespace ImageUtility.Codecs;

/// <summary>
/// PNG codec backed by the in-house PngCodec library.
/// </summary>
public class CustomPngCodec : IImageCodec
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    public string Name => "Custom PNG Codec (PngCodec)";
    public string[] Extensions => [".png"];
    public bool CanDecode => true;
    public bool CanEncode => true;

    public bool CanHandle(ReadOnlySpan<byte> header)
    {
        if (header.Length < PngSignature.Length) return false;
        for (var i = 0; i < PngSignature.Length; i++)
            if (header[i] != PngSignature[i]) return false;
        return true;
    }

    public ImageData Decode(byte[] data)
    {
        PngImage image = PngDecoder.Decode(data);

        return new ImageData
        {
            Width = image.Width,
            Height = image.Height,
            PixelFormat = PixelFormat.Bgra32,
            Data = image.PixelData,
            DpiX = 96.0,
            DpiY = 96.0,
            Metadata =
            {
                ["BitDepth"] = image.BitDepth,
                ["ColorType"] = image.ColorType.ToString(),
            },
        };
    }

    public byte[] Encode(ImageData imageData, CodecOptions? options = null)
    {
        byte[] bgra = PixelConverter.ToBgra32(imageData.Data, imageData.Width, imageData.Height, imageData.PixelFormat);
        var image = new PngImage(imageData.Width, imageData.Height, 8, PngColorType.Rgba, bgra);

        PngColorType colorType = PixelConverter.HasAlpha(imageData.PixelFormat)
            ? PngColorType.Rgba
            : PngColorType.Rgb;

        return PngEncoder.Encode(image, colorType);
    }
}
