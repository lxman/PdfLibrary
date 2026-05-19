namespace PbmCodec;

/// <summary>
/// Netpbm format variants. The magic number at the start of the file
/// (P1..P6) selects one of these.
/// </summary>
public enum PbmFormat
{
    /// <summary>ASCII bitmap (1 bit per pixel, 0 = white, 1 = black). Magic "P1".</summary>
    AsciiBitmap = 1,

    /// <summary>ASCII graymap. Magic "P2".</summary>
    AsciiGraymap = 2,

    /// <summary>ASCII pixmap (RGB). Magic "P3".</summary>
    AsciiPixmap = 3,

    /// <summary>Binary bitmap (1 bit per pixel, packed MSB-first). Magic "P4".</summary>
    BinaryBitmap = 4,

    /// <summary>Binary graymap (1 or 2 bytes per pixel, big-endian). Magic "P5".</summary>
    BinaryGraymap = 5,

    /// <summary>Binary pixmap (3 or 6 bytes per pixel, big-endian RGB). Magic "P6".</summary>
    BinaryPixmap = 6,
}
