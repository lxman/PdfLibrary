using System;

namespace ImageLibrary.Jpeg;

/// <summary>
/// Decodes entropy-coded data to extract DCT coefficients.
/// This is Stage 3 of the decoder.
/// </summary>
internal class EntropyDecoder
{
    private readonly JpegFrame _frame;
    private readonly BitReader _bitReader;
    private readonly HuffmanTable[] _dcTables = new HuffmanTable[4];
    private readonly HuffmanTable[] _acTables = new HuffmanTable[4];

    // DC prediction for each component (DPCM)
    private readonly int[] _dcPredictors;

    /// <summary>
    /// Standard JPEG zig-zag order for 8x8 block.
    /// Maps linear index to zig-zag position.
    /// </summary>
    public static readonly byte[] ZigZagOrder =
    [
        0,  1,  8, 16,  9,  2,  3, 10,
        17, 24, 32, 25, 18, 11,  4,  5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13,  6,  7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63
    ];

    public EntropyDecoder(JpegFrame frame, byte[] data)
    {
        _frame = frame;
        _bitReader = new BitReader(data, frame.EntropyDataOffset, frame.EntropyDataLength);
        _dcPredictors = new int[frame.ComponentCount];

        // Build Huffman tables
        BuildHuffmanTables();
    }

    private void BuildHuffmanTables()
    {
        for (var i = 0; i < 4; i++)
        {
            if (_frame.DcHuffmanTables[i] != null)
            {
                _dcTables[i] = new HuffmanTable(_frame.DcHuffmanTables[i]!);
            }

            if (_frame.AcHuffmanTables[i] != null)
            {
                _acTables[i] = new HuffmanTable(_frame.AcHuffmanTables[i]!);
            }
        }
    }

    /// <summary>
    /// Decodes all DCT coefficients for the image.
    /// Returns a 3D array: [component][block][coefficient]
    /// </summary>
    public short[][][] DecodeAllBlocks()
    {
        // Reset DC predictors
        Array.Clear(_dcPredictors, 0, _dcPredictors.Length);

        // Calculate blocks per component
        // Formula: ceil(dimension / (maxSamp * 8)) * componentSamp
        var blocksPerComponent = new (int width, int height)[_frame.ComponentCount];
        for (var c = 0; c < _frame.ComponentCount; c++)
        {
            JpegComponent comp = _frame.Components[c];
            int hBlocks = (_frame.Width + _frame.MaxHorizontalSamplingFactor * 8 - 1)
                          / (_frame.MaxHorizontalSamplingFactor * 8) * comp.HorizontalSamplingFactor;
            int vBlocks = (_frame.Height + _frame.MaxVerticalSamplingFactor * 8 - 1)
                          / (_frame.MaxVerticalSamplingFactor * 8) * comp.VerticalSamplingFactor;
            blocksPerComponent[c] = (hBlocks, vBlocks);
        }

        // Allocate result arrays
        var result = new short[_frame.ComponentCount][][];
        for (var c = 0; c < _frame.ComponentCount; c++)
        {
            (int w, int h) = blocksPerComponent[c];
            int totalBlocks = w * h;
            result[c] = new short[totalBlocks][];
            for (var b = 0; b < totalBlocks; b++)
            {
                result[c][b] = new short[64];
            }
        }

        // Decode MCU by MCU
        int mcuCountX = _frame.McuCountX;
        int mcuCountY = _frame.McuCountY;

        // Reset decode position counter for sequential storage mode
        _decodePosition = 0;

        for (var mcuY = 0; mcuY < mcuCountY; mcuY++)
        {
            for (var mcuX = 0; mcuX < mcuCountX; mcuX++)
            {
                DecodeMcu(mcuX, mcuY, result, blocksPerComponent);
            }
        }

        return result;
    }

    // Track the current decode position for sequential storage
    private int _decodePosition;

    private void DecodeMcu(int mcuX, int mcuY, short[][][] result, (int width, int height)[] blocksPerComponent)
    {
        // Decode each component in the MCU
        for (var c = 0; c < _frame.ComponentCount; c++)
        {
            JpegComponent comp = _frame.Components[c];
            int hSamp = comp.HorizontalSamplingFactor;
            int vSamp = comp.VerticalSamplingFactor;

            // For single-component images, store blocks sequentially by decode order
            // This matches how some decoders (like ImageSharp) handle grayscale with sampling factors > 1
            bool useDecodeOrder = _frame.ComponentCount == 1 && (hSamp > 1 || vSamp > 1);

            // Decode the blocks for this component within the MCU
            for (var blockY = 0; blockY < vSamp; blockY++)
            {
                for (var blockX = 0; blockX < hSamp; blockX++)
                {
                    int blockIndex;
                    if (useDecodeOrder)
                    {
                        // Store sequentially by decode order
                        blockIndex = _decodePosition++;
                    }
                    else
                    {
                        // Calculate the global block position (standard spatial mapping)
                        int globalBlockX = mcuX * hSamp + blockX;
                        int globalBlockY = mcuY * vSamp + blockY;

                        (int compWidth, _) = blocksPerComponent[c];
                        blockIndex = globalBlockY * compWidth + globalBlockX;
                    }

                    // Decode the block
                    DecodeBlock(c, result[c][blockIndex]);
                }
            }
        }
    }

    /// <summary>
    /// Decodes a single 8x8 block of DCT coefficients.
    /// </summary>
    private void DecodeBlock(int componentIndex, short[] block)
    {
        JpegComponent comp = _frame.Components[componentIndex];
        HuffmanTable dcTable = _dcTables[comp.DcTableId];
        HuffmanTable acTable = _acTables[comp.AcTableId];

        // Clear the block
        Array.Clear(block, 0, block.Length);

        // Decode DC coefficient
        int dcCategory = dcTable.DecodeSymbol(_bitReader);
        int dcDiff = _bitReader.ReadSignedValue(dcCategory);
        int dcValue = _dcPredictors[componentIndex] + dcDiff;
        _dcPredictors[componentIndex] = dcValue;
        block[0] = (short)dcValue;

        // Decode AC coefficients
        var k = 1;
        while (k < 64)
        {
            byte symbol = acTable.DecodeSymbol(_bitReader);

            if (symbol == 0x00)
            {
                // EOB - End of Block, remaining coefficients are zero
                break;
            }

            if (symbol == 0xF0)
            {
                // ZRL - 16 zeros
                k += 16;
                continue;
            }

            // Symbol encodes (run, size) where run is in high nibble, size in low nibble
            int runLength = symbol >> 4;
            int acSize = symbol & 0x0F;

            k += runLength; // Skip zeros

            if (k >= 64)
            {
                break;
            }

            int acValue = _bitReader.ReadSignedValue(acSize);

            // Store in zig-zag order
            block[ZigZagOrder[k]] = (short)acValue;
            k++;
        }
    }

    /// <summary>
    /// Decodes a single block and returns it (for testing).
    /// </summary>
    public short[] DecodeSingleBlock(int componentIndex)
    {
        var block = new short[64];
        DecodeBlock(componentIndex, block);
        return block;
    }

    /// <summary>
    /// Resets the decoder state for re-reading.
    /// </summary>
    public void Reset()
    {
        _bitReader.Reset();
        Array.Clear(_dcPredictors, 0, _dcPredictors.Length);
    }
}
