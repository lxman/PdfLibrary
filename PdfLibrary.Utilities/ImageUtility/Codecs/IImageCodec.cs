namespace ImageUtility.Codecs;

/// <summary>
/// Interface for image codec implementations (encoder and/or decoder).
/// </summary>
public interface IImageCodec
{
    /// <summary>
    /// Gets the codec name (e.g., "JPEG", "PNG", "TIFF").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the file extensions supported by this codec (e.g., [".jpg", ".jpeg"]).
    /// </summary>
    string[] Extensions { get; }

    /// <summary>
    /// Gets whether this codec can decode images.
    /// </summary>
    bool CanDecode { get; }

    /// <summary>
    /// Gets whether this codec can encode images.
    /// </summary>
    bool CanEncode { get; }

    /// <summary>
    /// Checks if the codec can handle the given file based on its magic bytes.
    /// </summary>
    /// <param name="header">First bytes of the file (at least 16 bytes recommended).</param>
    /// <returns>True if this codec can handle the file.</returns>
    bool CanHandle(ReadOnlySpan<byte> header);

    /// <summary>
    /// Decodes an image from raw bytes.
    /// </summary>
    /// <param name="data">Encoded image data.</param>
    /// <returns>Decoded image information.</returns>
    ImageData Decode(byte[] data);

    /// <summary>
    /// Encodes an image to raw bytes.
    /// </summary>
    /// <param name="imageData">Image data to encode.</param>
    /// <param name="options">Codec-specific encoding options.</param>
    /// <returns>Encoded image bytes.</returns>
    byte[] Encode(ImageData imageData, CodecOptions? options = null);
}

/// <summary>
/// Represents decoded image data with metadata.
/// </summary>
public class ImageData
{
    /// <summary>
    /// Raw pixel data (format specified by PixelFormat).
    /// </summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Image width in pixels.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Image height in pixels.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Pixel format (e.g., RGB24, RGBA32, Gray8).
    /// </summary>
    public PixelFormat PixelFormat { get; set; }

    /// <summary>
    /// Horizontal DPI (dots per inch).
    /// </summary>
    public double DpiX { get; set; } = 96.0;

    /// <summary>
    /// Vertical DPI (dots per inch).
    /// </summary>
    public double DpiY { get; set; } = 96.0;

    /// <summary>
    /// Additional metadata from the image file.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Pixel format enumeration.
/// </summary>
public enum PixelFormat
{
    /// <summary>8-bit grayscale (1 byte per pixel).</summary>
    Gray8,

    /// <summary>24-bit RGB (3 bytes per pixel: R, G, B).</summary>
    Rgb24,

    /// <summary>32-bit RGBA (4 bytes per pixel: R, G, B, A).</summary>
    Rgba32,

    /// <summary>24-bit BGR (3 bytes per pixel: B, G, R).</summary>
    Bgr24,

    /// <summary>32-bit BGRA (4 bytes per pixel: B, G, R, A).</summary>
    Bgra32,

    /// <summary>32-bit CMYK (4 bytes per pixel: C, M, Y, K).</summary>
    Cmyk32
}

/// <summary>
/// Base class for codec-specific encoding options.
/// </summary>
public class CodecOptions
{
    /// <summary>
    /// Generic options dictionary for codec-specific parameters.
    /// </summary>
    public Dictionary<string, object> Options { get; set; } = new();
}
