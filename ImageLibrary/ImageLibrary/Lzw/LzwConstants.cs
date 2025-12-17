namespace ImageLibrary.Lzw;

/// <summary>
/// Constants used in LZW compression/decompression.
/// </summary>
public static class LzwConstants
{
    /// <summary>
    /// Initial code size in bits (after clear code and EOD code are added).
    /// </summary>
    public const int InitialCodeSize = 9;

    /// <summary>
    /// Maximum code size in bits.
    /// </summary>
    public const int MaxCodeSize = 12;

    /// <summary>
    /// Maximum number of entries in the dictionary (2^12 = 4096).
    /// </summary>
    public const int MaxDictionarySize = 4096;

    /// <summary>
    /// Clear code - resets the dictionary.
    /// </summary>
    public const int ClearCode = 256;

    /// <summary>
    /// End of data code.
    /// </summary>
    public const int EndOfDataCode = 257;

    /// <summary>
    /// First available code for dictionary entries.
    /// </summary>
    public const int FirstDictionaryCode = 258;

    /// <summary>
    /// Number of single-byte entries (0-255).
    /// </summary>
    public const int SingleByteEntries = 256;
}