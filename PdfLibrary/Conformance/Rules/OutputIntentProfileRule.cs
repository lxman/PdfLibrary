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

            string? failure;
            try { failure = OutputIntentProfileValidator.Validate(intent.Profile.GetDecodedData(context.Document.Decryptor)); }
            catch (Exception)
            {
                // Malformed or undecodable profile data is treated as "not a valid ICC profile".
                failure = "The output intent /DestOutputProfile is not a valid ICC profile.";
            }

            if (failure is not null)
                yield return Error(context.Target, objectNumber, failure);
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
