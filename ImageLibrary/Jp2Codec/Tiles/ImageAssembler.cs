using System.Collections.Generic;
using System.IO;
using Jp2Codec.Codestream;
using Jp2Codec.Codestream.Segments;
using Jp2Codec.Geometry;
using Jp2Codec.Jp2File;
using Jp2Codec.TileAssembly;

namespace Jp2Codec.Tiles
{
    /// <summary>
    /// Top-level orchestration: takes the parsed main-header plus the
    /// already-walked tile-part assembly result and produces per-component
    /// flat sample arrays sized at the image canvas. Composes tile-component
    /// sample grids into the final per-component raster by stamping each
    /// tile's grid at its tile-component origin (B.5).
    /// </summary>
    internal static class ImageAssembler
    {
        public static Jp2DecodeResult Decode(
            MainHeader mainHeader,
            IReadOnlyList<AssembledTile> assembledTiles,
            Jp2FileInfo fileInfo)
        {
            SizSegment siz = mainHeader.Siz;
            int numComponents = siz.NumberOfComponents;
            int numXtiles = NumberOfTilesX(siz);
            int numYtiles = NumberOfTilesY(siz);

            // Allocate the per-component output samples sized at the COMPONENT canvas.
            var componentWidth = new int[numComponents];
            var componentHeight = new int[numComponents];
            var componentSamples = new int[numComponents][];
            var componentPrecision = new int[numComponents];
            var componentSigned = new bool[numComponents];
            for (var c = 0; c < numComponents; c++)
            {
                componentWidth[c] = siz.ComponentWidth(c);
                componentHeight[c] = siz.ComponentHeight(c);
                componentSamples[c] = new int[componentWidth[c] * componentHeight[c]];
                componentPrecision[c] = siz.Components[c].BitDepth;
                componentSigned[c] = siz.Components[c].IsSigned;
            }

            // Per-component origin on the component grid (A.5.1):
            //   compOriginX_c = ceil(XOsiz / XRsiz_c)
            // tcRect coords from TileGeometry are on the same component grid,
            // so subtracting the component origin yields indices into
            // componentSamples[c] (which is sized at the image extent, not
            // the reference grid).
            var compOriginX = new int[numComponents];
            var compOriginY = new int[numComponents];
            for (var c = 0; c < numComponents; c++)
            {
                byte xr = siz.Components[c].HorizontalSubsampling;
                byte yr = siz.Components[c].VerticalSubsampling;
                compOriginX[c] = (int)((siz.ImageHorizontalOffset + xr - 1) / xr);
                compOriginY[c] = (int)((siz.ImageVerticalOffset + yr - 1) / yr);
            }

            foreach (AssembledTile tile in assembledTiles)
            {
                int tileU = tile.TileIndex % numXtiles;
                int tileV = tile.TileIndex / numXtiles;
                TileDecodeResult decoded = TileDecoder.DecodeTile(
                    siz, mainHeader.Cod, mainHeader.Qcd,
                    mainHeader.CocOverrides, mainHeader.QccOverrides,
                    tile, tileU, tileV);

                for (var c = 0; c < numComponents; c++)
                {
                    int[,] grid = decoded.ComponentSamples[c];
                    CanvasRect tcRect = decoded.ComponentStates[c].TileComponentCanvas;
                    int dstStride = componentWidth[c];
                    int[] dst = componentSamples[c];
                    int gridH = grid.GetLength(0);
                    int gridW = grid.GetLength(1);
                    int tileX0 = tcRect.X0 - compOriginX[c];
                    int tileY0 = tcRect.Y0 - compOriginY[c];
                    for (var y = 0; y < gridH; y++)
                    {
                        int destRowStart = (tileY0 + y) * dstStride + tileX0;
                        for (var x = 0; x < gridW; x++)
                        {
                            dst[destRowStart + x] = grid[y, x];
                        }
                    }
                }
            }

            if (fileInfo.Palette is not null && fileInfo.ComponentMapping is not null)
            {
                return ExpandPalette(
                    siz.ImageWidth, siz.ImageHeight, fileInfo,
                    componentSamples, componentWidth, componentHeight);
            }

            return new Jp2DecodeResult(
                width: siz.ImageWidth,
                height: siz.ImageHeight,
                componentData: componentSamples,
                componentWidth: componentWidth,
                componentHeight: componentHeight,
                componentPrecision: componentPrecision,
                componentSigned: componentSigned,
                colorSpace: fileInfo.ColorSpace,
                iccProfile: fileInfo.IccProfile,
                associationToComponent: FlattenChannelDefinition(fileInfo.ChannelDefinition));
        }

