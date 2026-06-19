using System;
using System.Collections.Generic;
using System.IO;
using Jp2Codec.Codestream.Segments;
using Jp2Codec.Color;
using Jp2Codec.Geometry;
using Jp2Codec.Mq;
using Jp2Codec.Quantization;
using Jp2Codec.TileAssembly;
using Jp2Codec.Tier1;
using Jp2Codec.Tier2;
using Jp2Codec.Wavelet;

namespace Jp2Codec.Tiles
{
    /// <summary>
    /// Decodes one tile: walks the packet body stream, runs EBCOT Tier-1 per
    /// code-block, dequantizes per subband, runs the multi-level inverse
    /// DWT per component, applies the inverse multi-component transform
    /// (RCT / ICT) if the COD flag is set, then applies the inverse DC
    /// level shift. Result: int sample grids per component on the
    /// component grid.
    /// </summary>
    internal static class TileDecoder
    {
        /// <summary>
        /// Optional sink invoked once per (component, resolution, orientation)
        /// after Tier-1 finishes producing the dequantized integer (5/3) or
        /// float (9/7) grid. Tests use this to diff per-subband state across
        /// equivalent .j2c files (same image, different encoder options) when
        /// chasing decode-correctness anomalies; null in normal operation.
        /// </summary>
        internal static Action<int, int, SubbandOrientation, int[,]>? OnSubbandInts53;

        internal static Action<int, int, SubbandOrientation, float[,]>? OnSubbandFloats97;

        /// <summary>
        /// Optional per-block trace: called from <see cref="DecodeCodeBlock"/>
        /// with (component, resolution, orientation, blockX, blockY,
        /// passCount, segments, firstBitPlane, zeroBitPlanes). Test seam for
        /// tracking down LAZY-specific divergence — set the hook on a single
        /// block, decode two .j2c files, diff the captured segment streams.
        /// </summary>
        internal static Action<int, int, SubbandOrientation, int, int,
            int, IReadOnlyList<CodeBlockSegment>, int, int>? OnCodeBlockTrace;

        /// <summary>Active component/resolution/orientation/block selector for OnCodeBlockTrace; null = no filter.</summary>
        internal static (int Component, int Resolution, SubbandOrientation Orientation, int BlockX, int BlockY)? CodeBlockTraceFilter;

