using System;

namespace ImageLibrary.Jp2;

/// <summary>
/// Coding style parameters from COD marker.
/// </summary>
public class CodingParameters
{
    /// <summary>Coding style (Scod).</summary>
    public CodingStyle Style { get; set; }

    /// <summary>Progression order.</summary>
    public ProgressionOrder Progression { get; set; }

    /// <summary>Number of quality layers.</summary>
    public int LayerCount { get; set; }

    /// <summary>Multiple component transform (0 = none, 1 = reversible, 2 = irreversible).</summary>
    public int MultipleComponentTransform { get; set; }

    /// <summary>Number of decomposition levels (NL).</summary>
    public int DecompositionLevels { get; set; }

    /// <summary>Code-block width exponent (xcb + 2, actual width = 2^(xcb+2)).</summary>
    public int CodeBlockWidthExponent { get; set; }

    /// <summary>Code-block height exponent (ycb + 2, actual height = 2^(ycb+2)).</summary>
    public int CodeBlockHeightExponent { get; set; }

    /// <summary>Code-block style flags.</summary>
    public CodeBlockStyle CodeBlockFlags { get; set; }

    /// <summary>Wavelet transform type (0 = 9-7 irreversible, 1 = 5-3 reversible).</summary>
    public WaveletTransform WaveletType { get; set; }

    /// <summary>Precinct sizes for each resolution level.</summary>
    public (int Width, int Height)[] PrecinctSizes { get; set; } = [];

    /// <summary>Code-block width.</summary>
    public int CodeBlockWidth => 1 << CodeBlockWidthExponent;

    /// <summary>Code-block height.</summary>
    public int CodeBlockHeight => 1 << CodeBlockHeightExponent;
}

/// <summary>
/// Coding style flags (Scod byte).
/// </summary>
[Flags]
public enum CodingStyle : byte
{
    /// <summary>No coding style flags set.</summary>
    None = 0,
    /// <summary>Precincts defined in COD marker.</summary>
    PrecinctsDefined = 0x01,
    /// <summary>SOP markers may be present.</summary>
    SopMarkers = 0x02,
    /// <summary>EPH markers may be present.</summary>
    EphMarkers = 0x04,
}

/// <summary>
/// Progression order types.
/// </summary>
public enum ProgressionOrder : byte
{
    /// <summary>Layer-Resolution-Component-Position.</summary>
    LRCP = 0,
    /// <summary>Resolution-Layer-Component-Position.</summary>
    RLCP = 1,
    /// <summary>Resolution-Position-Component-Layer.</summary>
    RPCL = 2,
    /// <summary>Position-Component-Resolution-Layer.</summary>
    PCRL = 3,
    /// <summary>Component-Position-Resolution-Layer.</summary>
    CPRL = 4,
}

/// <summary>
/// Code-block style flags (Scb byte in COD/COC).
/// </summary>
[Flags]
public enum CodeBlockStyle : byte
{
    /// <summary>No code-block style flags set.</summary>
    None = 0,
    /// <summary>Selective arithmetic coding bypass.</summary>
    SelectiveBypass = 0x01,
    /// <summary>Reset context probabilities on coding pass boundaries.</summary>
    ResetContext = 0x02,
    /// <summary>Termination on each coding pass.</summary>
    TerminateOnPass = 0x04,
    /// <summary>Vertically causal context.</summary>
    VerticallyCausal = 0x08,
    /// <summary>Predictable termination.</summary>
    PredictableTermination = 0x10,
    /// <summary>Segmentation symbols.</summary>
    SegmentationSymbols = 0x20,
}

/// <summary>
/// Wavelet transform types.
/// </summary>
public enum WaveletTransform : byte
{
    /// <summary>9-7 irreversible filter (lossy).</summary>
    Irreversible_9_7 = 0,
    /// <summary>5-3 reversible filter (lossless).</summary>
    Reversible_5_3 = 1,
}

/// <summary>
/// Quantization parameters from QCD marker.
/// </summary>
public class QuantizationParameters
{
    /// <summary>Quantization style (Sqcd).</summary>
    public QuantizationStyle Style { get; set; }

    /// <summary>Number of guard bits.</summary>
    public int GuardBits { get; set; }

    /// <summary>Quantization step sizes for each subband.</summary>
    public QuantizationStepSize[] StepSizes { get; set; } = [];
}

/// <summary>
/// Quantization style.
/// </summary>
public enum QuantizationStyle : byte
{
    /// <summary>No quantization.</summary>
    None = 0,
    /// <summary>Scalar derived quantization.</summary>
    ScalarDerived = 1,
    /// <summary>Scalar expounded quantization.</summary>
    ScalarExpounded = 2,
}

/// <summary>
/// Quantization step size for a subband.
/// </summary>
public readonly struct QuantizationStepSize
{
    /// <summary>
    /// Gets the exponent component of the quantization step size.
    /// </summary>
    public readonly int Exponent;

    /// <summary>
    /// Gets the mantissa component of the quantization step size.
    /// </summary>
    public readonly int Mantissa;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuantizationStepSize"/> struct.
    /// </summary>
    /// <param name="exponent">The exponent component.</param>
    /// <param name="mantissa">The mantissa component.</param>
    public QuantizationStepSize(int exponent, int mantissa)
    {
        Exponent = exponent;
        Mantissa = mantissa;
    }
        
    /// <summary>
    /// Gets the actual step size delta = 2^(-Exponent) * (1 + Mantissa/2048).
    /// For irreversible coding: step = 2^(Rb - epsilon) where epsilon is stored in Exponent.
    /// Using negative exponent as approximation when Rb is unknown.
    /// </summary>
    public double StepSize => Math.Pow(2, -Exponent) * (1.0 + Mantissa / 2048.0);
}
