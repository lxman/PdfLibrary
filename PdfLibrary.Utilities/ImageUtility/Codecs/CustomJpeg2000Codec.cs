using Compressors.Jpeg2000;

namespace ImageUtility.Codecs;

/// <summary>
/// Custom JPEG2000 decoder using the Compressors.Jpeg2000 library.
/// Supports .jp2 and .j2k files (decode only).
/// </summary>
public class CustomJpeg2000Codec : IImageCodec
{
    public string Name => "Custom JPEG2000 Decoder (Compressors.Jpeg2000)";
    public string[] Extensions => new[] { ".jp2", ".j2k", ".jpx", ".jpf" };
    public bool CanDecode => true;
    public bool CanEncode => false; // Encoder not implemented yet

    public bool CanHandle(ReadOnlySpan<byte> header)
    {
        // JP2 signature box: 0x0000000C 0x6A502020 0x0D0A870A
        // Or raw codestream: 0xFF4FFF51
        if (header.Length >= 12)
        {
            // Check for JP2 format signature
            if (header[4] == 0x6A && header[5] == 0x50 &&
                header[6] == 0x20 && header[7] == 0x20)
            {
                return true;
            }
        }

        if (header.Length >= 4)
        {
            // Check for raw J2K codestream (SOC + SIZ markers)
            if (header[0] == 0xFF && header[1] == 0x4F &&
                header[2] == 0xFF && header[3] == 0x51)
            {
                return true;
            }
        }

        return false;
    }

    public ImageData Decode(byte[] data)
    {
        // Decode using Compressors.Jpeg2000
        byte[] pixelData = Jpeg2000.Decompress(data, out int width, out int height, out int components);

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
            DpiX = 96.0, // JPEG2000 may include resolution, but defaulting to 96
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
