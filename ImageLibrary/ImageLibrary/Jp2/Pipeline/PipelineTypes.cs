namespace ImageLibrary.Jp2.Pipeline;

/// <summary>
/// Output from Tier-2 decoding: code-block bitstreams organized by resolution level and subband.
/// </summary>
public class Tier2Output
{
    /// <summary>Tile index this output belongs to.</summary>
    public int TileIndex { get; set; }

    /// <summary>Component index.</summary>
    public int ComponentIndex { get; set; }

    /// <summary>Number of resolution levels.</summary>
    public int ResolutionLevels { get; set; }

    /// <summary>
    /// Code-blocks organized by [resolution][subband][codeblock].
    /// Resolution 0 has only LL subband.
    /// Resolution r > 0 has HL, LH, HH subbands.
    /// </summary>
    public CodeBlockBitstream[][][] CodeBlocks { get; set; } = [];
}

/// <summary>
/// Bitstream data for a single code-block, with context information for decoding.
/// </summary>
public class CodeBlockBitstream
{
    /// <summary>Code-block X index within the subband.</summary>
    public int BlockX { get; set; }

    /// <summary>Code-block Y index within the subband.</summary>
    public int BlockY { get; set; }

    /// <summary>Width of the code-block in samples.</summary>
    public int Width { get; set; }

    /// <summary>Height of the code-block in samples.</summary>
    public int Height { get; set; }

    /// <summary>Number of coding passes included.</summary>
    public int CodingPasses { get; set; }

    /// <summary>Number of zero bit-planes (leading zeros in magnitude).</summary>
    public int ZeroBitPlanes { get; set; }

    /// <summary>The compressed bitstream data.</summary>
    public byte[] Data { get; set; } = [];

    /// <summary>Bit offset within the first byte.</summary>
    public int BitOffset { get; set; }

    /// <summary>Subband type (LL, HL, LH, HH) - affects context selection.</summary>
    public SubbandType SubbandType { get; set; }
}

/// <summary>
/// Quantized subband data ready for dequantization.
/// </summary>
public class QuantizedSubband
{
    /// <summary>Subband type (LL, HL, LH, or HH).</summary>
    public SubbandType Type { get; set; }

    /// <summary>Resolution level (0 = lowest).</summary>
    public int ResolutionLevel { get; set; }

    /// <summary>Width of the subband in samples.</summary>
    public int Width { get; set; }

    /// <summary>Height of the subband in samples.</summary>
    public int Height { get; set; }

    /// <summary>Quantization step size for this subband.</summary>
    public QuantizationStepSize StepSize { get; set; }

    /// <summary>Quantized coefficient values.</summary>
    public int[,] Coefficients { get; set; } = new int[0, 0];
}

/// <summary>
/// Subband types in the DWT decomposition.
/// </summary>
public enum SubbandType
{
    /// <summary>Low-Low (approximation) subband.</summary>
    LL,
    /// <summary>High-Low (horizontal detail) subband.</summary>
    HL,
    /// <summary>Low-High (vertical detail) subband.</summary>
    LH,
    /// <summary>High-High (diagonal detail) subband.</summary>
    HH,
}

/// <summary>
/// DWT coefficients for all subbands of a tile-component, ready for inverse transform.
/// </summary>
public class DwtCoefficients
{
    /// <summary>Component index.</summary>
    public int ComponentIndex { get; set; }

    /// <summary>Number of decomposition levels.</summary>
    public int DecompositionLevels { get; set; }

    /// <summary>Width at full resolution.</summary>
    public int Width { get; set; }

    /// <summary>Height at full resolution.</summary>
    public int Height { get; set; }

    /// <summary>
    /// Subbands organized by [level][subband].
    /// Level 0 contains only LL (the final approximation).
    /// Level n > 0 contains HL, LH, HH from decomposition level n.
    /// </summary>
    public double[][,] Subbands { get; set; } = [];
}

/// <summary>
/// Reconstructed tile data after inverse DWT.
/// </summary>
internal class ReconstructedTile
{
    /// <summary>Tile index.</summary>
    public int TileIndex { get; set; }

    /// <summary>Tile X position in the image.</summary>
    public int TileX { get; set; }

    /// <summary>Tile Y position in the image.</summary>
    public int TileY { get; set; }

    /// <summary>Tile width.</summary>
    public int Width { get; set; }

    /// <summary>Tile height.</summary>
    public int Height { get; set; }

    /// <summary>
    /// Component data [component][y, x].
    /// Values are floating-point before final conversion to integers.
    /// </summary>
    public double[][,] Components { get; set; } = [];
}
