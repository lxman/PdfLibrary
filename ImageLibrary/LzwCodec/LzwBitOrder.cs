#nullable enable

namespace LzwCodec
{
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
        /// Least significant bit first (TIFF FillOrder=2).
        /// </summary>
        LsbFirst
    }
}
