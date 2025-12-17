namespace ImageLibrary.Lzw;

/// <summary>
/// Options for LZW encoding/decoding, particularly for PDF compatibility.
/// </summary>
public class LzwOptions
{
    /// <summary>
    /// Gets or sets whether to use early code size change.
    /// When true, the code size is increased before emitting the code that triggers it.
    /// When false, the code size is increased after emitting that code.
    /// Default is true (PDF default, also used by TIFF).
    /// GIF uses false.
    /// </summary>
    public bool EarlyChange { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to emit a clear code at the start of the stream.
    /// Required for some PDF readers (e.g., macOS Preview).
    /// Default is true.
    /// </summary>
    public bool EmitInitialClearCode { get; set; } = true;

    /// <summary>
    /// Gets or sets the bit order for reading/writing LZW codes.
    /// Default is MsbFirst (PDF/PostScript style).
    /// TIFF uses LsbFirst.
    /// </summary>
    public LzwBitOrder BitOrder { get; set; } = LzwBitOrder.MsbFirst;

    /// <summary>
    /// Creates default options suitable for PDF streams.
    /// </summary>
    public static LzwOptions PdfDefault => new LzwOptions
    {
        EarlyChange = true,
        EmitInitialClearCode = true
    };

    /// <summary>
    /// Creates options compatible with GIF format.
    /// </summary>
    public static LzwOptions GifCompatible => new LzwOptions
    {
        EarlyChange = false,
        EmitInitialClearCode = true
    };

    /// <summary>
    /// Creates options compatible with TIFF 6.0 format.
    /// TIFF 6.0 uses MSB-first bit order (FillOrder=1, default) and early code size change.
    /// Note: Some TIFF files may use FillOrder=2 (LSB-first) - check the FillOrder tag (266).
    /// </summary>
    public static LzwOptions TiffCompatible => new LzwOptions
    {
        EarlyChange = true,
        EmitInitialClearCode = false,
        BitOrder = LzwBitOrder.MsbFirst
    };
}