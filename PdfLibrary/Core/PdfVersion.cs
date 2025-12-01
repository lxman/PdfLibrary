namespace PdfLibrary.Core;

/// <summary>
/// Represents a PDF file format version
/// Supports both PDF 1.x (ISO 32000-1:2008) and PDF 2.x (ISO 32000-2:2020) specifications
/// </summary>
public sealed class PdfVersion : IComparable<PdfVersion>, IEquatable<PdfVersion>
{
    public int Major { get; }
    public int Minor { get; }

    public PdfVersion(int major, int minor)
    {
        if (major is < 1 or > 2)
            throw new ArgumentOutOfRangeException(nameof(major), "PDF major version must be 1 or 2");
        if (minor is < 0 or > 9)
            throw new ArgumentOutOfRangeException(nameof(minor), "PDF minor version must be between 0 and 9");

        Major = major;
        Minor = minor;
    }

    /// <summary>
    /// Gets the version as a decimal number (e.g., 1.7, 2.0)
    /// </summary>
    public double AsDecimal => Major + (Minor / 10.0);

    /// <summary>
    /// Gets the version string (e.g., "1.7", "2.0")
    /// </summary>
    public override string ToString() => $"{Major}.{Minor}";

    /// <summary>
    /// Gets the PDF header format (%PDF-X.Y)
    /// </summary>
    public string ToHeaderString() => $"%PDF-{Major}.{Minor}";

    /// <summary>
    /// Parses a PDF version from a header string (%PDF-X.Y or just X.Y)
    /// </summary>
    public static PdfVersion Parse(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version string cannot be null or empty", nameof(version));

        // Remove %PDF- prefix if present
        var versionStr = version.Replace("%PDF-", "").Trim();

        var parts = versionStr.Split('.');
        if (parts.Length != 2)
            throw new FormatException($"Invalid PDF version format: {version}");

        if (!int.TryParse(parts[0], out var major))
            throw new FormatException($"Invalid major version: {parts[0]}");
        return !int.TryParse(parts[1], out var minor)
            ? throw new FormatException($"Invalid minor version: {parts[1]}")
            : new PdfVersion(major, minor);
    }

    /// <summary>
    /// Tries to parse a PDF version from a string
    /// </summary>
    public static bool TryParse(string version, out PdfVersion? result)
    {
        try
        {
            result = Parse(version);
            return true;
        }
        catch
        {
            result = null;
            return false;
        }
    }

    // Common PDF versions
    public static readonly PdfVersion Pdf10 = new(1, 0);
    public static readonly PdfVersion Pdf11 = new(1, 1);
    public static readonly PdfVersion Pdf12 = new(1, 2);
    public static readonly PdfVersion Pdf13 = new(1, 3);
    public static readonly PdfVersion Pdf14 = new(1, 4);
    public static readonly PdfVersion Pdf15 = new(1, 5);
    public static readonly PdfVersion Pdf16 = new(1, 6);
    public static readonly PdfVersion Pdf17 = new(1, 7); // ISO 32000-1:2008
    public static readonly PdfVersion Pdf20 = new(2, 0); // ISO 32000-2:2020

    // IComparable<PdfVersion> implementation
    public int CompareTo(PdfVersion? other)
    {
        if (other is null) return 1;

        var majorComparison = Major.CompareTo(other.Major);
        return majorComparison != 0
            ? majorComparison
            : Minor.CompareTo(other.Minor);
    }

    // IEquatable<PdfVersion> implementation
    public bool Equals(PdfVersion? other) =>
        other is not null && Major == other.Major && Minor == other.Minor;

    public override bool Equals(object? obj) =>
        obj is PdfVersion version && Equals(version);

    public override int GetHashCode() => HashCode.Combine(Major, Minor);

    // Comparison operators
    public static bool operator ==(PdfVersion? left, PdfVersion? right) =>
        left?.Equals(right) ?? right is null;

    public static bool operator !=(PdfVersion? left, PdfVersion? right) =>
        !(left == right);

    public static bool operator <(PdfVersion left, PdfVersion right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(PdfVersion left, PdfVersion right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(PdfVersion left, PdfVersion right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(PdfVersion left, PdfVersion right) =>
        left.CompareTo(right) >= 0;

    /// <summary>
    /// Checks if this version supports a specific PDF feature based on version requirements
    /// </summary>
    public bool Supports(PdfVersion minimumVersion) =>
        this >= minimumVersion;
}
