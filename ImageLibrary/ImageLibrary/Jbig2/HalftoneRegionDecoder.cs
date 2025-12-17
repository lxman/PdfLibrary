using System;

namespace ImageLibrary.Jbig2;

/// <summary>
/// Parameters for halftone region decoding.
/// T.88 Section 7.4.5.
/// </summary>
internal sealed class HalftoneRegionParams
{
    /// <summary>
    /// Whether to use MMR encoding for gray values (HMMR flag).
    /// </summary>
    public bool UseMmr { get; set; }

    /// <summary>
    /// Template for arithmetic generic region decoding (0-3).
    /// </summary>
    public int Template { get; set; }

    /// <summary>
    /// Whether to enable skip flag (HSKIP).
    /// </summary>
    public bool EnableSkip { get; set; }

    /// <summary>
    /// Combination operator (HCOMBOP).
    /// </summary>
    public CombinationOperator CombinationOp { get; set; }

    /// <summary>
    /// Default pixel value.
    /// </summary>
    public int DefaultPixel { get; set; }

    /// <summary>
    /// Grid width (number of patterns horizontally).
    /// </summary>
    public int GridWidth { get; set; }

    /// <summary>
    /// Grid height (number of patterns vertically).
    /// </summary>
    public int GridHeight { get; set; }

    /// <summary>
    /// Grid vector X component (HGX).
    /// </summary>
    public int GridVectorX { get; set; }

    /// <summary>
    /// Grid vector Y component (HGY).
    /// </summary>
    public int GridVectorY { get; set; }

    /// <summary>
    /// Horizontal spacing between grid cells (HRX).
    /// </summary>
    public int RegionVectorX { get; set; }

    /// <summary>
    /// Vertical spacing between grid cells (HRY).
    /// </summary>
    public int RegionVectorY { get; set; }

    /// <summary>
    /// Adaptive template pixels (for template 0).
    /// </summary>
    public (int dx, int dy)[] AdaptivePixels { get; set; } = [];
}

/// <summary>
/// Decodes halftone region segments (types 22, 23).
/// T.88 Section 6.6.
/// </summary>
internal sealed class HalftoneRegionDecoder
{
    private readonly HalftoneRegionParams _params;
    private readonly PatternDictionary _patternDict;
    private readonly byte[] _data;
    private readonly int _dataOffset;
    private readonly int _dataLength;
    private readonly Jbig2DecoderOptions _options;

