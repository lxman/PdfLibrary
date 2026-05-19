using TgaCodec;

namespace ImageUtility.Codecs;

/// <summary>
/// TGA codec backed by the in-house TgaCodec library.
/// </summary>
public class CustomTgaCodec : IImageCodec
{
    public string Name => "Custom TGA Codec (TgaCodec)";
    public string[] Extensions => [".tga"];
    public bool CanDecode => true;
    public bool CanEncode => true;

    public bool CanHandle(ReadOnlySpan<byte> header)
    {
        // TGA has no leading magic — validate the header byte fields instead:
        //   byte 1: color map type (0 = none, 1 = present)
        //   byte 2: image type (0 = none, 1/2/3 = uncompressed, 9/10/11 = RLE)
        if (header.Length < 3) return false;
        if (header[1] > 1) return false;
        byte imageType = header[2];
        return imageType is 0 or 1 or 2 or 3 or 9 or 10 or 11;
    }

    public ImageData Decode(byte[] data)
    {
        TgaImage image = TgaDecoder.Decode(data);

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
                ["BitsPerPixel"] = image.BitsPerPixel,
            },
        };
    }

    public byte[] Encode(ImageData imageData, CodecOptions? options = null)
    {
        var useRle = false;
        if (options?.Options.TryGetValue("UseRle", out object? rle) == true)
            useRle = Convert.ToBoolean(rle);

        byte[] bgra = PixelConverter.ToBgra32(imageData.Data, imageData.Width, imageData.Height, imageData.PixelFormat);
        var image = new TgaImage(imageData.Width, imageData.Height, 32, bgra);

        int bitsPerPixel = PixelConverter.HasAlpha(imageData.PixelFormat) ? 32 : 24;
        return TgaEncoder.Encode(image, bitsPerPixel, useRle);
    }
}
