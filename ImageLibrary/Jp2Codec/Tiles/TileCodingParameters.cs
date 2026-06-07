using System.Collections.Generic;
using Jp2Codec.Codestream.Segments;

namespace Jp2Codec.Tiles
{
    /// <summary>
    /// Resolved coding parameters for one tile-component: the effective COD
    /// and QCD after applying COC/QCC overrides in the spec-mandated order:
    ///   tile-part COC/QCC &gt; main-header COC/QCC &gt; tile-part COD/QCD &gt;
    ///   main-header COD/QCD.
    ///
    /// COD-style fields that can never be component-overridden (progression
    /// order, layer count, MCT flag, SOP/EPH markers) are read from the
    /// tile-level COD only — COC carries no SGcod fields per A.6.2.
    /// </summary>
    internal sealed class TileComponentCodingParameters
    {
        public int ComponentIndex { get; }
        public int DecompositionLevels { get; }
        public int CodeBlockWidthExponent { get; }
        public int CodeBlockHeightExponent { get; }
        public CodeBlockStyle CodeBlockStyle { get; }
        public WaveletTransform WaveletTransform { get; }
        public int[] PrecinctWidthExponents { get; }
        public int[] PrecinctHeightExponents { get; }

        // Quantization (from QCD/QCC).
        public QuantizationStyle QuantizationStyle { get; }
        public int GuardBits { get; }
        public IReadOnlyList<int> QuantizationExponents { get; }
        public IReadOnlyList<int> QuantizationMantissas { get; }

        public TileComponentCodingParameters(
            int componentIndex,
            int decompositionLevels,
            int codeBlockWidthExponent,
            int codeBlockHeightExponent,
            CodeBlockStyle codeBlockStyle,
            WaveletTransform waveletTransform,
            int[] precinctWidthExponents,
            int[] precinctHeightExponents,
            QuantizationStyle quantizationStyle,
            int guardBits,
            IReadOnlyList<int> quantizationExponents,
            IReadOnlyList<int> quantizationMantissas)
        {
            ComponentIndex = componentIndex;
            DecompositionLevels = decompositionLevels;
            CodeBlockWidthExponent = codeBlockWidthExponent;
            CodeBlockHeightExponent = codeBlockHeightExponent;
            CodeBlockStyle = codeBlockStyle;
            WaveletTransform = waveletTransform;
            PrecinctWidthExponents = precinctWidthExponents;
            PrecinctHeightExponents = precinctHeightExponents;
            QuantizationStyle = quantizationStyle;
            GuardBits = guardBits;
            QuantizationExponents = quantizationExponents;
            QuantizationMantissas = quantizationMantissas;
        }

        /// <summary>True if the component uses the irreversible (9/7) wavelet kernel.</summary>
        public bool IsIrreversible => WaveletTransform == WaveletTransform.Irreversible9x7;
    }

    /// <summary>
    /// Builds <see cref="TileComponentCodingParameters"/> for one component
    /// by applying the COC/QCC override stack (tile-part beats main-header)
    /// on top of the tile-level COD/QCD.
    /// </summary>
    internal static class TileCodingParameterResolver
    {
        public static TileComponentCodingParameters Resolve(
            int componentIndex,
            CodSegment tileCod,
            QcdSegment tileQcd,
            IReadOnlyList<CocSegment> mainCocs,
            IReadOnlyList<QccSegment> mainQccs,
            IReadOnlyList<CocSegment> tileCocs,
            IReadOnlyList<QccSegment> tileQccs)
        {
            CocSegment? coc = FindCoc(tileCocs, componentIndex) ?? FindCoc(mainCocs, componentIndex);
            QccSegment? qcc = FindQcc(tileQccs, componentIndex) ?? FindQcc(mainQccs, componentIndex);

            int nl = coc?.DecompositionLevels ?? tileCod.DecompositionLevels;
            int xcb = coc?.CodeBlockWidthExponent ?? tileCod.CodeBlockWidthExponent;
            int ycb = coc?.CodeBlockHeightExponent ?? tileCod.CodeBlockHeightExponent;
            CodeBlockStyle style = coc?.CodeBlockStyle ?? tileCod.CodeBlockStyle;
            WaveletTransform transform = coc?.WaveletTransform ?? tileCod.WaveletTransform;
            int[] ppx = coc?.PrecinctWidthExponents ?? tileCod.PrecinctWidthExponents;
            int[] ppy = coc?.PrecinctHeightExponents ?? tileCod.PrecinctHeightExponents;

            QuantizationStyle qStyle = qcc?.Style ?? tileQcd.Style;
            int guard = qcc?.GuardBits ?? tileQcd.GuardBits;
            int[] exps = qcc?.Exponents ?? tileQcd.Exponents;
            int[] mants = qcc?.Mantissas ?? tileQcd.Mantissas;

            return new TileComponentCodingParameters(
                componentIndex, nl, xcb, ycb, style, transform,
                ppx, ppy, qStyle, guard, exps, mants);
        }

        private static CocSegment? FindCoc(IReadOnlyList<CocSegment> list, int componentIndex)
        {
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i].ComponentIndex == componentIndex) return list[i];
            return null;
        }

        private static QccSegment? FindQcc(IReadOnlyList<QccSegment> list, int componentIndex)
        {
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i].ComponentIndex == componentIndex) return list[i];
            return null;
        }
    }
}
