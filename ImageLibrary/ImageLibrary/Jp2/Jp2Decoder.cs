using System;
using System.IO;
using System.Linq;
using ImageLibrary.Jp2.Dequantization;
using ImageLibrary.Jp2.Pipeline;
using ImageLibrary.Jp2.PostProcess;
using ImageLibrary.Jp2.Tier1;
using ImageLibrary.Jp2.Tier2;
using ImageLibrary.Jp2.Wavelet;

namespace ImageLibrary.Jp2;

/// <summary>
/// Main JPEG2000 decoder that orchestrates all pipeline stages.
/// </summary>
public class Jp2Decoder
{
    private readonly Jp2Codestream _codestream;
    private readonly byte[] _codestreamData;
    private readonly int _jp2ColorSpace;
    private readonly Jp2Palette? _palette;
    private readonly ComponentMapping[]? _componentMappings;

    // Pipeline stages
    private readonly Tier2Decoder _tier2;
    private readonly Tier1Decoder _tier1;
    private readonly Dequantizer _dequantizer;
    private readonly InverseDwt _inverseDwt;
    private readonly PostProcessor _postProcessor;

    // JP2 color space constants
    /// <summary>
    /// Color space constant for sRGB.
    /// </summary>
    public const int ColorSpaceSRGB = 16;

    /// <summary>
    /// Color space constant for greyscale.
    /// </summary>
    public const int ColorSpaceGreyscale = 17;

    /// <summary>
    /// Color space constant for YCC (sYCC).
    /// </summary>
    public const int ColorSpaceYCC = 18;

