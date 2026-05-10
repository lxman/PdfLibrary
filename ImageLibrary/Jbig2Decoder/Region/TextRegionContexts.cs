using Jbig2Decoder.Arith;

namespace Jbig2Decoder.Region
{
    /// <summary>
    /// Pre-allocated arithmetic-context bundle for a text region decode.
    ///
    /// The standard text-region path allocates these internally; the symbol
    /// dictionary refagg path (T.88 §6.5.8.2 with REFAGGNINST > 1) needs to
    /// reuse the SD's own context set across many text-region invocations
    /// embedded inside a single SD decode, so it can pre-build a bundle and
    /// hand it to <see cref="TextRegionDecoder.DecodeArithmetic"/>.
    /// </summary>
    internal sealed class TextRegionContexts
    {
        public IntegerDecoder Iadt = null!;
        public IntegerDecoder Iafs = null!;
        public IntegerDecoder Iads = null!;
        public IntegerDecoder Iait = null!;

        // Refinement-only contexts; null when SbRefine is false.
        public IntegerDecoder? Iari;
        public IntegerDecoder? Iardw;
        public IntegerDecoder? Iardh;
        public IntegerDecoder? Iardx;
        public IntegerDecoder? Iardy;

        public IaidDecoder Iaid = null!;

        // Refinement gear; allocated lazily in the standard path, eagerly in SD-refagg.
        public RefinementRegionDecoder? RefDecoder;
        public byte[]? GrStats;
    }
}
