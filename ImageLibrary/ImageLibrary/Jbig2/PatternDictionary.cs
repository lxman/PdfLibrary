using System;

namespace ImageLibrary.Jbig2;

/// <summary>
/// Represents a pattern dictionary containing patterns for halftone regions.
/// T.88 Section 7.4.4.
/// </summary>
internal sealed class PatternDictionary
{
    private readonly Bitmap[] _patterns;

    /// <summary>
    /// Width of each pattern in pixels.
    /// </summary>
    public int PatternWidth { get; }

    /// <summary>
    /// Height of each pattern in pixels.
    /// </summary>
    public int PatternHeight { get; }

    /// <summary>
    /// Number of patterns in this dictionary.
    /// </summary>
    public int Count => _patterns.Length;

    /// <summary>
    /// Get a pattern by index.
    /// </summary>
    public Bitmap this[int index] => _patterns[index];

    public PatternDictionary(int patternWidth, int patternHeight, Bitmap[] patterns)
    {
        PatternWidth = patternWidth;
        PatternHeight = patternHeight;
        _patterns = patterns ?? throw new ArgumentNullException(nameof(patterns));
    }
}

/// <summary>
/// Parameters for pattern dictionary decoding.
/// </summary>
internal sealed class PatternDictionaryParams
{
    /// <summary>
    /// Width of each pattern in pixels (HDW).
    /// </summary>
    public int PatternWidth { get; set; }

    /// <summary>
    /// Height of each pattern in pixels (HDH).
    /// </summary>
    public int PatternHeight { get; set; }

    /// <summary>
    /// Maximum gray value - number of patterns is GRAYMAX + 1.
    /// </summary>
    public int GrayMax { get; set; }

    /// <summary>
    /// Whether to use MMR encoding (HDMMR flag).
    /// </summary>
    public bool UseMmr { get; set; }

    /// <summary>
    /// Template for generic region decoding (0-3).
    /// </summary>
    public int Template { get; set; }

    /// <summary>
    /// Adaptive template pixels (for template 0).
    /// </summary>
    public (int dx, int dy)[] AdaptivePixels { get; set; } = [];
}
