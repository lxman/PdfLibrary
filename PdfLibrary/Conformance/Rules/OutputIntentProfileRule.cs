using ICCSharp.Profile;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// The DestOutputProfile of a PDF/A output intent must be a valid ICC profile whose device class is
/// output ('prtr') or display ('mntr'), whose data colour space is RGB, CMYK or Gray, and whose
/// version predates ICC v5 (ISO 19005-2, 6.2.3, test 1).
/// </summary>
internal sealed class OutputIntentProfileRule : IConformanceRule
{
    public string RuleId => "output-intent-profile";
    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        foreach (OutputIntentInfo intent in context.OutputIntents)
        {
            if (intent.Profile is null)
                continue;

            IccProfile? profile = null;
            try { profile = IccProfile.Parse(intent.Profile.GetDecodedData(context.Document.Decryptor)); }
            catch { /* handled below */ }

            if (profile is null)
            {
                yield return Error(context.Target,
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
                yield return Error(context.Target,
                    $"The output intent ICC profile has an invalid header (device class {h.RawClass}, "
                    + $"colour space {h.DataColorSpace}, version {h.Version.Major}).");
            }
        }
    }

    private Finding Error(ConformanceProfile profile, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(profile, "6.2.3"),
        Message = message,
    };
}
