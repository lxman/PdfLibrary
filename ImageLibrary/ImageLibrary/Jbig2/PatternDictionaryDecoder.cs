using System;

namespace ImageLibrary.Jbig2;

/// <summary>
/// Decodes pattern dictionary segments (type 16).
/// T.88 Section 6.7 and 7.4.4.
/// </summary>
internal sealed class PatternDictionaryDecoder
{
    private readonly PatternDictionaryParams _params;
    private readonly byte[] _data;
    private readonly int _dataOffset;
    private readonly int _dataLength;
    private readonly Jbig2DecoderOptions _options;

    public PatternDictionaryDecoder(
        byte[] data,
        int dataOffset,
        int dataLength,
        PatternDictionaryParams parameters,
        Jbig2DecoderOptions? options = null)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _dataOffset = dataOffset;
        _dataLength = dataLength;
        _params = parameters ?? throw new ArgumentNullException(nameof(parameters));
        _options = options ?? Jbig2DecoderOptions.Default;
    }

    /// <summary>
    /// Decode the pattern dictionary.
    /// T.88 Section 6.7.
    /// </summary>
    public PatternDictionary Decode()
    {
        int numPatterns = _params.GrayMax + 1;

        // The collective bitmap contains all patterns side by side
        int collectiveWidth = _params.PatternWidth * numPatterns;
        int collectiveHeight = _params.PatternHeight;

        // Decode the collective bitmap
        Bitmap collectiveBitmap;
        if (_params.UseMmr)
        {
            // Use MMR decoder
            var mmrDecoder = new MmrDecoder(_data, _dataOffset, _dataLength, collectiveWidth, collectiveHeight);
            collectiveBitmap = mmrDecoder.Decode();
        }
        else
        {
            // Use arithmetic generic region decoder
            // Pattern dictionaries don't use typical prediction (TPGDON)
            var arithDecoder = new ArithmeticDecoder(_data, _dataOffset, _dataLength);
            var grDecoder = new GenericRegionDecoder(
                arithDecoder,
                _params.Template,
                _params.AdaptivePixels.Length > 0 ? _params.AdaptivePixels : null,
                typicalPrediction: false,
                _options);
            collectiveBitmap = grDecoder.Decode(collectiveWidth, collectiveHeight);
        }

        // Slice the collective bitmap into individual patterns
        var patterns = new Bitmap[numPatterns];
        for (var i = 0; i < numPatterns; i++)
        {
            int srcX = i * _params.PatternWidth;
            patterns[i] = new Bitmap(_params.PatternWidth, _params.PatternHeight);

            for (var y = 0; y < _params.PatternHeight; y++)
            {
                for (var x = 0; x < _params.PatternWidth; x++)
                {
                    patterns[i].SetPixel(x, y, collectiveBitmap.GetPixel(srcX + x, y));
                }
            }
        }

        return new PatternDictionary(_params.PatternWidth, _params.PatternHeight, patterns);
    }
}
