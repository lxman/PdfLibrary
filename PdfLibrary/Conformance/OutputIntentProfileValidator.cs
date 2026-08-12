using ICCSharp.Profile;

namespace PdfLibrary.Conformance;

/// <summary>
/// Validates that an output intent's <c>/DestOutputProfile</c> bytes are a usable ICC profile: parseable,
/// device class output ('prtr') or display ('mntr'), data colour space RGB/CMYK/Gray, and predating ICC v5.
/// The single source of truth for these checks — <c>Rules.OutputIntentProfileRule</c> delegates here so the
/// conformance rule and any editing/remediation caller (<see cref="Editing.PdfDocumentEditor"/>) can never
/// drift on what counts as a valid profile.
/// </summary>
public static class OutputIntentProfileValidator
{
    /// <summary>Returns null when <paramref name="iccBytes"/> is a valid output-intent ICC profile,
    /// otherwise the human-readable reason it is not.</summary>
    public static string? Validate(byte[] iccBytes)
    {
        ArgumentNullException.ThrowIfNull(iccBytes);

        IccProfile? profile = null;
        // Malformed or undecodable profile data is treated as "not a valid ICC profile".
        try { profile = IccProfile.Parse(iccBytes); }
        catch (Exception) { /* handled below */ }

        if (profile is null)
            return "The output intent /DestOutputProfile is not a valid ICC profile.";

        ProfileHeader h = profile.Header;
        bool classOk = h.Class is ProfileClass.Output or ProfileClass.Display;
        bool spaceOk = h.DataColorSpace == ColorSpaceSignatures.RGB
                       || h.DataColorSpace == ColorSpaceSignatures.CMYK
                       || h.DataColorSpace == ColorSpaceSignatures.Gray;
        bool versionOk = h.Version.Major < 5;
        if (classOk && spaceOk && versionOk)
            return null;

        return $"The output intent ICC profile has an invalid header (device class {h.RawClass}, "
               + $"colour space {h.DataColorSpace}, version {h.Version.Major}).";
    }
}
