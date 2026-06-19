using System;
using System.Collections.Generic;
using Jp2Codec.Codestream.Segments;
using Jp2Codec.Geometry;

namespace Jp2Codec.Tiles
{
    /// <summary>
    /// One packet's coordinates within a tile: (layer, resolution,
    /// component, precinct index). Position-based progression orders
    /// (RPCL/PCRL/CPRL) ultimately map to the same flat 4-tuple — the
    /// precinct index runs in raster order over the precinct grid at the
    /// (component, resolution) selected.
    /// </summary>
    internal readonly struct PacketCoordinates
    {
        public int Layer { get; }
        public int Resolution { get; }
        public int Component { get; }
        public int Precinct { get; }

        public PacketCoordinates(int layer, int resolution, int component, int precinct)
        {
            Layer = layer;
            Resolution = resolution;
            Component = component;
            Precinct = precinct;
        }
    }

    /// <summary>
    /// Generates the in-codestream packet ordering for a tile per
    /// ISO/IEC 15444-1 B.12. All five Part-1 orders are wired here: LRCP,
    /// RLCP, RPCL, PCRL, CPRL.
    ///
    /// For LRCP the packet ordering is:
    /// <code>
    ///     for layer = 0 .. numLayers - 1:
    ///         for resolution = 0 .. maxResolutions:
    ///             for component = 0 .. numComponents - 1:
    ///                 for precinct = 0 .. numPrecincts(component, resolution) - 1:
    ///                     emit (layer, resolution, component, precinct)
    /// </code>
    /// "maxResolutions" is the max of (component decomposition levels + 1)
    /// across all components — components with fewer resolutions are
    /// skipped at higher r values, not padded with empty packets.
    ///
    /// For PCRL (B.12.1.2) the packets are emitted at each reference-grid
    /// position where a precinct corner falls, in (y, x, component,
    /// resolution, layer) order. Two precinct grids of different (c, r)
    /// may co-locate corners at the same (y, x) — they tie-break on
    /// (component, resolution) per the spec.
    /// </summary>
    internal static class PacketIterator
    {
        public static IEnumerable<PacketCoordinates> Enumerate(
            ProgressionOrder order,
            int numLayers,
            IReadOnlyList<TileComponentState> components,
            CanvasRect tileRectOnReferenceGrid)
        {
            if (components is null) throw new ArgumentNullException(nameof(components));
            if (numLayers < 1) throw new ArgumentOutOfRangeException(nameof(numLayers));

            var maxResolutions = 0;
            for (var c = 0; c < components.Count; c++)
            {
                int n = components[c].Resolutions.Length;
                if (n > maxResolutions) maxResolutions = n;
            }

            switch (order)
            {
                case ProgressionOrder.Lrcp:
                    return EnumerateLrcp(numLayers, components, maxResolutions);
                case ProgressionOrder.Rlcp:
                    return EnumerateRlcp(numLayers, components, maxResolutions);
                case ProgressionOrder.Pcrl:
                    return EnumeratePositionBased(numLayers, components, tileRectOnReferenceGrid, ComparePcrl);
                case ProgressionOrder.Rpcl:
                    return EnumeratePositionBased(numLayers, components, tileRectOnReferenceGrid, CompareRpcl);
                case ProgressionOrder.Cprl:
                    return EnumeratePositionBased(numLayers, components, tileRectOnReferenceGrid, CompareCprl);
                default:
                    throw new NotSupportedException(
                        $"Progression order {order} is not yet implemented in Jp2Codec.");
            }
        }

        private static IEnumerable<PacketCoordinates> EnumerateLrcp(
            int numLayers,
            IReadOnlyList<TileComponentState> components,
            int maxResolutions)
        {
            for (var l = 0; l < numLayers; l++)
            {
                for (var r = 0; r < maxResolutions; r++)
                {
                    for (var c = 0; c < components.Count; c++)
                    {
                        TileComponentState comp = components[c];
                        if (r >= comp.Resolutions.Length) continue;
                        int totalPrecincts = comp.Resolutions[r].Precincts.TotalPrecincts;
                        for (var p = 0; p < totalPrecincts; p++)
                        {
                            yield return new PacketCoordinates(l, r, c, p);
                        }
                    }
                }
            }
        }

