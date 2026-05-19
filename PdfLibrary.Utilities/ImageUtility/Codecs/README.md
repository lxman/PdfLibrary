# Image Codecs Architecture

This directory contains the pluggable codec architecture for ImageUtility.

## Overview

The codec system supports custom image format implementations that you own and control. As of the current release every supported format is backed by an in-tree codec from `ImageLibrary/`; ImageUtility has no third-party image-format dependencies.

## Architecture

### Core Interfaces

- **IImageCodec** - Main interface that all codecs must implement
  - `CanDecode` / `CanEncode` - Capabilities
  - `Decode(byte[])` - Decode image bytes to ImageData
  - `Encode(ImageData)` - Encode ImageData to image bytes
  - `CanHandle(ReadOnlySpan<byte>)` - Check file magic bytes

- **ImageData** - Standard format for decoded images
  - Raw pixel data
  - Width, height, pixel format
  - DPI information
  - Metadata dictionary

- **CodecRegistry** - Singleton that manages all codecs
  - `Register()` / `Unregister()` - Manage codecs
  - `FindByExtension()` - Find codec by file extension
  - `FindByFileSignature()` - Find codec by magic bytes
  - `DecodeFile()` / `EncodeFile()` - High-level file operations

### Pixel Formats

Supported pixel formats (can be extended):
- `Gray8` - 8-bit grayscale
- `Rgb24` - 24-bit RGB (3 bytes per pixel)
- `Rgba32` - 32-bit RGBA (4 bytes per pixel)
- `Bgr24` - 24-bit BGR (3 bytes per pixel)
- `Bgra32` - 32-bit BGRA (4 bytes per pixel)
- `Cmyk32` - 32-bit CMYK (4 bytes per pixel)

## Adding a New Codec

To add a custom codec implementation:

1. **Create codec class** implementing `IImageCodec`:

```csharp
public class JpegCodec : IImageCodec
{
    public string Name => "JPEG";
    public string[] Extensions => new[] { ".jpg", ".jpeg" };
    public bool CanDecode => true;
    public bool CanEncode => true;

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
        // Implement JPEG decoding
        // Return ImageData with decoded pixels
    }

    public byte[] Encode(ImageData imageData, CodecOptions? options = null)
    {
        // Implement JPEG encoding
        // Return encoded bytes
    }
}
```

2. **Register the codec** in `CodecRegistry.RegisterBuiltInCodecs()`:

```csharp
private void RegisterBuiltInCodecs()
{
    Register(new JpegCodec());
    Register(new PngCodec());
    // etc.
}
```

### Separate Encode/Decode Codecs

You can register **different codecs for encoding vs decoding** the same format. This is useful when:
- You have a mature third-party decoder but want to implement your own encoder
- Encoding and decoding have very different implementations
- You want to use one library for decoding and another for encoding

**Example:**

```csharp
// Decoder-only codec using JpegDecoder
public class JpegDecoder : IImageCodec
{
    public string Name => "JPEG Decoder";
    public string[] Extensions => new[] { ".jpg", ".jpeg" };
    public bool CanDecode => true;   // Can decode
    public bool CanEncode => false;  // Cannot encode

    public bool CanHandle(ReadOnlySpan<byte> header) { /* ... */ }
    public ImageData Decode(byte[] data) { /* Use JpegDecoder */ }
    public byte[] Encode(ImageData imageData, CodecOptions? options = null)
    {
        throw new NotSupportedException("This codec can only decode.");
    }
}

// Encoder-only codec with custom implementation
public class JpegEncoder : IImageCodec
{
    public string Name => "JPEG Encoder";
    public string[] Extensions => new[] { ".jpg", ".jpeg" };
    public bool CanDecode => false;  // Cannot decode
    public bool CanEncode => true;   // Can encode

    public bool CanHandle(ReadOnlySpan<byte> header) { return false; }
    public ImageData Decode(byte[] data)
    {
        throw new NotSupportedException("This codec can only encode.");
    }
    public byte[] Encode(ImageData imageData, CodecOptions? options = null)
    {
        // Custom JPEG encoding implementation
    }
}

// Register both - the registry will automatically pick the right one
private void RegisterBuiltInCodecs()
{
    Register(new JpegDecoder());  // Used for decoding .jpg files
    Register(new JpegEncoder());  // Used for encoding .jpg files
}
```

The `CodecRegistry` automatically selects the appropriate codec based on the operation:
- `DecodeFile()` only considers codecs where `CanDecode == true`
- `EncodeFile()` only considers codecs where `CanEncode == true`

## Registered Codecs

All formats supported by ImageUtility are backed by in-house codecs from `ImageLibrary/`:

| Codec class | Backing library | Formats | Decode | Encode |
|---|---|---|---|---|
| `CustomJpegCodec` | `ImageLibrary/JpegCodec` | `.jpg`, `.jpeg` | ✓ (baseline + progressive, 1/3/4 components) | ✓ |
| `CustomPngCodec` | `ImageLibrary/PngCodec` | `.png` | ✓ | ✓ |
| `CustomBmpCodec` | `ImageLibrary/BmpCodec` | `.bmp` | ✓ | ✓ |
| `CustomGifCodec` | `ImageLibrary/GifCodec` | `.gif` | ✓ (first frame for animated) | ✓ |
| `CustomTgaCodec` | `ImageLibrary/TgaCodec` | `.tga` | ✓ | ✓ |
| `CustomTiffCodec` | `ImageLibrary/TiffCodec` | `.tif`, `.tiff` | ✓ | ✓ (LZW default) |
| `CustomJpeg2000Codec` | `ImageLibrary/Jp2Codec` | `.jp2`, `.j2k`, `.jpx`, `.jpf` | ✓ | — |
| `CustomPbmCodec` | `ImageLibrary/PbmCodec` | `.pbm`, `.pgm`, `.ppm`, `.pnm` | ✓ (P1–P6) | ✓ (P4/P5/P6 binary) |

`MainWindow` still uses WPF's `BitmapImage` for on-screen display, but every transcoding operation that flows through `CodecRegistry.DecodeFile` / `EncodeFile` runs through one of the in-house codecs above.

## Benefits

- **Full source control** — own and maintain all codec code
- **No third-party image dependencies** — `ImageUtility.csproj` references only its own per-codec libraries
- **Customizable** — add format extensions or optimizations as needed
- **Independent updates** — fix bugs and add features on your schedule
- **Cross-format operations** — unified API for all image formats
