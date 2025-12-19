using ImageLibrary.Jp2;

namespace ImageUtility.Codecs;

/// <summary>
/// Custom JPEG2000 decoder using the ImageLibrary.Jp2 library.
/// Supports .jp2 and .j2k files (decode only).
/// </summary>
public class CustomJpeg2000Codec : IImageCodec
{
    public string Name => "Custom JPEG2000 Decoder (ImageLibrary)";
    public string[] Extensions => [".jp2", ".j2k", ".jpx", ".jpf"];
    public bool CanDecode => true;
    public bool CanEncode => false; // Encoder not implemented yet

    public bool CanHandle(ReadOnlySpan<byte> header)
    {
        return header.Length switch
        {
            // JP2 signature box: 0x0000000C 0x6A502020 0x0D0A870A
            // Or raw codestream: 0xFF4FFF51
            // Check for JP2 format signature
            >= 12 when header[4] == 0x6A && header[5] == 0x50 && header[6] == 0x20 && header[7] == 0x20 => true,
            < 4 => false,
            _ => header[0] == 0xFF && header[1] == 0x4F && header[2] == 0xFF && header[3] == 0x51
        };

        // Check for raw J2K codestream (SOC + SIZ markers)
    }

    public ImageData Decode(byte[] data)
    {
        // Decode using ImageLibrary.Jp2
        var decoder = new Jp2Decoder(data);
        byte[] pixelData = decoder.Decode();
        int width = decoder.Width;
        int height = decoder.Height;
        int components = decoder.ComponentCount;

        // Determine pixel format based on component count
        PixelFormat pixelFormat = components switch
        {
            1 => PixelFormat.Gray8,
            3 => PixelFormat.Rgb24,
            4 => PixelFormat.Rgba32,
            _ => throw new NotSupportedException($"JPEG2000 with {components} components not supported")
        };

        return new ImageData
        {
            Data = pixelData,
            Width = width,
            Height = height,
            PixelFormat = pixelFormat,
            DpiX = 96.0,
            DpiY = 96.0,
            Metadata = new Dictionary<string, object>
            {
                { "ComponentCount", components },
                { "Format", "JPEG2000" }
            }
        };
    }

    public byte[] Encode(ImageData imageData, CodecOptions? options = null)
    {
        // Encoding not supported yet
        throw new NotSupportedException($"{Name} can only decode JPEG2000 images. Encoding is not yet implemented.");
    }
}