        /// <summary>
        /// Decode tile (<paramref name="tileU"/>, <paramref name="tileV"/>) and
        /// return the per-component sample grid for the tile. Each returned
        /// grid is sized at the tile-component canvas (width = tcx1 - tcx0,
        /// height = tcy1 - tcy0).
        /// </summary>
        public static TileDecodeResult DecodeTile(
            SizSegment siz,
            CodSegment defaultCod,
            QcdSegment defaultQcd,
            IReadOnlyList<CocSegment> mainCocs,
            IReadOnlyList<QccSegment> mainQccs,
            AssembledTile tile,
            int tileU, int tileV)
        {
            CodSegment tileCod = tile.CodOverride ?? defaultCod;
            QcdSegment tileQcd = tile.QcdOverride ?? defaultQcd;

            CanvasRect tileRect = TileGeometry.TileRectOnReferenceGrid(siz, tileU, tileV);

            // Build per-component states.
            var components = new TileComponentState[siz.NumberOfComponents];
            for (var c = 0; c < siz.NumberOfComponents; c++)
            {
                TileComponentCodingParameters parameters = TileCodingParameterResolver.Resolve(
                    c, tileCod, tileQcd,
                    mainCocs, mainQccs,
                    tile.CocOverrides, tile.QccOverrides);
                components[c] = TileComponentState.Build(siz, c, tileRect, parameters);
            }

            // Walk packets in progression order. When PPM/PPT supplies packed
            // packet headers separately from the body (A.7.4 / A.7.5), the
            // header bits come from the packed-header stream and the inline
            // body stream carries body bytes only. Without PPM/PPT both
            // logical streams point at the same buffer.
            int numLayers = tileCod.NumberOfLayers;
            var bodyStream = new PacketBodyStream(tile.PacketBody);
            PacketBodyStream headerStream = tile.UsesPackedHeaders
                ? new PacketBodyStream(tile.PackedPacketHeaders)
                : bodyStream;
            bool useSop = tileCod.UseSopMarkers;
            bool useEph = tileCod.UseEphMarkers;
            foreach (PacketCoordinates pc in PacketIterator.Enumerate(tileCod.ProgressionOrder, numLayers, components, tileRect))
            {
                // SOP precedes each packet in the body stream (A.8.1).
                if (useSop) ConsumeSopMarker(bodyStream);
                TileComponentState comp = components[pc.Component];
                ProcessPacket(pc, comp, headerStream, bodyStream, useEph,
                    comp.Parameters.CodeBlockStyle);
            }

            // Run Tier-1 → dequantize → IDWT per component.
            var tileComponentSamplesInt = new int[siz.NumberOfComponents][,];
            var tileComponentSamplesFloat = new float[siz.NumberOfComponents][,];
            var componentIsIrreversible = new bool[siz.NumberOfComponents];
            for (var c = 0; c < siz.NumberOfComponents; c++)
            {
                TileComponentState comp = components[c];
                componentIsIrreversible[c] = comp.Parameters.IsIrreversible;
                if (comp.Parameters.IsIrreversible)
                {
                    tileComponentSamplesFloat[c] = DecodeComponent97(comp);
                }
                else
                {
                    tileComponentSamplesInt[c] = DecodeComponent53(comp);
                }
            }

            // Inverse multi-component transform on the first three components, if requested.
            if (tileCod.UseMultipleComponentTransform && siz.NumberOfComponents >= 3)
            {
                if (componentIsIrreversible[0])
                {
                    InverseIct.Apply(
                        tileComponentSamplesFloat[0],
                        tileComponentSamplesFloat[1],
                        tileComponentSamplesFloat[2]);
                }
                else
                {
                    InverseRct.Apply(
                        tileComponentSamplesInt[0],
                        tileComponentSamplesInt[1],
                        tileComponentSamplesInt[2]);
                }
            }

            // Round float (irreversible) back to int and apply DC level shift per component.
            var perComponentTileSamples = new int[siz.NumberOfComponents][,];
            for (var c = 0; c < siz.NumberOfComponents; c++)
            {
                int precision = siz.Components[c].BitDepth;
                bool isSigned = siz.Components[c].IsSigned;
                if (componentIsIrreversible[c])
                {
                    InverseDcLevelShift.Apply(tileComponentSamplesFloat[c], precision, isSigned);
                    perComponentTileSamples[c] = RoundAndClip(tileComponentSamplesFloat[c], precision, isSigned);
                }
                else
                {
                    InverseDcLevelShift.Apply(tileComponentSamplesInt[c], precision, isSigned);
                    perComponentTileSamples[c] = ClipInt(tileComponentSamplesInt[c], precision, isSigned);
                }
            }

            return new TileDecodeResult(tileRect, perComponentTileSamples, components);
        }

        // ---- SOP / EPH marker handling (A.8) -------------------------------

        /// <summary>
        /// Consume an SOP marker (FF 91, length 6 total) when SOP is enabled on
        /// the COD. Silently no-ops if the next bytes aren't an SOP marker —
        /// encoders are not required to emit SOP for every packet even when
        /// the flag is set, and some streams use SOP only at tile boundaries.
        /// </summary>
        private static void ConsumeSopMarker(PacketBodyStream stream)
        {
            if (stream.Remaining < 6) return;
            if (stream.Buffer[stream.Position] != 0xFF) return;
            if (stream.Buffer[stream.Position + 1] != 0x91) return;
            int lsop = (stream.Buffer[stream.Position + 2] << 8) | stream.Buffer[stream.Position + 3];
            if (lsop != 4)
                throw new InvalidDataException(
                    $"SOP marker length {lsop} != 4 (Annex A.8.1).");
            // bytes [4..5] are Nsop (packet sequence number) — we don't
            // validate ordering; just skip.
            stream.Advance(6);
        }

