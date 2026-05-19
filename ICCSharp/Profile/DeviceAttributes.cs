using System;

namespace ICCSharp.Profile;

/// <summary>
/// ICC.1:2010 §7.2.14. Low 32 bits are ICC-defined; high 32 bits are vendor-specific.
/// </summary>
[Flags]
public enum DeviceAttributes : ulong
{
    None         = 0,
    Transparency = 1ul << 0, // 0=reflective, 1=transparency
    Matte        = 1ul << 1, // 0=glossy,     1=matte
    Negative     = 1ul << 2, // 0=positive,   1=negative
    BlackAndWhite = 1ul << 3, // 0=color,     1=B&W
    // Bits 4-31 reserved for ICC; 32-63 vendor-defined.
}
