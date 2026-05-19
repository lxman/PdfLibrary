using System;

namespace Jp2Codec.Geometry
{
    /// <summary>
    /// Precinct partition for one (tile, component, resolution) per
    /// ISO/IEC 15444-1 B.6 / B.10. A precinct is a power-of-2 aligned chunk
    /// of the resolution-r canvas; the (i, j) precinct occupies
    /// <c>[i·2^PPx, (i+1)·2^PPx) × [j·2^PPy, (j+1)·2^PPy)</c> intersected
    /// with the resolution-r canvas <c>[trx0, trx1) × [try0, try1)</c>. The
    /// number of precincts spanning the canvas is given by B-16:
    /// <code>
    ///   numPrecinctsWide = ceil(trx1 / 2^PPx) - floor(trx0 / 2^PPx)
    ///   numPrecinctsHigh = ceil(try1 / 2^PPy) - floor(try0 / 2^PPy)
    /// </code>
    /// when the canvas is non-empty; zero otherwise. Empty precincts (their
    /// intersection with the canvas is empty) are still counted by Tier-2 —
    /// they show up in the codestream as one-bit (zero-length) packets.
    /// </summary>
    internal sealed class PrecinctGrid
    {
        /// <summary>Resolution index r in 0..N_L (0 = LL only, r &gt; 0 = HL+LH+HH triple).</summary>
        public int Resolution { get; }

        /// <summary>Precinct width exponent PPx for this resolution.</summary>
        public int PrecinctWidthExponent { get; }

        /// <summary>Precinct height exponent PPy for this resolution.</summary>
        public int PrecinctHeightExponent { get; }

        /// <summary>Number of precincts spanning the resolution canvas horizontally.</summary>
        public int NumPrecinctsWide { get; }

        /// <summary>Number of precincts spanning the resolution canvas vertically.</summary>
        public int NumPrecinctsHigh { get; }

        /// <summary>The resolution-r canvas this grid partitions.</summary>
        public CanvasRect ResolutionCanvas { get; }

        public int TotalPrecincts => NumPrecinctsWide * NumPrecinctsHigh;

        public PrecinctGrid(
            int resolution,
            int precinctWidthExponent,
            int precinctHeightExponent,
            int numPrecinctsWide,
            int numPrecinctsHigh,
            CanvasRect resolutionCanvas)
        {
            Resolution = resolution;
            PrecinctWidthExponent = precinctWidthExponent;
            PrecinctHeightExponent = precinctHeightExponent;
            NumPrecinctsWide = numPrecinctsWide;
            NumPrecinctsHigh = numPrecinctsHigh;
            ResolutionCanvas = resolutionCanvas;
        }

        /// <summary>
        /// Compute the precinct grid for the supplied resolution canvas. PPx
        /// and PPy are the precinct exponents for this resolution as signalled
        /// by COD / COC (default = 15 each, i.e. 32768).
        /// </summary>
        public static PrecinctGrid Build(int resolution, int ppx, int ppy, CanvasRect resolutionCanvas)
        {
            if (resolution < 0) throw new ArgumentOutOfRangeException(nameof(resolution), resolution, null);
            if (ppx < 0 || ppy < 0)
                throw new ArgumentOutOfRangeException(
                    "Precinct exponents must be non-negative");

            int numWide, numHigh;
            if (resolutionCanvas.IsEmpty)
            {
                numWide = 0;
                numHigh = 0;
            }
            else
            {
                numWide = CoordMath.CeilDivPow2(resolutionCanvas.X1, ppx)
                          - CoordMath.FloorDivPow2(resolutionCanvas.X0, ppx);
                numHigh = CoordMath.CeilDivPow2(resolutionCanvas.Y1, ppy)
                          - CoordMath.FloorDivPow2(resolutionCanvas.Y0, ppy);
                if (numWide < 0) numWide = 0;
                if (numHigh < 0) numHigh = 0;
            }

            return new PrecinctGrid(resolution, ppx, ppy, numWide, numHigh, resolutionCanvas);
        }

