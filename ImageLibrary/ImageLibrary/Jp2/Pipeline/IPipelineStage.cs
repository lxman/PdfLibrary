namespace ImageLibrary.Jp2.Pipeline;

/// <summary>
/// Base interface for JPEG2000 decoder pipeline stages.
/// Each stage takes input from the previous stage and produces output for the next.
/// The intermediate outputs can be captured for testing and comparison.
/// </summary>
/// <typeparam name="TInput">Input type from previous stage.</typeparam>
/// <typeparam name="TOutput">Output type for next stage.</typeparam>
internal interface IPipelineStage<TInput, TOutput>
{
    /// <summary>
    /// Process the input and produce output for the next stage.
    /// </summary>
    /// <param name="input">Input from previous stage.</param>
    /// <returns>Output for next stage.</returns>
    TOutput Process(TInput input);
}

/// <summary>
/// Stage 1: Codestream parsing.
/// Input: Raw JP2 file bytes or codestream bytes.
/// Output: Parsed codestream with tile-part bitstreams.
/// </summary>
internal interface ICodestreamParser : IPipelineStage<byte[], Jp2Codestream>
{
}

/// <summary>
/// Stage 2: Tier-2 decoding (packet parsing).
/// Input: Tile-part bitstream.
/// Output: Code-block bitstreams organized by subband.
/// </summary>
internal interface ITier2Decoder : IPipelineStage<Jp2TilePart, Tier2Output>
{
}

/// <summary>
/// Stage 3: Tier-1 decoding (EBCOT arithmetic decoding).
/// Input: Code-block bitstream.
/// Output: Quantized DWT coefficients for the code-block.
/// </summary>
internal interface ITier1Decoder : IPipelineStage<CodeBlockBitstream, int[,]>
{
}

/// <summary>
/// Stage 4: Dequantization.
/// Input: Quantized coefficients for a subband.
/// Output: Floating-point DWT coefficients.
/// </summary>
internal interface IDequantizer : IPipelineStage<QuantizedSubband, double[,]>
{
}

/// <summary>
/// Stage 5: Inverse DWT.
/// Input: DWT coefficients for all subbands of a tile-component.
/// Output: Reconstructed pixel values.
/// </summary>
internal interface IInverseDwt : IPipelineStage<DwtCoefficients, double[,]>
{
}

/// <summary>
/// Stage 6: Post-processing (color transform, level shift, clamping).
/// Input: Reconstructed components.
/// Output: Final RGB/grayscale image data.
/// </summary>
internal interface IPostProcessor : IPipelineStage<ReconstructedTile, byte[]>
{
}