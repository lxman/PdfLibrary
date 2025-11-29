namespace PdfLibrary.Filters;

/// <summary>
/// Factory for creating stream filters by name
/// </summary>
internal static class StreamFilterFactory
{
    private static readonly Dictionary<string, Func<IStreamFilter>> Filters = new()
    {
        { "FlateDecode", () => new FlateDecodeFilter() },
        { "Fl", () => new FlateDecodeFilter() }, // Abbreviation
        { "ASCIIHexDecode", () => new AsciiHexDecodeFilter() },
        { "AHx", () => new AsciiHexDecodeFilter() }, // Abbreviation
        { "ASCII85Decode", () => new Ascii85DecodeFilter() },
        { "A85", () => new Ascii85DecodeFilter() }, // Abbreviation
        { "RunLengthDecode", () => new RunLengthDecodeFilter() },
        { "RL", () => new RunLengthDecodeFilter() }, // Abbreviation
        { "LZWDecode", () => new LzwDecodeFilter() },
        { "LZW", () => new LzwDecodeFilter() }, // Abbreviation
        { "DCTDecode", () => new DctDecodeFilter() },
        { "DCT", () => new DctDecodeFilter() }, // Abbreviation
        { "CCITTFaxDecode", () => new CcittFaxDecodeFilter() },
        { "CCF", () => new CcittFaxDecodeFilter() }, // Abbreviation
        { "JBIG2Decode", () => new Jbig2DecodeFilter() },
        { "JPXDecode", () => new JpxDecodeFilter() },
    };

    /// <summary>
    /// Creates a filter by name
    /// </summary>
    public static IStreamFilter? CreateFilter(string filterName)
    {
        if (string.IsNullOrEmpty(filterName))
            return null;

        return Filters.TryGetValue(filterName, out Func<IStreamFilter>? factory) ? factory() : null;
    }

    /// <summary>
    /// Checks if a filter is supported
    /// </summary>
    public static bool IsSupported(string filterName)
    {
        return !string.IsNullOrEmpty(filterName) && Filters.ContainsKey(filterName);
    }

    /// <summary>
    /// Gets all supported filter names
    /// </summary>
    public static IEnumerable<string> GetSupportedFilters()
    {
        return Filters.Keys;
    }
}
