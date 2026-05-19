using PbmCodec;

namespace ImageUtility.Codecs;

/// <summary>
/// Netpbm codec (PBM / PGM / PPM, ASCII + binary) backed by the in-house PbmCodec library.
/// Decode handles P1–P6; encode emits binary forms (P6 by default).
/// </summary>
public class CustomPbmCodec : IImageCodec
{
    public string Name => "Custom PBM/PGM/PPM Codec (PbmCodec)";
    public string[] Extensions => [".pbm", ".pgm", ".ppm", ".pnm"];
    public bool CanDecode => true;
    public bool CanEncode => true;

    public bool CanHandle(ReadOnlySpan<byte> header)
    {
        if (header.Length < 2) return false;
        return header[0] == (byte)'P' && header[1] >= (byte)'1' && header[1] <= (byte)'6';
    }

    public ImageData Decode(byte[] data)
    {
        PbmImage image = PbmDecoder.Decode(data);

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
                ["NetpbmVariant"] = $"P{(int)image.SourceFormat}",
            },
        };
    }

    public byte[] Encode(ImageData imageData, CodecOptions? options = null)
    {
        var format = PbmFormat.BinaryPixmap;
        if (options?.Options.TryGetValue("Format", out object? f) == true && f is PbmFormat pf)
            format = pf;

        byte[] bgra = PixelConverter.ToBgra32(imageData.Data, imageData.Width, imageData.Height, imageData.PixelFormat);
        var image = new PbmImage(imageData.Width, imageData.Height, bgra);
        return PbmEncoder.Encode(image, format);
    }
}