    public HalftoneRegionDecoder(
        byte[] data,
        int dataOffset,
        int dataLength,
        HalftoneRegionParams parameters,
        PatternDictionary patternDict,
        Jbig2DecoderOptions? options = null)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _dataOffset = dataOffset;
        _dataLength = dataLength;
        _params = parameters ?? throw new ArgumentNullException(nameof(parameters));
        _patternDict = patternDict ?? throw new ArgumentNullException(nameof(patternDict));
        _options = options ?? Jbig2DecoderOptions.Default;
    }

    /// <summary>
    /// Decode the halftone region.
    /// T.88 Section 6.6.
    /// </summary>
    public Bitmap Decode(int width, int height)
    {
        // Calculate number of bits needed for gray values
        int numPatterns = _patternDict.Count;
        var bitsPerGray = 0;
        int temp = numPatterns - 1;
        while (temp > 0)
        {
            bitsPerGray++;
            temp >>= 1;
        }
        if (bitsPerGray == 0) bitsPerGray = 1;

        // Decode the gray-scale image (each pixel is a gray value index)
        int[,] grayValues = DecodeGrayValues(bitsPerGray);

        // Create the output bitmap
        var result = new Bitmap(width, height);

        // Initialize with default pixel
        if (_params.DefaultPixel != 0)
        {
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    result.SetPixel(x, y, 1);
        }

        // Render patterns at grid positions
        // T.88 6.6.5: For each grid position (m, n):
        //   x = HGX + m*HRX + n*HRY
        //   y = HGY + m*HRY + n*-HRX (note: HRY used for y, -HRX for shearing)
        // Actually per T.88, the formula is simpler for non-sheared grids
        int patternW = _patternDict.PatternWidth;
        int patternH = _patternDict.PatternHeight;

        for (var m = 0; m < _params.GridHeight; m++)
        {
            for (var n = 0; n < _params.GridWidth; n++)
            {
                // Calculate grid position (fixed-point scaled by 256)
                // T.88 6.6.5.2: Per jbig2dec implementation:
                //   x = HGX + m*HRY + n*HRX
                //   y = HGY + m*HRX - n*HRY
                // where m is row index, n is column index in the grid
                int gx = (_params.GridVectorX + m * _params.RegionVectorY + n * _params.RegionVectorX) >> 8;
                int gy = (_params.GridVectorY + m * _params.RegionVectorX - n * _params.RegionVectorY) >> 8;

                // Get gray value (pattern index)
                int grayIdx = grayValues[m, n];
                if (grayIdx < 0 || grayIdx >= numPatterns)
                {
                    continue;
                }

                Bitmap pattern = _patternDict[grayIdx];

                // Render pattern at (gx, gy)
                for (var py = 0; py < patternH; py++)
                {
                    int destY = gy + py;
                    if (destY < 0 || destY >= height) continue;

                    for (var px = 0; px < patternW; px++)
                    {
                        int destX = gx + px;
                        if (destX < 0 || destX >= width) continue;

                        int patternPixel = pattern.GetPixel(px, py);

                        // Apply combination operator
                        switch (_params.CombinationOp)
                        {
                            case CombinationOperator.Or:
                                if (patternPixel != 0)
                                    result.SetPixel(destX, destY, 1);
                                break;
                            case CombinationOperator.And:
                                if (patternPixel == 0)
                                    result.SetPixel(destX, destY, 0);
                                break;
                            case CombinationOperator.Xor:
                                result.SetPixel(destX, destY, result.GetPixel(destX, destY) ^ patternPixel);
                                break;
                            case CombinationOperator.Xnor:
                                result.SetPixel(destX, destY, 1 - (result.GetPixel(destX, destY) ^ patternPixel));
                                break;
                            case CombinationOperator.Replace:
                            default:
                                result.SetPixel(destX, destY, patternPixel);
                                break;
                        }
                    }
                }
            }
        }

        return result;
    }

    private int[,] DecodeGrayValues(int bitsPerGray)
    {
        int gridH = _params.GridHeight;
        int gridW = _params.GridWidth;
        var grayValues = new int[gridH, gridW];

        if (_params.UseMmr)
        {
            // Decode gray values using MMR
            // Gray values are stored as bitsPerGray bit planes
            var bitPlanes = new Bitmap[bitsPerGray];

            int planeOffset = _dataOffset;
            int planeWidth = gridW;
            int planeHeight = gridH;

            for (int b = bitsPerGray - 1; b >= 0; b--)
            {
                // Calculate MMR data length for this plane
                // For MMR, we need to decode each plane separately
                var mmrDecoder = new MmrDecoder(_data, planeOffset, _dataLength - (planeOffset - _dataOffset), planeWidth, planeHeight);
                bitPlanes[b] = mmrDecoder.Decode();
                int consumed = mmrDecoder.BytesConsumed;
                planeOffset += consumed;

                // T.88 Section C.5 step 3(b): XOR this plane with the previous plane (Gray code decoding)
                // GSPLANES[j] = GSPLANES[j+1] XOR GSPLANES[j]
                if (b < bitsPerGray - 1)
                {
                    for (var y = 0; y < gridH; y++)
                    {
                        for (var x = 0; x < gridW; x++)
                        {
                            int val = bitPlanes[b].GetPixel(x, y) ^ bitPlanes[b + 1].GetPixel(x, y);
                            bitPlanes[b].SetPixel(x, y, val);
                        }
                    }
                }
            }

            // Reconstruct gray values from bit planes
            for (var m = 0; m < gridH; m++)
            {
                for (var n = 0; n < gridW; n++)
                {
                    var gray = 0;
                    for (int b = bitsPerGray - 1; b >= 0; b--)
                    {
                        gray = (gray << 1) | bitPlanes[b].GetPixel(n, m);
                    }
                    grayValues[m, n] = gray;
                }
            }
        }
        else
        {
            // Decode gray values using arithmetic coding
            // Each bit plane is decoded separately
            var bitPlanes = new Bitmap[bitsPerGray];
            var arithDecoder = new ArithmeticDecoder(_data, _dataOffset, _dataLength);

            for (int b = bitsPerGray - 1; b >= 0; b--)
            {
                var grDecoder = new GenericRegionDecoder(
                    arithDecoder,
                    _params.Template,
                    _params.AdaptivePixels.Length > 0 ? _params.AdaptivePixels : null,
                    typicalPrediction: false,
                    _options);
                bitPlanes[b] = grDecoder.Decode(gridW, gridH);

                // T.88 Section C.5 step 3(b): XOR this plane with the previous plane (Gray code decoding)
                // GSPLANES[j] = GSPLANES[j+1] XOR GSPLANES[j]
                if (b < bitsPerGray - 1)
                {
                    for (var y = 0; y < gridH; y++)
                    {
                        for (var x = 0; x < gridW; x++)
                        {
                            int val = bitPlanes[b].GetPixel(x, y) ^ bitPlanes[b + 1].GetPixel(x, y);
                            bitPlanes[b].SetPixel(x, y, val);
                        }
                    }
                }
            }

            // Reconstruct gray values from bit planes
            // Each bit plane contributes one bit to the gray value
            // bitPlanes[K-1] is MSB, bitPlanes[0] is LSB
            for (var m = 0; m < gridH; m++)
            {
                for (var n = 0; n < gridW; n++)
                {
                    var gray = 0;
                    for (int b = bitsPerGray - 1; b >= 0; b--)
                    {
                        gray = (gray << 1) | bitPlanes[b].GetPixel(n, m);
                    }
                    grayValues[m, n] = gray;
                }
            }
        }

        return grayValues;
    }
}