        /// <summary>
        /// Consume an EPH marker (FF 92, length 2 total) when EPH is enabled
        /// on the COD. Silently no-ops if the next bytes aren't an EPH marker.
        /// </summary>
        private static void ConsumeEphMarker(PacketBodyStream stream)
        {
            if (stream.Remaining < 2) return;
            if (stream.Buffer[stream.Position] != 0xFF) return;
            if (stream.Buffer[stream.Position + 1] != 0x92) return;
            stream.Advance(2);
        }

        // ---- Per-packet processing -----------------------------------------

        private static void ProcessPacket(
            PacketCoordinates pc, TileComponentState comp,
            PacketBodyStream headerStream, PacketBodyStream bodyStream,
            bool useEph,
            CodeBlockStyle codeBlockStyle)
        {
            ResolutionState res = comp.Resolutions[pc.Resolution];
            Precinct precinct = res.Tier2Precincts[pc.Precinct];

            // Packet header consumes bytes from the header cursor up to its
            // byte-aligned boundary. The reader transparently honours the
            // 0xFF-stuff-bit rule per B.10.1. With PPM/PPT, headerStream is a
            // separate buffer (the main-header packed-header stream);
            // otherwise it aliases bodyStream and the cursor advances in
            // lockstep.
            int headerStart = headerStream.Position;
            var bitReader = new PacketHeaderBitReader(headerStream.Buffer, headerStart, headerStream.Remaining);
            PacketHeader header = PacketHeaderParser.Parse(precinct, pc.Layer, bitReader, codeBlockStyle);
            int headerByteLength = bitReader.BytesConsumedAfterAlign;
            headerStream.Advance(headerByteLength);

            // EPH (End-of-packet-header) marker: FF 92 — appears immediately
            // after the header in the same stream the header was read from.
            if (useEph) ConsumeEphMarker(headerStream);

            if (header.IsEmpty) return;

            // Each contribution's body bytes follow in subband / code-block order
            // (the same iteration order the PacketHeaderParser used). Each
            // contribution may carry MULTIPLE terminated segments (under TERMALL
            // and/or LAZY); read one byte chunk per segment.
            foreach (CodeBlockContribution contrib in header.Contributions)
            {
                SubbandState sub = res.Subbands[contrib.SubbandIndex];
                List<CodeBlockSegment> segments = sub.CodeBlockSegments[pc.Precinct][contrib.Y, contrib.X];
                foreach (ContributionSegment cs in contrib.Segments)
                {
                    int bodyLen = cs.ByteLength;
                    if (bodyLen > bodyStream.Remaining)
                        throw new InvalidDataException(
                            $"Packet body byte count {bodyLen} exceeds remaining tile bytes ({bodyStream.Remaining}).");
                    var segBytes = new byte[bodyLen];
                    Buffer.BlockCopy(bodyStream.Buffer, bodyStream.Position, segBytes, 0, bodyLen);
                    bodyStream.Advance(bodyLen);
                    segments.Add(new CodeBlockSegment(segBytes, cs.PassCount, cs.IsRaw));
                }
                if (contrib.IsFirstInclusion)
                {
                    sub.CodeBlockZeroBitPlanes[pc.Precinct][contrib.Y, contrib.X] = contrib.ZeroBitPlanesIfFirst;
                }
            }
        }

        // ---- Per-component decode (reversible) -----------------------------

