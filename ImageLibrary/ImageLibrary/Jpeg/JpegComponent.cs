namespace ImageLibrary.Jpeg;

/// <summary>
/// Represents a single component in a JPEG image (Y, Cb, Cr for color; single for grayscale).
/// </summary>
internal class JpegComponent
{
    /// <summary>
    /// Component identifier (1=Y, 2=Cb, 3=Cr typically).
    /// </summary>
    public byte Id { get; set; }

    /// <summary>
    /// Horizontal sampling factor (1-4).
    /// </summary>
    public byte HorizontalSamplingFactor { get; set; }

    /// <summary>
    /// Vertical sampling factor (1-4).
    /// </summary>
    public byte VerticalSamplingFactor { get; set; }

    /// <summary>
    /// Quantization table index for this component.
    /// </summary>
    public byte QuantizationTableId { get; set; }

    /// <summary>
    /// DC Huffman table index (set during SOS parsing).
    /// </summary>
    public byte DcTableId { get; set; }

    /// <summary>
    /// AC Huffman table index (set during SOS parsing).
    /// </summary>
    public byte AcTableId { get; set; }

    public override string ToString()
    {
        return $"Component {Id}: {HorizontalSamplingFactor}x{VerticalSamplingFactor}, QT={QuantizationTableId}, DC={DcTableId}, AC={AcTableId}";
    }
}
