using System;
using Jp2Codec.Codestream.Segments;
using Jp2Codec.Geometry;
using Jp2Codec.Tier1;
using Jp2Codec.Tier2;

namespace Jp2Codec.Tiles
{
    /// <summary>
    /// Per-(tile, component, resolution, subband) accumulator: holds the
    /// effective subband canvas, the precinct grid for the parent resolution
    /// (one is shared across the three subbands at a resolution &gt; 0), the
    /// per-precinct code-block grid, and the cumulative per-code-block state
    /// that the Tier-2 walk fills in.
    /// </summary>
    internal sealed class SubbandState
    {
        public SubbandOrientation Orientation { get; }
        public CanvasRect Canvas { get; }
        public CodeBlockGrid[] CodeBlockGridsByPrecinct { get; }

        /// <summary>
        /// Segments accumulated per code-block (one list per (precinct, x, y)).
        /// Default coding style emits one segment per contribution; TERMALL
        /// one per pass; LAZY alternates MQ and raw segments. The list is
        /// fed into Tier-1 in arrival order at decode time.
        /// </summary>
        public System.Collections.Generic.List<CodeBlockSegment>[][,] CodeBlockSegments { get; }

        /// <summary>
        /// ZeroBitPlanes per code-block (K in Annex E), captured from the
        /// inclusion tag tree on the block's first-inclusion packet. Indexed
        /// <c>[precinct][y, x]</c>; defaults to 0 until first inclusion.
        /// </summary>
        public int[][,] CodeBlockZeroBitPlanes { get; }

        /// <summary>
        /// Mb (encoder bit-plane count) per subband per Annex E:
        /// Mb = G + epsilon_b - 1.
        /// </summary>
        public int MagnitudeBits { get; }

        /// <summary>Annex E dequantization step size Δ_b (1 for reversible).</summary>
        public double StepSize { get; }

        public SubbandState(
            SubbandOrientation orientation,
            CanvasRect canvas,
            CodeBlockGrid[] codeBlockGridsByPrecinct,
            int magnitudeBits,
            double stepSize)
        {
            Orientation = orientation;
            Canvas = canvas;
            CodeBlockGridsByPrecinct = codeBlockGridsByPrecinct;
            MagnitudeBits = magnitudeBits;
            StepSize = stepSize;

            int n = codeBlockGridsByPrecinct.Length;
            CodeBlockSegments = new System.Collections.Generic.List<CodeBlockSegment>[n][,];
            CodeBlockZeroBitPlanes = new int[n][,];
            for (var p = 0; p < n; p++)
            {
                CodeBlockGrid g = codeBlockGridsByPrecinct[p];
                var segments2D = new System.Collections.Generic.List<CodeBlockSegment>[g.CodeBlockRows, g.CodeBlockColumns];
                var zerobp2D = new int[g.CodeBlockRows, g.CodeBlockColumns];
                for (var y = 0; y < g.CodeBlockRows; y++)
                    for (var x = 0; x < g.CodeBlockColumns; x++)
                        segments2D[y, x] = new System.Collections.Generic.List<CodeBlockSegment>();
                CodeBlockSegments[p] = segments2D;
                CodeBlockZeroBitPlanes[p] = zerobp2D;
            }
        }
    }

    /// <summary>
    /// Per-resolution data within a tile-component: the precinct grid for
    /// the resolution canvas plus the subband states for this resolution
    /// (1 LL at resolution 0, or HL/LH/HH at resolutions &gt; 0). Also owns
    /// the Tier-2 <see cref="Precinct"/> objects that pass through
    /// <see cref="PacketHeaderParser"/>.
    /// </summary>
    internal sealed class ResolutionState
    {
        public int Resolution { get; }
        public int DecompositionLevel { get; }
        public CanvasRect Canvas { get; }
        public PrecinctGrid Precincts { get; }
        public SubbandState[] Subbands { get; }
        /// <summary>Tier-2 precinct objects, indexed by linear precinct number.</summary>
        public Precinct[] Tier2Precincts { get; }

        public ResolutionState(
            int resolution,
            int decompositionLevel,
            CanvasRect canvas,
            PrecinctGrid precincts,
            SubbandState[] subbands,
            Precinct[] tier2Precincts)
        {
            Resolution = resolution;
            DecompositionLevel = decompositionLevel;
            Canvas = canvas;
            Precincts = precincts;
            Subbands = subbands;
            Tier2Precincts = tier2Precincts;
        }
    }

    /// <summary>
    /// Per-(tile, component) decode state: canvases at every resolution and
    /// subband, plus the precinct/code-block scaffolding. Construction is
    /// expensive in the worst case but cheap for the conformance corpus,
    /// which is dominated by NL &lt;= 5, one-precinct-per-resolution images.
    /// </summary>
    internal sealed class TileComponentState
    {
        public int ComponentIndex { get; }
        public TileComponentCodingParameters Parameters { get; }
        public CanvasRect TileComponentCanvas { get; }
        public ResolutionState[] Resolutions { get; }
        public SizComponent SizComponent { get; }

        public int NumDecompositionLevels => Parameters.DecompositionLevels;

        public TileComponentState(
            int componentIndex,
            TileComponentCodingParameters parameters,
            CanvasRect tileComponentCanvas,
            ResolutionState[] resolutions,
            SizComponent sizComponent)
        {
            ComponentIndex = componentIndex;
            Parameters = parameters;
            TileComponentCanvas = tileComponentCanvas;
            Resolutions = resolutions;
            SizComponent = sizComponent;
        }

