using ICCSharp.IO;

namespace ICCSharp.Profile;

/// <summary>
/// ICC.1:2010 §7.2.6 color space encoding. Stored as the raw signature so xCLR (n-channel)
/// values for arbitrary channel counts are preserved verbatim. The known-value constants
/// cover the enumerated entries from the spec.
/// </summary>
public static class ColorSpaceSignatures
{
    public static readonly IccSignature XYZ    = IccSignature.FromAscii("XYZ ");
    public static readonly IccSignature Lab    = IccSignature.FromAscii("Lab ");
    public static readonly IccSignature Luv    = IccSignature.FromAscii("Luv ");
    public static readonly IccSignature YCbCr  = IccSignature.FromAscii("YCbr");
    public static readonly IccSignature Yxy    = IccSignature.FromAscii("Yxy ");
    public static readonly IccSignature RGB    = IccSignature.FromAscii("RGB ");
    public static readonly IccSignature Gray   = IccSignature.FromAscii("GRAY");
    public static readonly IccSignature HSV    = IccSignature.FromAscii("HSV ");
    public static readonly IccSignature HLS    = IccSignature.FromAscii("HLS ");
    public static readonly IccSignature CMYK   = IccSignature.FromAscii("CMYK");
    public static readonly IccSignature CMY    = IccSignature.FromAscii("CMY ");

    public static readonly IccSignature TwoColor    = IccSignature.FromAscii("2CLR");
    public static readonly IccSignature ThreeColor  = IccSignature.FromAscii("3CLR");
    public static readonly IccSignature FourColor   = IccSignature.FromAscii("4CLR");
    public static readonly IccSignature FiveColor   = IccSignature.FromAscii("5CLR");
    public static readonly IccSignature SixColor    = IccSignature.FromAscii("6CLR");
    public static readonly IccSignature SevenColor  = IccSignature.FromAscii("7CLR");
    public static readonly IccSignature EightColor  = IccSignature.FromAscii("8CLR");
    public static readonly IccSignature NineColor   = IccSignature.FromAscii("9CLR");
    public static readonly IccSignature TenColor    = IccSignature.FromAscii("ACLR");
    public static readonly IccSignature ElevenColor = IccSignature.FromAscii("BCLR");
    public static readonly IccSignature TwelveColor = IccSignature.FromAscii("CCLR");
    public static readonly IccSignature ThirteenColor = IccSignature.FromAscii("DCLR");
    public static readonly IccSignature FourteenColor = IccSignature.FromAscii("ECLR");
    public static readonly IccSignature FifteenColor  = IccSignature.FromAscii("FCLR");

    /// <summary>
    /// Returns the number of channels for a known color space signature, or 0 if unknown.
    /// </summary>
    public static int ChannelCount(IccSignature sig)
    {
        if (sig == XYZ || sig == Lab || sig == Luv || sig == YCbCr || sig == Yxy ||
            sig == RGB || sig == HSV || sig == HLS) return 3;
        if (sig == Gray) return 1;
        if (sig == CMYK) return 4;
        if (sig == CMY) return 3;
        if (sig == TwoColor) return 2;
        if (sig == ThreeColor) return 3;
        if (sig == FourColor) return 4;
        if (sig == FiveColor) return 5;
        if (sig == SixColor) return 6;
        if (sig == SevenColor) return 7;
        if (sig == EightColor) return 8;
        if (sig == NineColor) return 9;
        if (sig == TenColor) return 10;
        if (sig == ElevenColor) return 11;
        if (sig == TwelveColor) return 12;
        if (sig == ThirteenColor) return 13;
        if (sig == FourteenColor) return 14;
        if (sig == FifteenColor) return 15;
        return 0;
    }
}
