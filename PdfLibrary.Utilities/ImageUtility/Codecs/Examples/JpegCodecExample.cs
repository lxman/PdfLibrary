namespace ImageUtility.Codecs.Examples;

/// <summary>
/// Example showing how to implement separate JPEG decoder and encoder codecs.
/// This demonstrates using JpegLibrary for decoding and a custom implementation for encoding.
/// </summary>

// DECODER EXAMPLE - Uses existing JpegLibrary for decoding
public class JpegDecoderExample : IImageCodec
{
    public string Name => "JPEG Decoder (JpegLibrary)";
    public string[] Extensions => new[] { ".jpg", ".jpeg" };
    public bool CanDecode => true;   // This codec can ONLY decode
    public bool CanEncode => false;  // This codec CANNOT encode

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
        // Example implementation using JpegLibrary
        // (You would use the actual JpegLibrary API here)

        // var decoder = new JpegLibrary.JpegDecoder();
        // decoder.SetInput(data);
        // decoder.Identify();
        //
        // int width = decoder.Width;
        // int height = decoder.Height;
        // byte[] pixelData = ... // decode to RGB24 or other format

        return new ImageData
        {
            Width = 0,      // Would be actual width from decoder
            Height = 0,     // Would be actual height from decoder
            PixelFormat = PixelFormat.Rgb24,
            Data = Array.Empty<byte>(),  // Would be actual pixel data
            DpiX = 96.0,
            DpiY = 96.0
        };
    }

    public byte[] Encode(ImageData imageData, CodecOptions? options = null)
    {
        // This codec cannot encode - throw exception
        throw new NotSupportedException($"{Name} can only decode JPEG images.");
    }
}

// ENCODER EXAMPLE - Custom JPEG encoder implementation
public class JpegEncoderExample : IImageCodec
{
    public string Name => "JPEG Encoder (Custom)";
    public string[] Extensions => new[] { ".jpg", ".jpeg" };
    public bool CanDecode => false;  // This codec CANNOT decode
    public bool CanEncode => true;   // This codec can ONLY encode

    public bool CanHandle(ReadOnlySpan<byte> header)
    {
        // Encoder doesn't need to handle file signatures
        return false;
    }

    public ImageData Decode(byte[] data)
    {
        // This codec cannot decode - throw exception
        throw new NotSupportedException($"{Name} can only encode JPEG images.");
    }

    public byte[] Encode(ImageData imageData, CodecOptions? options = null)
    {
        // Example implementation of custom JPEG encoding
        // This is where you would implement:
        // 1. RGB to YCbCr color conversion
        // 2. Chroma subsampling (4:2:0, 4:2:2, etc.)
        // 3. DCT (Discrete Cosine Transform)
        // 4. Quantization
        // 5. Huffman encoding
        // 6. JPEG file format generation

        int quality = 95; // Default quality

        // Extract quality from options if provided
        if (options?.Options.TryGetValue("Quality", out object? qualityObj) == true)
        {
            quality = Convert.ToInt32(qualityObj);
        }

        // TODO: Implement actual JPEG encoding here
        // For now, return empty array as placeholder
        return Array.Empty<byte>();
    }
}

// REGISTRATION EXAMPLE
// In CodecRegistry.RegisterBuiltInCodecs(), you would register both:
//
//   Register(new JpegDecoderExample());  // Will be used for decoding .jpg files
//   Register(new JpegEncoderExample());  // Will be used for encoding .jpg files
//
// When you call CodecRegistry.Instance.DecodeFile("image.jpg"):
//   - Only JpegDecoderExample is considered (because CanDecode == true)
//   - JpegDecoderExample.Decode() is called
//
// When you call CodecRegistry.Instance.EncodeFile(imageData, "output.jpg"):
//   - Only JpegEncoderExample is considered (because CanEncode == true)
//   - JpegEncoderExample.Encode() is called
