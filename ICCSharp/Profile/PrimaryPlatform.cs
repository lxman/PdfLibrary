using ICCSharp.IO;

namespace ICCSharp.Profile;

/// <summary>ICC.1:2010 §7.2.10. All-zero signature means "unspecified".</summary>
public enum PrimaryPlatform
{
    Unspecified = 0,
    Apple,       // 'APPL'
    Microsoft,   // 'MSFT'
    SGI,         // 'SGI '
    Sun,         // 'SUNW'
    Taligent,    // 'TGNT'
    Unknown,
}

public static class PrimaryPlatformSignatures
{
    public static readonly IccSignature Apple     = IccSignature.FromAscii("APPL");
    public static readonly IccSignature Microsoft = IccSignature.FromAscii("MSFT");
    public static readonly IccSignature SGI       = IccSignature.FromAscii("SGI ");
    public static readonly IccSignature Sun       = IccSignature.FromAscii("SUNW");
    public static readonly IccSignature Taligent  = IccSignature.FromAscii("TGNT");

    public static PrimaryPlatform FromSignature(IccSignature sig)
    {
        if (sig.Value == 0)        return PrimaryPlatform.Unspecified;
        if (sig == Apple)          return PrimaryPlatform.Apple;
        if (sig == Microsoft)      return PrimaryPlatform.Microsoft;
        if (sig == SGI)            return PrimaryPlatform.SGI;
        if (sig == Sun)            return PrimaryPlatform.Sun;
        if (sig == Taligent)       return PrimaryPlatform.Taligent;
        return PrimaryPlatform.Unknown;
    }
}