        /// <summary>
        /// Bounding rectangle for the (<paramref name="px"/>, <paramref name="py"/>)
        /// precinct on the RESOLUTION canvas (intersected with [trx0..trx1) × [try0..try1)).
        /// May return an empty rectangle for precincts that lie entirely outside the canvas
        /// (legal — they still appear in the codestream as one-bit empty packets).
        /// </summary>
        public CanvasRect PrecinctRectOnResolution(int px, int py)
        {
            if ((uint)px >= (uint)NumPrecinctsWide || (uint)py >= (uint)NumPrecinctsHigh)
                throw new ArgumentOutOfRangeException(nameof(px), $"Precinct index ({px}, {py}) outside grid ({NumPrecinctsWide}x{NumPrecinctsHigh}).");

            int firstColOnCanvas = CoordMath.FloorDivPow2(ResolutionCanvas.X0, PrecinctWidthExponent);
            int firstRowOnCanvas = CoordMath.FloorDivPow2(ResolutionCanvas.Y0, PrecinctHeightExponent);
            int rawX0 = (firstColOnCanvas + px) << PrecinctWidthExponent;
            int rawY0 = (firstRowOnCanvas + py) << PrecinctHeightExponent;
            int rawX1 = rawX0 + (1 << PrecinctWidthExponent);
            int rawY1 = rawY0 + (1 << PrecinctHeightExponent);

            int x0 = Math.Max(rawX0, ResolutionCanvas.X0);
            int y0 = Math.Max(rawY0, ResolutionCanvas.Y0);
            int x1 = Math.Min(rawX1, ResolutionCanvas.X1);
            int y1 = Math.Min(rawY1, ResolutionCanvas.Y1);
            return new CanvasRect(x0, y0, x1, y1);
        }

        /// <summary>
        /// Bounding rectangle (on a SUBBAND canvas) of the slice of precinct
        /// (<paramref name="px"/>, <paramref name="py"/>) that hits this subband.
        /// For resolution 0 the resolution canvas IS the LL subband canvas, so
        /// this returns the precinct rect directly. For resolution &gt; 0 the
        /// subband canvas is at half the resolution canvas scale, so the
        /// precinct rect's coordinates halve (floor for the low corner, ceil
        /// for the high corner). The result is intersected with the supplied
        /// subband canvas.
        /// </summary>
        /// <param name="px">Precinct column.</param>
        /// <param name="py">Precinct row.</param>
        /// <param name="subbandCanvas">Subband canvas rect (from <see cref="TileGeometry.SubbandRect"/>).</param>
        public CanvasRect PrecinctRectOnSubband(int px, int py, CanvasRect subbandCanvas)
        {
            CanvasRect resRect = PrecinctRectOnResolution(px, py);
            if (Resolution == 0)
            {
                int x0 = Math.Max(resRect.X0, subbandCanvas.X0);
                int y0 = Math.Max(resRect.Y0, subbandCanvas.Y0);
                int x1 = Math.Min(resRect.X1, subbandCanvas.X1);
                int y1 = Math.Min(resRect.Y1, subbandCanvas.Y1);
                return new CanvasRect(x0, y0, x1, y1);
            }
            else
            {
                // r > 0: resolution canvas at scale 2^(NL-r); subband at scale
                // 2^(NL-r+1) = 2 * resolution scale. So the precinct rect on
                // the subband is the rect halved (ceil on the upper bound).
                int sbx0 = Math.Max(CoordMath.FloorDivPow2(resRect.X0, 1), subbandCanvas.X0);
                int sby0 = Math.Max(CoordMath.FloorDivPow2(resRect.Y0, 1), subbandCanvas.Y0);
                int sbx1 = Math.Min(CoordMath.CeilDivPow2(resRect.X1, 1), subbandCanvas.X1);
                int sby1 = Math.Min(CoordMath.CeilDivPow2(resRect.Y1, 1), subbandCanvas.Y1);
                return new CanvasRect(sbx0, sby0, sbx1, sby1);
            }
        }
    }

    /// <summary>
    /// Code-block partition for one subband of one precinct. The partition
    /// rule per B.7: code-block size on the subband canvas is 2^xcb' by
    /// 2^ycb', where xcb' / ycb' are clamped against the subband-side
    /// precinct exponent (B.6 / Table B.7):
    /// <list type="bullet">
    ///   <item>Resolution 0 (LL only): xcb' = min(xcb, PPx), ycb' = min(ycb, PPy).</item>
    ///   <item>Resolution &gt; 0:    xcb' = min(xcb, PPx-1), ycb' = min(ycb, PPy-1).</item>
    /// </list>
    /// The code-block grid is anchored at <c>(0, 0)</c> of the subband
    /// canvas; the precinct's slice of code-blocks is the rectangle of
    /// blocks whose anchor cells fall inside <see cref="PrecinctRectOnSubband"/>.
    /// </summary>
    internal sealed class CodeBlockGrid
    {
        /// <summary>Effective code-block width exponent xcb' (clamped against PPx).</summary>
        public int CodeBlockWidthExponent { get; }

        /// <summary>Effective code-block height exponent ycb' (clamped against PPy).</summary>
        public int CodeBlockHeightExponent { get; }

        /// <summary>Number of code-block columns this precinct contributes to this subband.</summary>
        public int CodeBlockColumns { get; }

        /// <summary>Number of code-block rows this precinct contributes to this subband.</summary>
        public int CodeBlockRows { get; }

