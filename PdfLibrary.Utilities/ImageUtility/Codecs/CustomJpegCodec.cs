using ImageLibrary.Jpeg;

namespace ImageUtility.Codecs;

/// <summary>
/// Custom JPEG codec using the ImageLibrary.Jpeg library.
/// This is the preferred codec for JPEG operations when available.
/// </summary>
public class CustomJpegCodec : IImageCodec
{
    public string Name => "Custom JPEG (ImageLibrary)";
    public string[] Extensions => [".jpg", ".jpeg"];
    public bool CanDecode => true;
    public bool CanEncode => false; // Encoder not implemented in ImageLibrary yet

    public bool CanHandle(ReadOnlySpan<byte> header)
    {
        // JPEG magic bytes: FF D8 FF
        return header.Length >= 3
            && header[0] == 0xFF
            && header[1] == 0xD8
            && header[2] == 0xFF;
    }

    public ImageData Decode(byte[] data)
    {
        // Decode using ImageLibrary.Jpeg
        var decoder = new JpegDecoder(data);
        var result = decoder.Decode();

        // ImageLibrary always decodes to RGB24
        return new ImageData
        {
            Data = result.RgbData,
            Width = result.Width,
            Height = result.Height,
            PixelFormat = PixelFormat.Rgb24,
            DpiX = 96.0,
            DpiY = 96.0,
            Metadata = new Dictionary<string, object>
            {
                { "Format", "JPEG" }
            }
        };
    }

    public byte[] Encode(ImageData imageData, CodecOptions? options = null)
    {
        throw new NotSupportedException($"{Name} can only decode JPEG images. Encoding is not yet implemented in ImageLibrary.");
    }
}