        private static int[,] DecodeComponent53(TileComponentState comp)
        {
            int nl = comp.NumDecompositionLevels;
            // LL at deepest level → numerator of MultiLevelInverseDwt.Reverse53.
            ResolutionState resLL = comp.Resolutions[0];
            int[,] llDeepest = DecodeSubbandInts(comp, resLL, SubbandOrientation.LL);

            if (nl == 0) return llDeepest;

            var levels = new WaveletLevel53[nl];
            for (var r = 1; r <= nl; r++)
            {
                int n_b = nl - r + 1; // decomposition level
                ResolutionState rs = comp.Resolutions[r];
                int[,] hl = DecodeSubbandInts(comp, rs, SubbandOrientation.HL);
                int[,] lh = DecodeSubbandInts(comp, rs, SubbandOrientation.LH);
                int[,] hh = DecodeSubbandInts(comp, rs, SubbandOrientation.HH);

                // Parent canvas (LL at level n_b - 1) parities.
                int parentExponent = n_b - 1;
                int u0 = CoordMath.CeilDivPow2(comp.TileComponentCanvas.X0, parentExponent) & 1;
                int v0 = CoordMath.CeilDivPow2(comp.TileComponentCanvas.Y0, parentExponent) & 1;

                levels[n_b - 1] = new WaveletLevel53(hl, lh, hh, u0, v0);
            }
            return MultiLevelInverseDwt.Reverse53(llDeepest, levels);
        }

        // ---- Per-component decode (irreversible) ---------------------------

        private static float[,] DecodeComponent97(TileComponentState comp)
        {
            int nl = comp.NumDecompositionLevels;
            ResolutionState resLL = comp.Resolutions[0];
            float[,] llDeepest = DecodeSubbandFloats(comp, resLL, SubbandOrientation.LL);

            if (nl == 0) return llDeepest;

            var levels = new WaveletLevel97[nl];
            for (var r = 1; r <= nl; r++)
            {
                int n_b = nl - r + 1;
                ResolutionState rs = comp.Resolutions[r];
                float[,] hl = DecodeSubbandFloats(comp, rs, SubbandOrientation.HL);
                float[,] lh = DecodeSubbandFloats(comp, rs, SubbandOrientation.LH);
                float[,] hh = DecodeSubbandFloats(comp, rs, SubbandOrientation.HH);

                int parentExponent = n_b - 1;
                int u0 = CoordMath.CeilDivPow2(comp.TileComponentCanvas.X0, parentExponent) & 1;
                int v0 = CoordMath.CeilDivPow2(comp.TileComponentCanvas.Y0, parentExponent) & 1;

                levels[n_b - 1] = new WaveletLevel97(hl, lh, hh, u0, v0);
            }
            return MultiLevelInverseDwt.Reverse97(llDeepest, levels);
        }

        // ---- Per-subband decode (reversible) -------------------------------

        private static int[,] DecodeSubbandInts(TileComponentState comp, ResolutionState res, SubbandOrientation orientation)
        {
            int orientationIndex = ResolveSubbandIndex(res, orientation);
            SubbandState sub = res.Subbands[orientationIndex];
            int height = Math.Max(sub.Canvas.Height, 0);
            int width = Math.Max(sub.Canvas.Width, 0);
            var grid = new int[height, width];

            int totalPrecincts = res.Precincts.TotalPrecincts;
            for (var p = 0; p < totalPrecincts; p++)
            {
                CodeBlockGrid cbGrid = sub.CodeBlockGridsByPrecinct[p];
                int[,] zeroBp = sub.CodeBlockZeroBitPlanes[p];
                List<CodeBlockSegment>[,] segmentsByBlock = sub.CodeBlockSegments[p];
                for (var y = 0; y < cbGrid.CodeBlockRows; y++)
                {
                    for (var x = 0; x < cbGrid.CodeBlockColumns; x++)
                    {
                        List<CodeBlockSegment> segments = segmentsByBlock[y, x];
                        if (segments.Count == 0) continue;
                        int passCount = TotalPasses(segments);
                        if (passCount == 0) continue;
                        CanvasRect blockRect = cbGrid.CodeBlockRectOnSubband(x, y);
                        if (blockRect.Width <= 0 || blockRect.Height <= 0) continue;

                        int firstBitPlane = sub.MagnitudeBits - 1 - zeroBp[y, x];
                        if (firstBitPlane < 0) firstBitPlane = 0;
                        OnCodeBlockTrace?.Invoke(
                            comp.ComponentIndex, res.Resolution, orientation,
                            x, y, passCount, segments, firstBitPlane, zeroBp[y, x]);

                        int[,] q = DecodeCodeBlock(
                            sub, comp.Parameters,
                            blockRect.Width, blockRect.Height,
                            orientation, segments,
                            firstInclusionZeroBitPlanes: zeroBp[y, x]);

                        int missingBp = ComputeMissingBitPlanes(passCount, sub.MagnitudeBits, zeroBp[y, x]);
                        int[,] dq = SubbandDequantizer.DequantizeReversible(q, missingBp);

                        // Place block samples into the subband canvas, accounting
                        // for the block's offset relative to the subband's
                        // top-left.
                        int dstX = blockRect.X0 - sub.Canvas.X0;
                        int dstY = blockRect.Y0 - sub.Canvas.Y0;
                        for (var by = 0; by < blockRect.Height; by++)
                            for (var bx = 0; bx < blockRect.Width; bx++)
                                grid[dstY + by, dstX + bx] = dq[by, bx];
                    }
                }
            }
            OnSubbandInts53?.Invoke(comp.ComponentIndex, res.Resolution, orientation, grid);
            return grid;
        }

