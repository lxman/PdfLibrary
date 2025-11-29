namespace PdfLibrary.Filters;

/// <summary>
/// Interface for PDF stream filters (ISO 32000-1:2008 section 7.4)
/// Filters encode/decode stream data
/// </summary>
internal interface IStreamFilter
{
    /// <summary>
    /// Gets the filter name as it appears in PDF (/FlateDecode, /ASCIIHexDecode, etc.)
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Encodes (compresses/transforms) data
    /// </summary>
    byte[] Encode(byte[] data);

    /// <summary>
    /// Decodes (decompresses/transforms) data
    /// </summary>
    byte[] Decode(byte[] data);

    /// <summary>
    /// Decodes data with optional decode parameters
    /// </summary>
    byte[] Decode(byte[] data, Dictionary<string, object>? parameters);
}
