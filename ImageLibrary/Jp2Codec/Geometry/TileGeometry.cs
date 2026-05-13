using System;
using Jp2Codec.Codestream.Segments;
using Jp2Codec.Tier1;

namespace Jp2Codec.Geometry
{
    /// <summary>
    /// Half-open rectangle in canvas coordinates: <c>[X0, X1)</c> by
    /// <c>[Y0, Y1)</c>. The spec uses x0..x1-1 notation (closed) but the
    /// arithmetic for widths/heights and intersections is cleaner in
    /// half-open form, and conversion is just <c>w = X1 - X0</c>.
    /// </summary>
    internal readonly struct CanvasRect : IEquatable<CanvasRect>
    {
        public int X0 { get; }
        public int Y0 { get; }
        public int X1 { get; }
        public int Y1 { get; }

        public int Width => X1 - X0;
        public int Height => Y1 - Y0;
        public bool IsEmpty => Width <= 0 || Height <= 0;

        public CanvasRect(int x0, int y0, int x1, int y1)
        {
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
        }

        public bool Equals(CanvasRect other) =>
            X0 == other.X0 && Y0 == other.Y0 && X1 == other.X1 && Y1 == other.Y1;

        public override bool Equals(object? obj) => obj is CanvasRect r && Equals(r);
        public override int GetHashCode() => HashCode.Combine(X0, Y0, X1, Y1);
        public override string ToString() => $"[{X0}..{X1}) x [{Y0}..{Y1})";

        public static bool operator ==(CanvasRect a, CanvasRect b) => a.Equals(b);
        public static bool operator !=(CanvasRect a, CanvasRect b) => !a.Equals(b);
    }

    /// <summary>
    /// Computes the canvas rectangles laid out by ISO/IEC 15444-1 Annex B
    /// for one tile: tile-on-reference-grid, each tile-component on its
    /// component grid, each resolution on its component grid, and each
    /// subband on its own canvas. All coordinates honour the ceiling math
    /// from B-12 / B-14 / B-15 (which carries negative numerators through
    /// the HL/LH/HH 2^(n_b-1) shift).
    /// </summary>
    internal static class TileGeometry
    {
        /// <summary>
        /// Tile rectangle on the reference grid for tile index
        /// (<paramref name="u"/>, <paramref name="v"/>) per B-11:
        /// tx0 = max(XTOsiz + u·XTsiz, XOsiz), ...
        /// </summary>
        public static CanvasRect TileRectOnReferenceGrid(SizSegment siz, int u, int v)
        {
            if (siz is null) throw new ArgumentNullException(nameof(siz));

            long xtosiz = siz.TileHorizontalOffset;
            long ytosiz = siz.TileVerticalOffset;
            long xtsiz = siz.TileWidth;
            long ytsiz = siz.TileHeight;
            long xosiz = siz.ImageHorizontalOffset;
            long yosiz = siz.ImageVerticalOffset;
            long xsiz = siz.ReferenceGridWidth;
            long ysiz = siz.ReferenceGridHeight;

            long tx0 = Math.Max(xtosiz + (long)u * xtsiz, xosiz);
            long ty0 = Math.Max(ytosiz + (long)v * ytsiz, yosiz);
            long tx1 = Math.Min(xtosiz + ((long)u + 1) * xtsiz, xsiz);
            long ty1 = Math.Min(ytosiz + ((long)v + 1) * ytsiz, ysiz);

            return new CanvasRect(
                checked((int)tx0), checked((int)ty0),
                checked((int)tx1), checked((int)ty1));
        }

        /// <summary>
        /// Tile-component rectangle on the component grid per B-12:
        /// tcx0 = ceil(tx0 / XRsiz_c), tcy0 = ceil(ty0 / YRsiz_c), and so on.
        /// </summary>
        public static CanvasRect TileComponentRect(SizSegment siz, CanvasRect tileOnReferenceGrid, int componentIndex)
        {
            if (siz is null) throw new ArgumentNullException(nameof(siz));
            SizComponent comp = siz.Components[componentIndex];
            int xr = comp.HorizontalSubsampling;
            int yr = comp.VerticalSubsampling;
            return new CanvasRect(
                CoordMath.CeilDiv(tileOnReferenceGrid.X0, xr),
                CoordMath.CeilDiv(tileOnReferenceGrid.Y0, yr),
                CoordMath.CeilDiv(tileOnReferenceGrid.X1, xr),
                CoordMath.CeilDiv(tileOnReferenceGrid.Y1, yr));
        }

        /// <summary>
        /// Resolution-r rectangle on the component grid per B-14:
        /// trx0(r) = ceil(tcx0 / 2^(N_L - r)), and so on.
        /// </summary>
        /// <param name="numDecompositionLevels">N_L for this tile-component (from COD or COC).</param>
        /// <param name="resolution">r in 0..N_L. r=0 is the deepest LL approximation; r=N_L is full resolution.</param>
        public static CanvasRect ResolutionRect(CanvasRect tileComponent, int numDecompositionLevels, int resolution)
        {
            if (numDecompositionLevels < 0)
                throw new ArgumentOutOfRangeException(nameof(numDecompositionLevels), numDecompositionLevels, null);
            if (resolution < 0 || resolution > numDecompositionLevels)
                throw new ArgumentOutOfRangeException(nameof(resolution), resolution, null);

            int e = numDecompositionLevels - resolution;
            return new CanvasRect(
                CoordMath.CeilDivPow2(tileComponent.X0, e),
                CoordMath.CeilDivPow2(tileComponent.Y0, e),
                CoordMath.CeilDivPow2(tileComponent.X1, e),
                CoordMath.CeilDivPow2(tileComponent.Y1, e));
        }

        /// <summary>
        /// Subband-canvas rectangle for orientation <paramref name="orientation"/>
        /// at decomposition level <paramref name="decompositionLevel"/> per B-15.
        /// For LL at decomposition level <c>N_L</c> (i.e. resolution r=0) this
        /// agrees with <see cref="ResolutionRect"/>; for the detail subbands
        /// at any level it applies the 2^(n_b-1) shift before the ceiling.
        /// </summary>
        public static CanvasRect SubbandRect(
            CanvasRect tileComponent,
            int decompositionLevel,
            SubbandOrientation orientation)
        {
            if (decompositionLevel < 0)
                throw new ArgumentOutOfRangeException(nameof(decompositionLevel), decompositionLevel, null);

            int nb = decompositionLevel;
            int shift = nb > 0 ? 1 << (nb - 1) : 0;

            int hxOffset, hyOffset;
            switch (orientation)
            {
                case SubbandOrientation.LL: hxOffset = 0;     hyOffset = 0;     break;
                case SubbandOrientation.HL: hxOffset = shift; hyOffset = 0;     break;
                case SubbandOrientation.LH: hxOffset = 0;     hyOffset = shift; break;
                case SubbandOrientation.HH: hxOffset = shift; hyOffset = shift; break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(orientation), orientation, null);
            }

            return new CanvasRect(
                CoordMath.CeilDivPow2(tileComponent.X0 - hxOffset, nb),
                CoordMath.CeilDivPow2(tileComponent.Y0 - hyOffset, nb),
                CoordMath.CeilDivPow2(tileComponent.X1 - hxOffset, nb),
                CoordMath.CeilDivPow2(tileComponent.Y1 - hyOffset, nb));
        }
    }
}