        private static int TotalPasses(List<CodeBlockSegment> segments)
        {
            var total = 0;
            for (var i = 0; i < segments.Count; i++) total += segments[i].PassCount;
            return total;
        }

        // ---- Per-subband decode (irreversible) -----------------------------

        private static float[,] DecodeSubbandFloats(TileComponentState comp, ResolutionState res, SubbandOrientation orientation)
        {
            int orientationIndex = ResolveSubbandIndex(res, orientation);
            SubbandState sub = res.Subbands[orientationIndex];
            int height = Math.Max(sub.Canvas.Height, 0);
            int width = Math.Max(sub.Canvas.Width, 0);
            var grid = new float[height, width];

            int totalPrecincts = res.Precincts.TotalPrecincts;
            for (var p = 0; p < totalPrecincts; p++)
            {
                CodeBlockGrid cbGrid = sub.CodeBlockGridsByPrecinct[p];
                int[,] zeroBp = sub.CodeBlockZeroBitPlanes[p];
                List<CodeBlockSegment>[,] segmentsByBlock = sub.CodeBlockSegments[p];
                for (var y = 0; y < cbGrid.CodeBlockRows; y++)
                {
                    for (var x = 0; x < cbGrid.CodeBlockColumns; x++)
                    {
                        List<CodeBlockSegment> segments = segmentsByBlock[y, x];
                        if (segments.Count == 0) continue;
                        int passCount = TotalPasses(segments);
                        if (passCount == 0) continue;
                        CanvasRect blockRect = cbGrid.CodeBlockRectOnSubband(x, y);
                        if (blockRect.Width <= 0 || blockRect.Height <= 0) continue;

                        int[,] q = DecodeCodeBlock(
                            sub, comp.Parameters,
                            blockRect.Width, blockRect.Height,
                            orientation, segments,
                            firstInclusionZeroBitPlanes: zeroBp[y, x]);

                        int missingBp = ComputeMissingBitPlanes(passCount, sub.MagnitudeBits, zeroBp[y, x]);
                        float[,] dq = SubbandDequantizer.DequantizeIrreversible(q, sub.StepSize, missingBp);

                        int dstX = blockRect.X0 - sub.Canvas.X0;
                        int dstY = blockRect.Y0 - sub.Canvas.Y0;
                        for (var by = 0; by < blockRect.Height; by++)
                            for (var bx = 0; bx < blockRect.Width; bx++)
                                grid[dstY + by, dstX + bx] = dq[by, bx];
                    }
                }
            }
            return grid;
        }

