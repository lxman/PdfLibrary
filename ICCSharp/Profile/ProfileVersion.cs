using System;

namespace ICCSharp.Profile;

/// <summary>
/// ICC.1:2010 §7.2.4. Stored on disk as a four-byte big-endian field:
///   byte 0 = major version (BCD)
///   byte 1 = high nibble minor, low nibble bug-fix (each BCD)
///   bytes 2-3 = reserved (must be zero, preserved here verbatim).
/// </summary>
public readonly struct ProfileVersion : IEquatable<ProfileVersion>, IComparable<ProfileVersion>
{
    public byte Major { get; }
    public byte Minor { get; }
    public byte BugFix { get; }
    public ushort Reserved { get; }

    public ProfileVersion(byte major, byte minor, byte bugFix, ushort reserved = 0)
    {
        Major = major;
        Minor = minor;
        BugFix = bugFix;
        Reserved = reserved;
    }

    /// <summary>Reads the four-byte field as written in a profile header.</summary>
    public static ProfileVersion FromRaw(uint raw)
    {
        var major = (byte)((raw >> 24) & 0xFF);
        var byte1 = (byte)((raw >> 16) & 0xFF);
        var minor = (byte)((byte1 >> 4) & 0x0F);
        var bug   = (byte)(byte1 & 0x0F);
        var reserved = (ushort)(raw & 0xFFFF);
        return new ProfileVersion(major, minor, bug, reserved);
    }

    public uint ToRaw()
    {
        var byte1 = (byte)(((Minor & 0x0F) << 4) | (BugFix & 0x0F));
        return ((uint)Major << 24) | ((uint)byte1 << 16) | Reserved;
    }

    public bool Equals(ProfileVersion other)
        => Major == other.Major && Minor == other.Minor && BugFix == other.BugFix && Reserved == other.Reserved;

    public override bool Equals(object? obj) => obj is ProfileVersion v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, BugFix, Reserved);

    public int CompareTo(ProfileVersion other)
    {
        int c = Major.CompareTo(other.Major); if (c != 0) return c;
        c = Minor.CompareTo(other.Minor); if (c != 0) return c;
        return BugFix.CompareTo(other.BugFix);
    }

    public static bool operator ==(ProfileVersion a, ProfileVersion b) => a.Equals(b);
    public static bool operator !=(ProfileVersion a, ProfileVersion b) => !a.Equals(b);
    public static bool operator < (ProfileVersion a, ProfileVersion b) => a.CompareTo(b) <  0;
    public static bool operator <=(ProfileVersion a, ProfileVersion b) => a.CompareTo(b) <= 0;
    public static bool operator > (ProfileVersion a, ProfileVersion b) => a.CompareTo(b) >  0;
    public static bool operator >=(ProfileVersion a, ProfileVersion b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{BugFix}";
}