        /// <summary>X coordinate of the leftmost code-block column anchor on the subband canvas.</summary>
        public int FirstCodeBlockAnchorX { get; }

        /// <summary>Y coordinate of the topmost code-block row anchor on the subband canvas.</summary>
        public int FirstCodeBlockAnchorY { get; }

        /// <summary>The subband canvas this grid partitions.</summary>
        public CanvasRect SubbandCanvas { get; }

        /// <summary>Precinct slice on the subband canvas (clipped).</summary>
        public CanvasRect PrecinctSliceOnSubband { get; }

        public CodeBlockGrid(
            int codeBlockWidthExponent,
            int codeBlockHeightExponent,
            int codeBlockColumns,
            int codeBlockRows,
            int firstCodeBlockAnchorX,
            int firstCodeBlockAnchorY,
            CanvasRect subbandCanvas,
            CanvasRect precinctSliceOnSubband)
        {
            CodeBlockWidthExponent = codeBlockWidthExponent;
            CodeBlockHeightExponent = codeBlockHeightExponent;
            CodeBlockColumns = codeBlockColumns;
            CodeBlockRows = codeBlockRows;
            FirstCodeBlockAnchorX = firstCodeBlockAnchorX;
            FirstCodeBlockAnchorY = firstCodeBlockAnchorY;
            SubbandCanvas = subbandCanvas;
            PrecinctSliceOnSubband = precinctSliceOnSubband;
        }

        /// <summary>
        /// Bounding rectangle of code-block (<paramref name="x"/>, <paramref name="y"/>)
        /// on the subband canvas, clipped to the subband canvas. Local origin
        /// of the code-block (the top-left coefficient) is at this rect's
        /// (X0, Y0); the rect's Width/Height give the EBCOT pass dimensions.
        /// </summary>
        public CanvasRect CodeBlockRectOnSubband(int x, int y)
        {
            if ((uint)x >= (uint)CodeBlockColumns)
                throw new ArgumentOutOfRangeException(nameof(x), x, null);
            if ((uint)y >= (uint)CodeBlockRows)
                throw new ArgumentOutOfRangeException(nameof(y), y, null);

            int blockW = 1 << CodeBlockWidthExponent;
            int blockH = 1 << CodeBlockHeightExponent;
            int rawX0 = FirstCodeBlockAnchorX + x * blockW;
            int rawY0 = FirstCodeBlockAnchorY + y * blockH;
            int rawX1 = rawX0 + blockW;
            int rawY1 = rawY0 + blockH;

            int x0 = Math.Max(rawX0, PrecinctSliceOnSubband.X0);
            int y0 = Math.Max(rawY0, PrecinctSliceOnSubband.Y0);
            int x1 = Math.Min(rawX1, PrecinctSliceOnSubband.X1);
            int y1 = Math.Min(rawY1, PrecinctSliceOnSubband.Y1);
            return new CanvasRect(x0, y0, x1, y1);
        }

        /// <summary>
        /// Build a code-block grid for one (precinct, subband). The supplied
        /// <paramref name="ppxOnSubband"/> and <paramref name="ppyOnSubband"/>
        /// are the subband-side precinct exponents (PPx for resolution 0,
        /// PPx - 1 otherwise).
        /// </summary>
        public static CodeBlockGrid Build(
            int xcb, int ycb,
            int ppxOnSubband, int ppyOnSubband,
            CanvasRect subbandCanvas,
            CanvasRect precinctSliceOnSubband)
        {
            int xcbPrime = Math.Min(xcb, ppxOnSubband);
            int ycbPrime = Math.Min(ycb, ppyOnSubband);

            int columns, rows;
            int firstX, firstY;

            if (precinctSliceOnSubband.IsEmpty)
            {
                columns = 0;
                rows = 0;
                firstX = 0;
                firstY = 0;
            }
            else
            {
                int firstCol = CoordMath.FloorDivPow2(precinctSliceOnSubband.X0, xcbPrime);
                int firstRow = CoordMath.FloorDivPow2(precinctSliceOnSubband.Y0, ycbPrime);
                int lastCol = CoordMath.CeilDivPow2(precinctSliceOnSubband.X1, xcbPrime);
                int lastRow = CoordMath.CeilDivPow2(precinctSliceOnSubband.Y1, ycbPrime);

                columns = Math.Max(0, lastCol - firstCol);
                rows = Math.Max(0, lastRow - firstRow);
                firstX = firstCol << xcbPrime;
                firstY = firstRow << ycbPrime;
            }

            return new CodeBlockGrid(
                xcbPrime, ycbPrime, columns, rows,
                firstX, firstY,
                subbandCanvas, precinctSliceOnSubband);
        }
    }
}