        /// <summary>
        /// Run Tier-1 for a single code-block. Walks the per-block segment
        /// list emitted by the Tier-2 packet-header parser, routing each
        /// segment to either the MQ or the raw-bit pass runner based on
        /// the segment's <see cref="CodeBlockSegment.IsRaw"/> flag. Each
        /// MQ segment gets a fresh <see cref="Jp2MqDecoder"/>; the Tier-1
        /// driver carries pass state (passes-completed, the flag grid,
        /// the context array) across calls.
        /// </summary>
        private static int[,] DecodeCodeBlock(
            SubbandState sub,
            TileComponentCodingParameters parameters,
            int width, int height,
            SubbandOrientation orientation,
            List<CodeBlockSegment> segments,
            int firstInclusionZeroBitPlanes)
        {
            CodeBlockStyle style = parameters.CodeBlockStyle;
            bool segSym = (style & CodeBlockStyle.SegmentationSymbols) != 0;
            bool restart = (style & CodeBlockStyle.ResetContextOnPass) != 0;
            bool bypass = (style & CodeBlockStyle.SelectiveBypass) != 0;
            bool vsc = (style & CodeBlockStyle.VerticallyCausal) != 0;
            bool termAll = (style & CodeBlockStyle.TerminationOnPass) != 0;

            int firstBitPlane = sub.MagnitudeBits - 1 - firstInclusionZeroBitPlanes;
            if (firstBitPlane < 0) firstBitPlane = 0;

            var decoder = new Tier1CodeBlockDecoder(
                width, height, orientation, firstBitPlane,
                segSym: segSym, restart: restart, bypass: bypass, vsc: vsc);

            // Without TERMALL the encoder only terminates the MQ / raw
            // stream at LAZY transitions (raw↔MQ flips) and at the very end
            // of the code-block — NOT at contribution / packet boundaries
            // (Annex A.7.2 read together with D.6). The Tier-2 parser
            // emits one segment per contribution, so consecutive segments
            // of the same type (both MQ or both raw) accumulated across
            // contributions belong to the SAME natural codeblock segment
            // and must share one decoder. Group consecutive same-type
            // segments into one stream before dispatch.
            //
            // TERMALL is the opposite: every pass is its own terminated
            // segment, never fused with neighbours.
            if (termAll)
            {
                foreach (CodeBlockSegment seg in segments)
                {
                    if (seg.PassCount == 0) continue;
                    if (seg.IsRaw)
                    {
                        decoder.RunRawPasses(seg.Bytes, 0, seg.Bytes.Length, seg.PassCount);
                    }
                    else
                    {
                        var mq = new Jp2MqDecoder(seg.Bytes, 0, seg.Bytes.Length);
                        decoder.RunPasses(mq, seg.PassCount);
                    }
                }
                return decoder.ExtractCoefficients();
            }

            // Group consecutive same-type segments and dispatch as one
            // stream per group.
            var groupStart = 0;
            while (groupStart < segments.Count)
            {
                while (groupStart < segments.Count && segments[groupStart].PassCount == 0)
                    groupStart++;
                if (groupStart >= segments.Count) break;

                bool groupIsRaw = segments[groupStart].IsRaw;
                int groupEnd = groupStart;
                var groupPasses = 0;
                var groupBytes = 0;
                while (groupEnd < segments.Count &&
                       (segments[groupEnd].PassCount == 0 || segments[groupEnd].IsRaw == groupIsRaw))
                {
                    groupPasses += segments[groupEnd].PassCount;
                    groupBytes += segments[groupEnd].Bytes.Length;
                    groupEnd++;
                }

                if (groupPasses > 0)
                {
                    var concat = new byte[groupBytes];
                    var offset = 0;
                    for (int i = groupStart; i < groupEnd; i++)
                    {
                        byte[] segBytes = segments[i].Bytes;
                        Buffer.BlockCopy(segBytes, 0, concat, offset, segBytes.Length);
                        offset += segBytes.Length;
                    }

                    if (groupIsRaw)
                    {
                        decoder.RunRawPasses(concat, 0, concat.Length, groupPasses);
                    }
                    else
                    {
                        var mq = new Jp2MqDecoder(concat, 0, concat.Length);
                        decoder.RunPasses(mq, groupPasses);
                    }
                }

                groupStart = groupEnd;
            }

            return decoder.ExtractCoefficients();
        }

        // ---- Helpers -------------------------------------------------------

        private static int ResolveSubbandIndex(ResolutionState res, SubbandOrientation orientation)
        {
            for (var i = 0; i < res.Subbands.Length; i++)
                if (res.Subbands[i].Orientation == orientation) return i;
            throw new ArgumentException($"Subband orientation {orientation} not present at resolution {res.Resolution}.");
        }

