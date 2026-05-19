using System.IO;
using GifCodec;

namespace ImageUtility.Codecs;

/// <summary>
/// GIF codec backed by the in-house GifCodec library.
/// Single-frame decode and encode; for animated GIFs, only the first frame is returned.
/// </summary>
public class CustomGifCodec : IImageCodec
{
    public string Name => "Custom GIF Codec (GifCodec)";
    public string[] Extensions => [".gif"];
    public bool CanDecode => true;
    public bool CanEncode => true;

    public bool CanHandle(ReadOnlySpan<byte> header)
    {
        // "GIF87a" or "GIF89a"
        if (header.Length < 6) return false;
        return header[0] == 'G' && header[1] == 'I' && header[2] == 'F'
            && header[3] == '8' && (header[4] == '7' || header[4] == '9')
            && header[5] == 'a';
    }

    public ImageData Decode(byte[] data)
    {
        GifFile file = GifDecoder.Decode(data);
        GifImage? frame = file.FirstFrame
            ?? throw new InvalidDataException("GIF contained no frames");

        return new ImageData
        {
            Width = frame.Width,
            Height = frame.Height,
            PixelFormat = PixelFormat.Bgra32,
            Data = frame.PixelData,
            DpiX = 96.0,
            DpiY = 96.0,
            Metadata =
            {
                ["FrameCount"] = file.Frames.Count,
                ["LoopCount"] = file.LoopCount,
            },
        };
    }

    public byte[] Encode(ImageData imageData, CodecOptions? options = null)
    {
        var maxColors = 256;
        if (options?.Options.TryGetValue("MaxColors", out object? mc) == true)
            maxColors = Convert.ToInt32(mc);

        byte[] bgra = PixelConverter.ToBgra32(imageData.Data, imageData.Width, imageData.Height, imageData.PixelFormat);
        var image = new GifImage(imageData.Width, imageData.Height, bgra);
        return GifEncoder.Encode(image, maxColors);
    }
}
