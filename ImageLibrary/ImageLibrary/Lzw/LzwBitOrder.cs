namespace ImageLibrary.Lzw;

/// <summary>
/// Specifies the bit order for reading LZW codes.
/// </summary>
public enum LzwBitOrder
{
    /// <summary>
    /// Most significant bit first (PDF, PostScript, GIF).
    /// </summary>
    MsbFirst,

    /// <summary>
    /// Least significant bit first (TIFF).
    /// </summary>
    LsbFirst
}