        /// <summary>
        /// Compute M_b − N_b — the count of bit-planes truncated below the
        /// least-significant decoded bit-plane. Bit-plane numbering per
        /// Annex D.6: pass 0 is CUP at the first non-zero plane
        /// (M_b − 1 − ZeroBitPlanes); subsequent passes step down in
        /// SPP / MRP / CUP cycles. A code-block contributing P passes thus
        /// covers ceil((P + 2) / 3) bit-planes starting from
        /// (M_b − 1 − ZeroBitPlanes) downward.
        /// </summary>
        private static int ComputeMissingBitPlanes(int passCount, int magnitudeBits, int zeroBitPlanes)
        {
            if (passCount <= 0) return magnitudeBits;
            int decodedBitPlanes = (passCount + 2) / 3;
            // First non-zero bit-plane index is (magnitudeBits - 1 - zeroBitPlanes).
            // After decoding decodedBitPlanes planes starting there and stepping down,
            // the lowest decoded plane is (magnitudeBits - zeroBitPlanes - decodedBitPlanes).
            // Missing bit-planes = that lowest decoded index = max(0, M_b - K - N_b).
            int missing = magnitudeBits - zeroBitPlanes - decodedBitPlanes;
            return Math.Max(0, missing);
        }

        private static int[,] ClipInt(int[,] grid, int precision, bool isSigned)
        {
            int min, max;
            if (isSigned)
            {
                min = -(1 << (precision - 1));
                max = (1 << (precision - 1)) - 1;
            }
            else
            {
                min = 0;
                max = (1 << precision) - 1;
            }
            int height = grid.GetLength(0);
            int width = grid.GetLength(1);
            var result = new int[height, width];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    result[y, x] = Math.Min(max, Math.Max(min, grid[y, x]));
            return result;
        }

        private static int[,] RoundAndClip(float[,] grid, int precision, bool isSigned)
        {
            int min, max;
            if (isSigned)
            {
                min = -(1 << (precision - 1));
                max = (1 << (precision - 1)) - 1;
            }
            else
            {
                min = 0;
                max = (1 << precision) - 1;
            }
            int height = grid.GetLength(0);
            int width = grid.GetLength(1);
            var result = new int[height, width];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var v = (int)Math.Round(grid[y, x], MidpointRounding.AwayFromZero);
                    result[y, x] = Math.Min(max, Math.Max(min, v));
                }
            return result;
        }
    }

    /// <summary>
    /// Output of <see cref="TileDecoder.DecodeTile"/>: per-component sample
    /// grids on the component grid plus the tile's reference-grid rectangle
    /// (used by the orchestrator to place the tile in the final image).
    /// </summary>
    internal sealed class TileDecodeResult
    {
        public CanvasRect TileRectOnReferenceGrid { get; }
        public int[][,] ComponentSamples { get; }
        public TileComponentState[] ComponentStates { get; }

        public TileDecodeResult(
            CanvasRect tileRectOnReferenceGrid,
            int[][,] componentSamples,
            TileComponentState[] componentStates)
        {
            TileRectOnReferenceGrid = tileRectOnReferenceGrid;
            ComponentSamples = componentSamples;
            ComponentStates = componentStates;
        }
    }

    /// <summary>
    /// Simple byte cursor over the tile's concatenated packet body. The
    /// orchestrator pokes at the buffer directly when constructing
    /// <see cref="PacketHeaderBitReader"/>; this struct owns the cursor.
    /// </summary>
    internal sealed class PacketBodyStream
    {
        public byte[] Buffer { get; }
        public int Position { get; private set; }
        public int Remaining => Buffer.Length - Position;
        public bool IsAtEnd => Position >= Buffer.Length;

        public PacketBodyStream(byte[] buffer)
        {
            Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            Position = 0;
        }

        public void Advance(int count)
        {
            if (count < 0 || count > Remaining)
                throw new ArgumentOutOfRangeException(nameof(count), count, null);
            Position += count;
        }
    }
}