        /// <summary>
        /// Build the full per-resolution / per-subband scaffold for one
        /// tile-component.
        /// </summary>
        public static TileComponentState Build(
            SizSegment siz,
            int componentIndex,
            CanvasRect tileRectOnReferenceGrid,
            TileComponentCodingParameters parameters)
        {
            CanvasRect tcRect = TileGeometry.TileComponentRect(siz, tileRectOnReferenceGrid, componentIndex);
            int nl = parameters.DecompositionLevels;
            int numResolutions = nl + 1;

            // Build the per-subband quantization to obtain (M_b, Δ_b) per
            // subband. Index in QCD order; see SubbandLayout.EnumerateQcdOrder.
            int compBitDepth = siz.Components[componentIndex].BitDepth;
            Quantization.SubbandQuantization[] quant =
                Quantization.QuantizationTable.Build(
                    nl,
                    parameters.GuardBits,
                    parameters.QuantizationStyle,
                    parameters.QuantizationExponents,
                    parameters.QuantizationMantissas,
                    compBitDepth,
                    isReversible: !parameters.IsIrreversible);

            // Map "QCD index" → "(resolution, orientation)".
            int QcdIndex(int resolution, SubbandOrientation orientation)
            {
                if (resolution == 0)
                    return 0; // NLLL is always the first
                int decompLevel = nl - resolution + 1;
                int blockOfLevel = nl - decompLevel; // 0 = level NL, increases for lower levels
                int baseIndex = 1 + blockOfLevel * 3;
                return orientation switch
                {
                    SubbandOrientation.HL => baseIndex + 0,
                    SubbandOrientation.LH => baseIndex + 1,
                    SubbandOrientation.HH => baseIndex + 2,
                    _ => throw new InvalidOperationException("LL only appears at resolution 0"),
                };
            }

            var resolutions = new ResolutionState[numResolutions];
            for (var r = 0; r < numResolutions; r++)
            {
                int decompositionLevel = r == 0 ? nl : (nl - r + 1);
                CanvasRect resCanvas = TileGeometry.ResolutionRect(tcRect, nl, r);
                int ppx = parameters.PrecinctWidthExponents[r];
                int ppy = parameters.PrecinctHeightExponents[r];
                PrecinctGrid grid = PrecinctGrid.Build(r, ppx, ppy, resCanvas);
                int totalPrecincts = grid.TotalPrecincts;

                // r=0 holds LL only; r>0 holds HL/LH/HH (in that fixed order, the
                // same Tier-2 PrecinctSubband[] order).
                SubbandOrientation[] orientations = r == 0
                    ? new[] { SubbandOrientation.LL }
                    : new[] { SubbandOrientation.HL, SubbandOrientation.LH, SubbandOrientation.HH };

                var subbandStates = new SubbandState[orientations.Length];
                // Build PrecinctSubband entries per-(precinct, subband) so the Tier-2
                // parser can drive its tag trees off them.
                var tier2Precincts = new Precinct[totalPrecincts];
                for (var p = 0; p < totalPrecincts; p++)
                {
                    var precinctSubbands = new PrecinctSubband[orientations.Length];
                    for (var s = 0; s < orientations.Length; s++)
                    {
                        // Compute precinct slice on subband canvas, then build code-block grid.
                        CanvasRect subCanvas = TileGeometry.SubbandRect(tcRect, decompositionLevel, orientations[s]);
                        int px = p % grid.NumPrecinctsWide;
                        int py = p / grid.NumPrecinctsWide;
                        CanvasRect slice = grid.PrecinctRectOnSubband(px, py, subCanvas);
                        int ppxOnSub = r == 0 ? ppx : (ppx - 1);
                        int ppyOnSub = r == 0 ? ppy : (ppy - 1);
                        CodeBlockGrid cbGrid = CodeBlockGrid.Build(
                            parameters.CodeBlockWidthExponent, parameters.CodeBlockHeightExponent,
                            ppxOnSub, ppyOnSub, subCanvas, slice);
                        precinctSubbands[s] = new PrecinctSubband(
                            cbGrid.CodeBlockColumns, cbGrid.CodeBlockRows);

                        // Lazily defer subbandState construction outside the precinct loop;
                        // we collect cbGrids first, then fold into SubbandState below.
                        // For now, store grids in subbandStates via a side-table.
                    }
                    tier2Precincts[p] = new Precinct(precinctSubbands);
                }

                // Build SubbandState entries — for each subband, collect its
                // per-precinct CodeBlockGrid and (Mb, stepSize) from the
                // quantization table.
                for (var s = 0; s < orientations.Length; s++)
                {
                    CanvasRect subCanvas = TileGeometry.SubbandRect(tcRect, decompositionLevel, orientations[s]);
                    var grids = new CodeBlockGrid[totalPrecincts];
                    for (var p = 0; p < totalPrecincts; p++)
                    {
                        int px = p % grid.NumPrecinctsWide;
                        int py = p / grid.NumPrecinctsWide;
                        CanvasRect slice = grid.PrecinctRectOnSubband(px, py, subCanvas);
                        int ppxOnSub = r == 0 ? ppx : (ppx - 1);
                        int ppyOnSub = r == 0 ? ppy : (ppy - 1);
                        grids[p] = CodeBlockGrid.Build(
                            parameters.CodeBlockWidthExponent, parameters.CodeBlockHeightExponent,
                            ppxOnSub, ppyOnSub, subCanvas, slice);
                    }

                    int qcdIdx = QcdIndex(r, orientations[s]);
                    int mb = quant[qcdIdx].MagnitudeBits;
                    double delta = quant[qcdIdx].StepSize;
                    subbandStates[s] = new SubbandState(orientations[s], subCanvas, grids, mb, delta);
                }

                resolutions[r] = new ResolutionState(
                    r, decompositionLevel, resCanvas, grid, subbandStates, tier2Precincts);
            }

            return new TileComponentState(
                componentIndex, parameters, tcRect, resolutions, siz.Components[componentIndex]);
        }
    }
}
