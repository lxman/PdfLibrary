using System;
using Jbig2Decoder.Image;

namespace Jbig2Decoder.Region
{
    /// <summary>
    /// Result of a pattern-dictionary segment decode (T.88 §6.7).
    ///
    /// A pattern dictionary holds <c>GRAYMAX + 1</c> equally-sized bitmap patterns
    /// indexed by gray value. Halftone region segments reference one of these by
    /// segment number to translate per-cell gray values into bitmap composites.
    /// </summary>
    internal sealed class PatternDictionary
    {
        public Bitmap[] Patterns { get; }
        public int PatternWidth { get; }
        public int PatternHeight { get; }

        public PatternDictionary(Bitmap[] patterns, int patternWidth, int patternHeight)
        {
            Patterns = patterns ?? throw new ArgumentNullException(nameof(patterns));
            PatternWidth = patternWidth;
            PatternHeight = patternHeight;
        }
    }
}
