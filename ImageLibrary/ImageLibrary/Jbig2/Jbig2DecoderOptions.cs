namespace ImageLibrary.Jbig2;

/// <summary>
/// Configuration options for the JBIG2 decoder with resource limits.
/// </summary>
public sealed class Jbig2DecoderOptions
{
    /// <summary>
    /// Maximum width of a page or region in pixels.
    /// Default: 65536 (64K pixels)
    /// </summary>
    public int MaxWidth { get; set; } = 65536;

    /// <summary>
    /// Maximum height of a page or region in pixels.
    /// Default: 65536 (64K pixels)
    /// </summary>
    public int MaxHeight { get; set; } = 65536;

    /// <summary>
    /// Maximum total pixels (width * height) for a single bitmap.
    /// Default: 256 million pixels (~30MB for bi-level)
    /// </summary>
    public long MaxPixels { get; set; } = 256_000_000;

    /// <summary>
    /// Maximum number of segments to process.
    /// Default: 65536
    /// </summary>
    public int MaxSegments { get; set; } = 65536;

    /// <summary>
    /// Maximum number of pages.
    /// Default: 65536
    /// </summary>
    public int MaxPages { get; set; } = 65536;

    /// <summary>
    /// Maximum segment data length in bytes.
    /// Default: 100 MB
    /// </summary>
    public long MaxSegmentDataLength { get; set; } = 100 * 1024 * 1024;

    /// <summary>
    /// Maximum number of referred-to segments.
    /// Default: 65536
    /// </summary>
    public int MaxReferredSegments { get; set; } = 65536;

    /// <summary>
    /// Maximum decode operations (bits decoded) before aborting.
    /// Prevents CPU exhaustion from malicious streams.
    /// Default: 1 billion operations
    /// </summary>
    public long MaxDecodeOperations { get; set; } = 1_000_000_000;

    /// <summary>
    /// Maximum number of lines in a custom Huffman table.
    /// Default: 4096
    /// </summary>
    public int MaxHuffmanTableLines { get; set; } = 4096;

    /// <summary>
    /// Maximum number of symbols in a symbol dictionary.
    /// Default: 65536
    /// </summary>
    public int MaxSymbols { get; set; } = 65536;

    /// <summary>
    /// Maximum iterations for any single decoding loop.
    /// Prevents infinite loops from malformed data.
    /// Default: 10 million
    /// </summary>
    public int MaxLoopIterations { get; set; } = 10_000_000;

    /// <summary>
    /// Default options suitable for most use cases.
    /// </summary>
    public static Jbig2DecoderOptions Default { get; } = new();

    /// <summary>
    /// Strict options with lower limits for untrusted input.
    /// </summary>
    public static Jbig2DecoderOptions Strict { get; } = new()
    {
        MaxWidth = 16384,
        MaxHeight = 16384,
        MaxPixels = 64_000_000,
        MaxSegments = 1024,
        MaxPages = 100,
        MaxSegmentDataLength = 10 * 1024 * 1024,
        MaxReferredSegments = 256,
        MaxDecodeOperations = 100_000_000,
        MaxHuffmanTableLines = 1024,
        MaxSymbols = 16384,
        MaxLoopIterations = 1_000_000
    };

    /// <summary>
    /// Validates that dimensions are within limits.
    /// </summary>
    public void ValidateDimensions(int width, int height, string context)
    {
        if (width <= 0)
            throw new Jbig2DataException($"{context}: Invalid width {width}");
        if (height <= 0)
            throw new Jbig2DataException($"{context}: Invalid height {height}");
        if (width > MaxWidth)
            throw new Jbig2ResourceException($"{context}: Width {width} exceeds limit {MaxWidth}");
        if (height > MaxHeight)
            throw new Jbig2ResourceException($"{context}: Height {height} exceeds limit {MaxHeight}");

        long pixels = (long)width * height;
        if (pixels > MaxPixels)
            throw new Jbig2ResourceException($"{context}: Pixel count {pixels} exceeds limit {MaxPixels}");
    }
}
