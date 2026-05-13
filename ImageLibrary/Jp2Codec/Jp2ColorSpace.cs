namespace Jp2Codec
{
    /// <summary>
    /// Color-space hint carried by a JP2 file (colr box, ISO/IEC 15444-1 I.5.3.3).
    /// Unspecified for raw J2K codestreams (where the consumer must infer color
    /// space from external context, e.g. a PDF /ColorSpace entry).
    /// </summary>
    public enum Jp2ColorSpace
    {
        /// <summary>No JP2 wrapper, or colr box not present.</summary>
        Unspecified = 0,

        /// <summary>sRGB (Enumerated method, EnumCS = 16).</summary>
        Srgb = 16,

        /// <summary>Greyscale (Enumerated method, EnumCS = 17).</summary>
        Greyscale = 17,

        /// <summary>sYCC (Enumerated method, EnumCS = 18).</summary>
        SrgbYcc = 18,

        /// <summary>Restricted ICC profile (colr Method = 2).</summary>
        RestrictedIcc = 100,

        /// <summary>Any-ICC profile (colr Method = 3, JP2 Part 2 only).</summary>
        AnyIcc = 101,
    }
}
