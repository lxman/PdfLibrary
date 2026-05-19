using TiffCodec;

namespace ImageUtility.Codecs;

/// <summary>
/// TIFF codec backed by the in-house TiffCodec library.
/// </summary>
public class CustomTiffCodec : IImageCodec
{
    public string Name => "Custom TIFF Codec (TiffCodec)";
    public string[] Extensions => [".tif", ".tiff"];
    public bool CanDecode => true;
    public bool CanEncode => true;

    public bool CanHandle(ReadOnlySpan<byte> header)
    {
        if (header.Length < 4) return false;
        // Little-endian: "II" 0x2A 0x00
        if (header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x2A && header[3] == 0x00)
            return true;
        // Big-endian: "MM" 0x00 0x2A
        if (header[0] == 0x4D && header[1] == 0x4D && header[2] == 0x00 && header[3] == 0x2A)
            return true;
        return false;
    }

    public ImageData Decode(byte[] data)
    {
        TiffImage image = TiffDecoder.Decode(data);

        return new ImageData
        {
            Width = image.Width,
            Height = image.Height,
            PixelFormat = PixelFormat.Bgra32,
            Data = image.PixelData,
            DpiX = 96.0,
            DpiY = 96.0,
        };
    }

    public byte[] Encode(ImageData imageData, CodecOptions? options = null)
    {
        var compression = TiffCompression.Lzw;
        if (options?.Options.TryGetValue("Compression", out object? c) == true && c is TiffCompression tc)
            compression = tc;

        byte[] bgra = PixelConverter.ToBgra32(imageData.Data, imageData.Width, imageData.Height, imageData.PixelFormat);
        var image = new TiffImage(imageData.Width, imageData.Height, bgra);
        return TiffEncoder.Encode(image, compression);
    }
}
