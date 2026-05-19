using System;

namespace ICCSharp.Profile;

/// <summary>
/// ICC.1:2010 §7.2.11. Low 16 bits are ICC-defined; high 16 bits are vendor-specific.
/// </summary>
[Flags]
public enum ProfileFlags : uint
{
    None                   = 0,
    Embedded               = 1u << 0,
    NotIndependent         = 1u << 1,
    // Bits 2-15 reserved for ICC; 16-31 vendor-defined.
}
