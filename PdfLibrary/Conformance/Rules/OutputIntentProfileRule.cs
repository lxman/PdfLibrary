using ICCSharp.Profile;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// The DestOutputProfile of an output intent must be a valid ICC profile whose device class is output
/// ('prtr') or display ('mntr'), whose data colour space is RGB, CMYK or Gray, and whose version predates
/// ICC v5. Required by PDF/A (ISO 19005-2, 6.2.3, test 1) and equally by PDF/X-4 (ISO 15930-7), whose
/// output intent must carry a valid embedded profile — hence this validates the profile for all profiles.
/// </summary>
internal sealed class OutputIntentProfileRule : IConformanceRule
{
    public string RuleId => "output-intent-profile";
    public ConformanceProfile AppliesToProfiles => ConformanceProfile.All;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        foreach (OutputIntentInfo intent in context.OutputIntents)
        {
            if (intent.Profile is null)
                continue;

            int? objectNumber = intent.ProfileRef?.ObjectNumber;

            IccProfile? profile = null;
            // Malformed or undecodable profile data is treated as "not a valid ICC profile".
            try { profile = IccProfile.Parse(intent.Profile.GetDecodedData(context.Document.Decryptor)); }
            catch (Exception) { /* handled below */ }

            if (profile is null)
            {
                yield return Error(context.Target, objectNumber,
                    "The output intent /DestOutputProfile is not a valid ICC profile.");
                continue;
            }

            ProfileHeader h = profile.Header;
            bool classOk = h.Class is ProfileClass.Output or ProfileClass.Display;
            bool spaceOk = h.DataColorSpace == ColorSpaceSignatures.RGB
                           || h.DataColorSpace == ColorSpaceSignatures.CMYK
                           || h.DataColorSpace == ColorSpaceSignatures.Gray;
            bool versionOk = h.Version.Major < 5;
            if (!(classOk && spaceOk && versionOk))
            {
                yield return Error(context.Target, objectNumber,
                    $"The output intent ICC profile has an invalid header (device class {h.RawClass}, "
                    + $"colour space {h.DataColorSpace}, version {h.Version.Major}).");
            }
        }
    }

    private Finding Error(ConformanceProfile profile, int? objectNumber, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(profile, "6.2.3"),
        Message = message,
        ObjectNumber = objectNumber,
    };
}
