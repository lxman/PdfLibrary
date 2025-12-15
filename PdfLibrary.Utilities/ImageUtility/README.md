# ImageUtility - Image Viewer and Transcoder

A WPF application for viewing and transcoding images between different formats using owned codec implementations.

## Overview

ImageUtility is designed to provide:
- **Image viewing** with zoom controls and pan/scroll support
- **Format transcoding** - Convert images between formats
- **Owned codecs** - Full control over codec source code, updates, and fixes
- **No licensing issues** - No reliance on third-party libraries
- **Extensible architecture** - Easy to add new format support

## Current Features

### Image Viewing
- Open images from file (File → Open or Ctrl+O)
- Display images with scroll/pan support
- Zoom controls:
  - Zoom In (Ctrl++) - Increase zoom by 10%
  - Zoom Out (Ctrl+-) - Decrease zoom by 10%
  - Actual Size (Ctrl+0) - Reset to 100%
  - Fit to Window (Ctrl+F) - Scale to fit viewport
- Status bar showing:
  - Current file name
  - Image dimensions
  - Pixel format
  - DPI information
  - Current zoom level

### Image Transcoding
- Save As (Ctrl+S) - Convert to different formats:
  - PNG - Lossless compression
  - JPEG - Lossy compression (95% quality)
  - BMP - Uncompressed bitmap
  - TIFF - Tagged Image File Format
  - GIF - Graphics Interchange Format

### Architecture

The application uses a pluggable codec architecture:

```
ImageUtility/
├── MainWindow.xaml         - UI layout
├── MainWindow.xaml.cs      - UI logic and file handling
└── Codecs/
    ├── IImageCodec.cs      - Core codec interface
    ├── ImageData.cs        - Standard image data format
    ├── CodecRegistry.cs    - Codec management
    └── README.md           - Codec architecture docs
```

## Codec System

The codec system is designed for custom implementations:

### IImageCodec Interface
Each codec implements:
- `Name` - Codec identifier (e.g., "JPEG")
- `Extensions` - Supported file extensions
- `CanDecode` / `CanEncode` - Capability flags
- `CanHandle(bytes)` - Magic byte detection
- `Decode(bytes)` - Decode to ImageData
- `Encode(ImageData)` - Encode to bytes

### ImageData Format
Standard format for all codecs:
```csharp
public class ImageData
{
    byte[] Data;              // Raw pixels
    int Width, Height;        // Dimensions
    PixelFormat PixelFormat;  // RGB24, RGBA32, etc.
    double DpiX, DpiY;        // Resolution
    Dictionary Metadata;      // Additional info
}
```

### CodecRegistry
Central registry managing all codecs:
- Singleton instance
- Auto-detects format by magic bytes or extension
- Provides high-level DecodeFile/EncodeFile methods
- Easy codec registration

## Current Status

**Phase 1: Complete** ✓
- WPF application shell
- Image viewing UI
- Zoom and pan controls
- File open/save dialogs
- Status bar with image info
- Codec architecture defined

**Phase 2: In Progress**
- Custom codec implementations
  - JPEG codec (can leverage existing JpegLibrary code)
  - PNG codec
  - TIFF codec
  - BMP codec
  - GIF codec

**Current Behavior:**
The application currently uses WPF's built-in codecs for loading and saving. As custom codec implementations are added to the `Codecs/` directory and registered in `CodecRegistry.RegisterBuiltInCodecs()`, they will take priority over the built-in ones.

## Building and Running

### Prerequisites
- .NET 10.0 SDK
- Windows (WPF requirement)

### Build
```powershell
cd C:\Users\jorda\RiderProjects\PDF\PdfLibrary.Utilities\ImageUtility
dotnet build
```

### Run
```powershell
dotnet run
```

Or build and run the executable:
```powershell
dotnet build
.\bin\Debug\net10.0-windows\ImageUtility.exe
```

## Adding Custom Codecs

To add a custom codec implementation:

1. Create a new class implementing `IImageCodec` in the `Codecs/` directory
2. Implement all required methods (Decode, Encode, CanHandle, etc.)
3. Register the codec in `CodecRegistry.RegisterBuiltInCodecs()`
4. Rebuild and test

See `Codecs/README.md` for detailed instructions and examples.

## Future Enhancements

Planned features:
- Batch conversion (multiple files)
- Image editing tools (crop, rotate, resize)
- Metadata viewer/editor
- Color space conversion
- Image comparison (side-by-side)
- Drag-and-drop file support
- Recent files list
- Codec settings/options UI
- Performance profiling for codecs

## Integration with PdfLibrary

The codec implementations developed for ImageUtility can be shared with the PdfLibrary project, enabling:
- Consistent image handling across projects
- Shared codec maintenance
- Unified bug fixes and improvements
- No duplicate codec development

This is particularly relevant for:
- JPEG decoding in PDF DCTDecode filter
- TIFF handling in PDF image XObjects
- General image processing utilities