    /// <summary>
    /// Initializes a new instance of the <see cref="Jp2Decoder"/> class from JPEG2000 data.
    /// </summary>
    /// <param name="data">The JPEG2000 file data (JP2 or raw codestream).</param>
    public Jp2Decoder(byte[] data)
    {
        // Check if this is a JP2 file or raw codestream
        Jp2FileInfo? fileInfo = null;
        if (Jp2FileReader.IsRawCodestream(data))
        {
            // Raw codestream - use data directly
            _codestreamData = data;
            _jp2ColorSpace = 0; // Unknown
            _palette = null;
            _componentMappings = null;
        }
        else
        {
            // JP2 file - extract codestream from boxes
            var fileReader = new Jp2FileReader(data);
            fileInfo = fileReader.Read();

            _codestreamData = fileInfo.CodestreamData ?? throw new Jp2Exception("No codestream found in data");
            _jp2ColorSpace = fileInfo.ColorSpace;
            _palette = fileInfo.Palette;
            _componentMappings = fileInfo.ComponentMappings;
        }

        // Parse main header
        var codestreamReader = new CodestreamReader(_codestreamData);
        _codestream = codestreamReader.ReadMainHeader();

        // Read tile-parts
        while (true)
        {
            Jp2TilePart? tilePart = codestreamReader.ReadTilePart();
            if (tilePart == null) break;
            _codestream.TileParts.Add(tilePart);
        }

        // Initialize pipeline stages
        _tier2 = new Tier2Decoder(_codestream);
        _tier1 = new Tier1Decoder(_codestream);
        _dequantizer = new Dequantizer(_codestream);
        _inverseDwt = new InverseDwt(_codestream.CodingParameters.WaveletType);
        _postProcessor = new PostProcessor(_codestream, _jp2ColorSpace, fileInfo?.ChannelDefinitions);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Jp2Decoder"/> class from a stream.
    /// </summary>
    /// <param name="stream">Stream containing JPEG2000 data (JP2 or raw codestream).</param>
    public Jp2Decoder(Stream stream) : this(ReadStreamToArray(stream))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Jp2Decoder"/> class from a file path.
    /// </summary>
    /// <param name="path">Path to JPEG2000 file (JP2 or raw codestream).</param>
    public Jp2Decoder(string path) : this(File.ReadAllBytes(path))
    {
    }

    private static byte[] ReadStreamToArray(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Gets the image width.
    /// </summary>
    public int Width => _codestream.Frame.Width;

    /// <summary>
    /// Gets the image height.
    /// </summary>
    public int Height => _codestream.Frame.Height;

    /// <summary>
    /// Gets the number of components.
    /// </summary>
    public int ComponentCount => _codestream.Frame.ComponentCount;

    /// <summary>
    /// Gets the parsed codestream info.
    /// </summary>
    public Jp2Codestream Codestream => _codestream;

    /// <summary>
    /// Gets the JP2 color space (16=sRGB, 17=Greyscale, 18=sYCC).
    /// </summary>
    public int Jp2ColorSpace => _jp2ColorSpace;

    /// <summary>
    /// Gets a palette entry value for debugging.
    /// </summary>
    public int GetPaletteEntry(int index, int column)
    {
        if (_palette == null) return -1;
        if (index >= _palette.NumEntries || column >= _palette.NumColumns) return -1;
        return _palette.Entries[index, column];
    }

    /// <summary>
    /// Decodes the image and returns interleaved pixel data.
    /// </summary>
    public byte[] Decode()
    {
        if (_codestream.TileParts.Count == 0)
        {
            throw new Jp2Exception("No tile-parts found in codestream");
        }

        // For single-tile images, decode directly
        byte[] result = _codestream.Frame.TileCount == 1
            ? DecodeTile(0)
            // For multi-tile images, decode each tile and assemble
            : DecodeAllTiles();

        // Apply palette mapping if present
        if (_palette != null && _componentMappings != null)
        {
            result = ApplyPaletteMapping(result);
        }

        return result;
    }

    /// <summary>
    /// Decodes a single tile and returns pixel data.
    /// </summary>
    public byte[] DecodeTile(int tileIndex)
    {
        Jp2TilePart? tilePart = _codestream.TileParts.FirstOrDefault(tp => tp.TileIndex == tileIndex);
        if (tilePart == null)
        {
            throw new Jp2Exception($"Tile {tileIndex} not found");
        }

        // Calculate tile dimensions
        int numTilesX = _codestream.Frame.NumTilesX;
        int tileX = tileIndex % numTilesX;
        int tileY = tileIndex / numTilesX;

        int tileStartX = tileX * _codestream.Frame.TileWidth + _codestream.Frame.TileXOffset;
        int tileStartY = tileY * _codestream.Frame.TileHeight + _codestream.Frame.TileYOffset;
        int tileEndX = Math.Min(tileStartX + _codestream.Frame.TileWidth, _codestream.Frame.Width);
        int tileEndY = Math.Min(tileStartY + _codestream.Frame.TileHeight, _codestream.Frame.Height);
        int tileWidth = tileEndX - tileStartX;
        int tileHeight = tileEndY - tileStartY;

        // Decode each component through the pipeline
        var reconstructedComponents = new double[_codestream.Frame.ComponentCount][,];

        Tier2Output[] tier2Outputs = _tier2.DecodeAllComponents(tilePart);

        for (var c = 0; c < _codestream.Frame.ComponentCount; c++)
        {
            // Tier-2: packet parsing
            Tier2Output tier2Output = tier2Outputs[c];

            // Tier-1: EBCOT decoding
            QuantizedSubband[] subbands = _tier1.DecodeToSubbands(tier2Output);

            // Dequantization
            DwtCoefficients dwtCoefs = _dequantizer.DequantizeAll(subbands, c);

            // Inverse DWT
            double[,] reconstructed = _inverseDwt.Process(dwtCoefs);

            reconstructedComponents[c] = reconstructed;
        }

        // Create reconstructed tile
        var tile = new ReconstructedTile
        {
            TileIndex = tileIndex,
            TileX = tileX,
            TileY = tileY,
            Width = tileWidth,
            Height = tileHeight,
            Components = reconstructedComponents,
        };

        // Post-processing (color transform, level shift, clamping)
        return _postProcessor.Process(tile);
    }

    /// <summary>
    /// Decodes all tiles and assembles the complete image.
    /// </summary>
    private byte[] DecodeAllTiles()
    {
        int width = _codestream.Frame.Width;
        int height = _codestream.Frame.Height;
        int numComponents = _codestream.Frame.ComponentCount;
        int numTilesX = _codestream.Frame.NumTilesX;
        int numTilesY = _codestream.Frame.NumTilesY;

        var result = new byte[width * height * numComponents];

        for (var ty = 0; ty < numTilesY; ty++)
        {
            for (var tx = 0; tx < numTilesX; tx++)
            {
                int tileIndex = ty * numTilesX + tx;

                // Decode tile
                byte[] tileData = DecodeTile(tileIndex);

                // Calculate tile position
                int tileStartX = tx * _codestream.Frame.TileWidth + _codestream.Frame.TileXOffset;
                int tileStartY = ty * _codestream.Frame.TileHeight + _codestream.Frame.TileYOffset;
                int tileEndX = Math.Min(tileStartX + _codestream.Frame.TileWidth, width);
                int tileEndY = Math.Min(tileStartY + _codestream.Frame.TileHeight, height);
                int tileWidth = tileEndX - tileStartX;
                int tileHeight = tileEndY - tileStartY;

                // Copy tile data to result
                for (var y = 0; y < tileHeight; y++)
                {
                    int srcOffset = y * tileWidth * numComponents;
                    int dstOffset = ((tileStartY + y) * width + tileStartX) * numComponents;
                    Array.Copy(tileData, srcOffset, result, dstOffset, tileWidth * numComponents);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Decodes to a grayscale image (first component only).
    /// </summary>
    public byte[] DecodeGrayscale()
    {
        if (_codestream.TileParts.Count == 0)
        {
            throw new Jp2Exception("No tile-parts found in codestream");
        }

        Jp2TilePart tilePart = _codestream.TileParts[0];

        // Decode the first component only
        Tier2Output tier2Output = _tier2.Process(tilePart);
        QuantizedSubband[] subbands = _tier1.DecodeToSubbands(tier2Output);
        DwtCoefficients dwtCoefs = _dequantizer.DequantizeAll(subbands, 0);
        double[,] reconstructed = _inverseDwt.Process(dwtCoefs);

        // Post-process to bytes
        var grayscaleProcessor = new GrayscalePostProcessor(_codestream);
        return grayscaleProcessor.Process(reconstructed);
    }

    /// <summary>
    /// Decodes the image without palette mapping (returns raw indices for palette images).
    /// </summary>
    public byte[] DecodeRawIndices()
    {
        if (_codestream.TileParts.Count == 0)
        {
            throw new Jp2Exception("No tile-parts found in codestream");
        }

        // For single-tile images, decode directly
        return _codestream.Frame.TileCount == 1
            ? DecodeTile(0)
            : DecodeAllTiles();
    }

    /// <summary>
    /// Gets intermediate data for testing/debugging.
    /// </summary>
    public IntermediateData GetIntermediateData(int tileIndex = 0)
    {
        Jp2TilePart? tilePart = _codestream.TileParts.FirstOrDefault(tp => tp.TileIndex == tileIndex);
        if (tilePart == null)
        {
            return new IntermediateData();
        }

        Tier2Output tier2Output = _tier2.Process(tilePart);
        QuantizedSubband[] subbands = _tier1.DecodeToSubbands(tier2Output);
        DwtCoefficients dwtCoefs = _dequantizer.DequantizeAll(subbands, 0);

        return new IntermediateData
        {
            Tier2Output = tier2Output,
            Subbands = subbands,
            DwtCoefficients = dwtCoefs,
        };
    }

    /// <summary>
    /// Applies palette mapping to convert palette indices to actual color values.
    /// </summary>
    private byte[] ApplyPaletteMapping(byte[] indexData)
    {
        int width = _codestream.Frame.Width;
        int height = _codestream.Frame.Height;
        int numPixels = width * height;

        // Determine the output format based on component mappings
        // Each mapping produces one output component
        int numOutputComponents = _componentMappings!.Length;
        var result = new byte[numPixels * numOutputComponents];

        // For each pixel
        for (var i = 0; i < numPixels; i++)
        {
            // The input is a single-component image containing palette indices
            byte paletteIndex = indexData[i];

            // Apply each component mapping
            for (var m = 0; m < _componentMappings.Length; m++)
            {
                ComponentMapping mapping = _componentMappings[m];

                int value;
                if (mapping.MappingType == 1)
                {
                    // Palette mapping: look up value in palette
                    int col = mapping.PaletteColumn;
                    if (paletteIndex < _palette!.NumEntries && col < _palette.NumColumns)
                    {
                        value = _palette.Entries[paletteIndex, col];
                    }
                    else
                    {
                        value = 0;
                    }
                }
                else
                {
                    // Direct mapping: use the index value directly
                    value = paletteIndex;
                }

                // Clamp to 8-bit
                result[i * numOutputComponents + m] = (byte)Math.Clamp(value, 0, 255);
            }
        }

        return result;
    }
}

/// <summary>
/// Holds intermediate data from decoder stages for testing.
/// </summary>
public class IntermediateData
{
    /// <summary>
    /// Gets or sets the output from the Tier-2 decoding stage (packet parsing).
    /// </summary>
    public Tier2Output? Tier2Output { get; set; }

    /// <summary>
    /// Gets or sets the quantized subbands after Tier-1 decoding.
    /// </summary>
    public QuantizedSubband[]? Subbands { get; set; }

    /// <summary>
    /// Gets or sets the DWT coefficients after dequantization.
    /// </summary>
    public DwtCoefficients? DwtCoefficients { get; set; }
}