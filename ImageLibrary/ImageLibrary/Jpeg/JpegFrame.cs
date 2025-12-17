using System.Linq;

namespace ImageLibrary.Jpeg;

/// <summary>
/// Represents a parsed JPEG frame with all header information.
/// </summary>
internal class JpegFrame
{
    /// <summary>
    /// Frame type from SOF marker (0=baseline, 2=progressive).
    /// </summary>
    public byte FrameType { get; set; }

    /// <summary>
    /// Sample precision in bits (typically 8).
    /// </summary>
    public byte Precision { get; set; }

    /// <summary>
    /// Image width in pixels.
    /// </summary>
    public ushort Width { get; set; }

    /// <summary>
    /// Image height in pixels.
    /// </summary>
    public ushort Height { get; set; }

    /// <summary>
    /// True if this is a progressive JPEG.
    /// </summary>
    public bool IsProgressive => FrameType == JpegMarker.SOF2;

    /// <summary>
    /// True if this is a baseline JPEG.
    /// </summary>
    public bool IsBaseline => FrameType == JpegMarker.SOF0;

    /// <summary>
    /// Components in the frame.
    /// </summary>
    public JpegComponent[] Components { get; set; } = [];

    /// <summary>
    /// Number of components (1=grayscale, 3=YCbCr, 4=CMYK).
    /// </summary>
    public int ComponentCount => Components.Length;

    /// <summary>
    /// Maximum horizontal sampling factor across all components.
    /// </summary>
    public int MaxHorizontalSamplingFactor { get; set; }

    /// <summary>
    /// Maximum vertical sampling factor across all components.
    /// </summary>
    public int MaxVerticalSamplingFactor { get; set; }

    /// <summary>
    /// Quantization tables (up to 4).
    /// </summary>
    public ushort[]?[] QuantizationTables { get; } = new ushort[4][];

    /// <summary>
    /// DC Huffman tables (up to 4).
    /// </summary>
    public HuffmanTableSpec?[] DcHuffmanTables { get; } = new HuffmanTableSpec[4];

    /// <summary>
    /// AC Huffman tables (up to 4).
    /// </summary>
    public HuffmanTableSpec?[] AcHuffmanTables { get; } = new HuffmanTableSpec[4];

    /// <summary>
    /// Restart interval (0 if not set).
    /// </summary>
    public ushort RestartInterval { get; set; }

    /// <summary>
    /// Offset in the file where the entropy-coded data begins.
    /// </summary>
    public int EntropyDataOffset { get; set; }

    /// <summary>
    /// Length of the entropy-coded data in bytes.
    /// </summary>
    public int EntropyDataLength { get; set; }

    /// <summary>
    /// Number of MCU columns (width in MCUs).
    /// </summary>
    public int McuCountX => (Width + (MaxHorizontalSamplingFactor * 8) - 1) / (MaxHorizontalSamplingFactor * 8);

    /// <summary>
    /// Number of MCU rows (height in MCUs).
    /// </summary>
    public int McuCountY => (Height + (MaxVerticalSamplingFactor * 8) - 1) / (MaxVerticalSamplingFactor * 8);

    /// <summary>
    /// Total number of MCUs.
    /// </summary>
    public int McuCount => McuCountX * McuCountY;
}

/// <summary>
/// Holds the raw Huffman table specification as read from the file.
/// </summary>
internal class HuffmanTableSpec
{
    /// <summary>
    /// Number of codes for each bit length (1-16).
    /// </summary>
    public byte[] CodeCounts { get; set; } = new byte[16];

    /// <summary>
    /// Symbol values in order.
    /// </summary>
    public byte[] Symbols { get; set; } = [];

    /// <summary>
    /// Total number of codes in this table.
    /// </summary>
    public int TotalCodes => CodeCounts.Sum(c => c);
}
