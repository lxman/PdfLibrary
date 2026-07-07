namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// A device colour space may only be used if the file has a PDF/A output intent with a matching
/// destination profile (ISO 19005-2, 6.2.4.3): DeviceRGB needs an RGB output intent, DeviceCMYK a
/// CMYK one, DeviceGray any output intent. Detection covers the common paths (see DeviceColourAnalysis).
/// </summary>
internal sealed class DeviceColourRule : IConformanceRule
{
    public string RuleId => "device-colour";
    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        DeviceColourAnalysis.Usage usage = DeviceColourAnalysis.Scan(context);
        OutputIntentColour intent = context.OutputIntentColourFamily;

        if (usage.Rgb && intent != OutputIntentColour.Rgb)
            yield return Error(context.Target,
                "DeviceRGB colour is used, but the file has no output intent with an RGB destination profile.");
        if (usage.Cmyk && intent != OutputIntentColour.Cmyk)
            yield return Error(context.Target,
                "DeviceCMYK colour is used, but the file has no output intent with a CMYK destination profile.");
        if (usage.Gray && intent == OutputIntentColour.None)
            yield return Error(context.Target,
                "DeviceGray colour is used, but the file has no output intent.");
    }

    private Finding Error(ConformanceProfile profile, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(profile, "6.2.4.3"),
        Message = message,
    };
}
