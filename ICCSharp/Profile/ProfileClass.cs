using ICCSharp.IO;

namespace ICCSharp.Profile;

/// <summary>ICC.1:2010 §7.2.5 device/profile class.</summary>
public enum ProfileClass
{
    Unknown = 0,
    Input,            // 'scnr'
    Display,          // 'mntr'
    Output,           // 'prtr'
    DeviceLink,       // 'link'
    ColorSpace,       // 'spac'
    Abstract,         // 'abst'
    NamedColor,       // 'nmcl'
}

public static class ProfileClassSignatures
{
    public static readonly IccSignature Input       = IccSignature.FromAscii("scnr");
    public static readonly IccSignature Display     = IccSignature.FromAscii("mntr");
    public static readonly IccSignature Output      = IccSignature.FromAscii("prtr");
    public static readonly IccSignature DeviceLink  = IccSignature.FromAscii("link");
    public static readonly IccSignature ColorSpace  = IccSignature.FromAscii("spac");
    public static readonly IccSignature Abstract    = IccSignature.FromAscii("abst");
    public static readonly IccSignature NamedColor  = IccSignature.FromAscii("nmcl");

    public static ProfileClass FromSignature(IccSignature sig)
    {
        if (sig == Input)      return ProfileClass.Input;
        if (sig == Display)    return ProfileClass.Display;
        if (sig == Output)     return ProfileClass.Output;
        if (sig == DeviceLink) return ProfileClass.DeviceLink;
        if (sig == ColorSpace) return ProfileClass.ColorSpace;
        if (sig == Abstract)   return ProfileClass.Abstract;
        if (sig == NamedColor) return ProfileClass.NamedColor;
        return ProfileClass.Unknown;
    }
}