        private static IEnumerable<PacketCoordinates> EnumerateRlcp(
            int numLayers,
            IReadOnlyList<TileComponentState> components,
            int maxResolutions)
        {
            for (var r = 0; r < maxResolutions; r++)
            {
                for (var l = 0; l < numLayers; l++)
                {
                    for (var c = 0; c < components.Count; c++)
                    {
                        TileComponentState comp = components[c];
                        if (r >= comp.Resolutions.Length) continue;
                        int totalPrecincts = comp.Resolutions[r].Precincts.TotalPrecincts;
                        for (var p = 0; p < totalPrecincts; p++)
                        {
                            yield return new PacketCoordinates(l, r, c, p);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Shared body for the three position-based orders (PCRL, RPCL, CPRL,
        /// per B.12.1.2 / .3 / .4). Each walks every precinct corner on the
        /// reference grid; the supplied <paramref name="compare"/> picks the
        /// nesting of (position, component, resolution). Layer is always the
        /// innermost loop and is expanded after sorting.
        ///
        /// A precinct (px, py) of tile-component-resolution (c, r) has its
        /// top-left anchor at resolution-canvas coord
        /// <c>((firstColOnRes + px) &lt;&lt; PPx, (firstRowOnRes + py) &lt;&lt; PPy)</c>,
        /// which maps to reference-grid coord
        /// <c>(anchor_res_x &lt;&lt; (NL-r)) * XRsiz_c</c>. The very first
        /// precinct (px=0 or py=0) may have its unclipped anchor before the
        /// tile origin when the resolution canvas starts mid-precinct; the
        /// spec's "(x == tx0)" branch makes the tile origin itself a precinct
        /// corner in that case, which we model by clamping the anchor to it.
        /// </summary>
        private static IEnumerable<PacketCoordinates> EnumeratePositionBased(
            int numLayers,
            IReadOnlyList<TileComponentState> components,
            CanvasRect tileRect,
            Comparison<(long Y, long X, int C, int R, int P)> compare)
        {
            var entries = new List<(long Y, long X, int C, int R, int P)>();
            for (var c = 0; c < components.Count; c++)
            {
                TileComponentState comp = components[c];
                int xrsiz = comp.SizComponent.HorizontalSubsampling;
                int yrsiz = comp.SizComponent.VerticalSubsampling;
                int nl = comp.NumDecompositionLevels;
                int numResolutions = comp.Resolutions.Length;
                for (var r = 0; r < numResolutions; r++)
                {
                    ResolutionState res = comp.Resolutions[r];
                    PrecinctGrid grid = res.Precincts;
                    int totalPrecincts = grid.TotalPrecincts;
                    if (totalPrecincts == 0) continue;

                    int decompLevel = nl - r;
                    int ppx = grid.PrecinctWidthExponent;
                    int ppy = grid.PrecinctHeightExponent;

                    // Precinct anchor on the resolution canvas. Floor here so
                    // that the (0, 0) precinct's anchor matches what the rest
                    // of the geometry helpers compute (PrecinctRectOnResolution).
                    int firstColOnRes = CoordMath.FloorDivPow2(res.Canvas.X0, ppx) << ppx;
                    int firstRowOnRes = CoordMath.FloorDivPow2(res.Canvas.Y0, ppy) << ppy;

                    // Reference-grid step between successive precincts at this
                    // (c, r): XRsiz * 2^(PPx + NL-r).
                    long refStepX = (long)xrsiz << (ppx + decompLevel);
                    long refStepY = (long)yrsiz << (ppy + decompLevel);

                    // Unclipped reference-grid coord of the (0, 0) precinct anchor.
                    long firstColRef = ((long)firstColOnRes << decompLevel) * xrsiz;
                    long firstRowRef = ((long)firstRowOnRes << decompLevel) * yrsiz;

                    for (var p = 0; p < totalPrecincts; p++)
                    {
                        int px = p % grid.NumPrecinctsWide;
                        int py = p / grid.NumPrecinctsWide;
                        long refX = firstColRef + (long)px * refStepX;
                        long refY = firstRowRef + (long)py * refStepY;
                        // Clamp the very-first-precinct case to the tile origin.
                        if (refX < tileRect.X0) refX = tileRect.X0;
                        if (refY < tileRect.Y0) refY = tileRect.Y0;
                        entries.Add((refY, refX, c, r, p));
                    }
                }
            }

            entries.Sort(compare);

            foreach ((_, _, int c, int r, int p) in entries)
                for (var l = 0; l < numLayers; l++)
                    yield return new PacketCoordinates(l, r, c, p);
        }

        /// <summary>PCRL (B.12.1.2): sort key (y, x, c, r).</summary>
        private static int ComparePcrl(
            (long Y, long X, int C, int R, int P) a,
            (long Y, long X, int C, int R, int P) b)
        {
            int cmp = a.Y.CompareTo(b.Y);
            if (cmp != 0) return cmp;
            cmp = a.X.CompareTo(b.X);
            if (cmp != 0) return cmp;
            cmp = a.C.CompareTo(b.C);
            if (cmp != 0) return cmp;
            return a.R.CompareTo(b.R);
        }

        /// <summary>RPCL (B.12.1.3): sort key (r, y, x, c).</summary>
        private static int CompareRpcl(
            (long Y, long X, int C, int R, int P) a,
            (long Y, long X, int C, int R, int P) b)
        {
            int cmp = a.R.CompareTo(b.R);
            if (cmp != 0) return cmp;
            cmp = a.Y.CompareTo(b.Y);
            if (cmp != 0) return cmp;
            cmp = a.X.CompareTo(b.X);
            if (cmp != 0) return cmp;
            return a.C.CompareTo(b.C);
        }

        /// <summary>CPRL (B.12.1.4): sort key (c, y, x, r).</summary>
        private static int CompareCprl(
            (long Y, long X, int C, int R, int P) a,
            (long Y, long X, int C, int R, int P) b)
        {
            int cmp = a.C.CompareTo(b.C);
            if (cmp != 0) return cmp;
            cmp = a.Y.CompareTo(b.Y);
            if (cmp != 0) return cmp;
            cmp = a.X.CompareTo(b.X);
            if (cmp != 0) return cmp;
            return a.R.CompareTo(b.R);
        }
    }
}