        /// <summary>
        /// Expand codestream components through a JP2 palette (pclr) + component
        /// mapping (cmap) per ISO/IEC 15444-1 I.5.3.4–5. Each output channel
        /// described by <see cref="JpComponentMapping"/> is produced either by
        /// direct passthrough of the named codestream component (MTYP=0) or by
        /// looking up the codestream sample in the named palette column
        /// (MTYP=1). Output width/height matches the source codestream
        /// component's dimensions; output precision matches the palette
        /// column's bit depth.
        /// </summary>
        private static Jp2DecodeResult ExpandPalette(
            int imageWidth, int imageHeight,
            Jp2FileInfo fileInfo,
            int[][] codestreamSamples,
            int[] codestreamWidth, int[] codestreamHeight)
        {
            JpPalette palette = fileInfo.Palette!;
            JpComponentMapping mapping = fileInfo.ComponentMapping!;

            int outChannels = mapping.NumChannels;
            var outData = new int[outChannels][];
            var outWidth = new int[outChannels];
            var outHeight = new int[outChannels];
            var outPrecision = new int[outChannels];
            var outSigned = new bool[outChannels];

            for (var ch = 0; ch < outChannels; ch++)
            {
                int cmp = mapping.ComponentIndex[ch];
                if (cmp < 0 || cmp >= codestreamSamples.Length)
                    throw new InvalidDataException(
                        $"cmap channel {ch}: CMP {cmp} out of range (codestream has {codestreamSamples.Length} components).");

                int[] source = codestreamSamples[cmp];
                int w = codestreamWidth[cmp];
                int h = codestreamHeight[cmp];
                outWidth[ch] = w;
                outHeight[ch] = h;

                if (mapping.MappingType[ch] == 0)
                {
                    // Direct passthrough — share buffer with the codestream component.
                    outData[ch] = source;
                    outPrecision[ch] = fileInfo.BitsPerComponent.Length switch
                    {
                        0 => 8,
                        1 => fileInfo.BitsPerComponent[0],
                        _ => fileInfo.BitsPerComponent[cmp],
                    };
                    outSigned[ch] = fileInfo.ComponentSigned.Length switch
                    {
                        0 => false,
                        1 => fileInfo.ComponentSigned[0],
                        _ => fileInfo.ComponentSigned[cmp],
                    };
                    continue;
                }

                int pcol = mapping.PaletteColumn[ch];
                if (pcol < 0 || pcol >= palette.NumColumns)
                    throw new InvalidDataException(
                        $"cmap channel {ch}: PCOL {pcol} out of range (palette has {palette.NumColumns} columns).");

                outPrecision[ch] = palette.BitDepths[pcol];
                outSigned[ch] = palette.Signed[pcol];

                var lut = new int[palette.NumEntries];
                for (var i = 0; i < palette.NumEntries; i++)
                    lut[i] = palette.Entries[i, pcol];

                var destination = new int[source.Length];
                for (var i = 0; i < source.Length; i++)
                {
                    int idx = source[i];
                    if ((uint)idx >= (uint)palette.NumEntries)
                        throw new InvalidDataException(
                            $"Palette lookup at sample {i}: index {idx} out of range [0, {palette.NumEntries}).");
                    destination[i] = lut[idx];
                }
                outData[ch] = destination;
            }

            return new Jp2DecodeResult(
                width: imageWidth,
                height: imageHeight,
                componentData: outData,
                componentWidth: outWidth,
                componentHeight: outHeight,
                componentPrecision: outPrecision,
                componentSigned: outSigned,
                colorSpace: fileInfo.ColorSpace,
                iccProfile: fileInfo.IccProfile,
                associationToComponent: FlattenChannelDefinition(fileInfo.ChannelDefinition));
        }

        private static int[]? FlattenChannelDefinition(JpChannelDefinition? cdef)
        {
            if (cdef is null) return null;
            int n = cdef.Association.Length;
            var flat = new int[n * 2];
            for (var i = 0; i < n; i++)
            {
                flat[i * 2]     = cdef.Association[i];
                flat[i * 2 + 1] = cdef.ComponentIndex[i];
            }
            return flat;
        }

        private static int NumberOfTilesX(SizSegment siz)
        {
            // B.4: numXtiles = ceil((Xsiz - XTOsiz) / XTsiz).
            long xsiz = siz.ReferenceGridWidth;
            long xtosiz = siz.TileHorizontalOffset;
            long xtsiz = siz.TileWidth;
            long n = (xsiz - xtosiz + xtsiz - 1) / xtsiz;
            return checked((int)n);
        }

        private static int NumberOfTilesY(SizSegment siz)
        {
            long ysiz = siz.ReferenceGridHeight;
            long ytosiz = siz.TileVerticalOffset;
            long ytsiz = siz.TileHeight;
            long n = (ysiz - ytosiz + ytsiz - 1) / ytsiz;
            return checked((int)n);
        }
    }
}
