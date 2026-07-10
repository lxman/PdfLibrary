namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/X-4 colour governance (ISO 15930-7:2010, 6.2.2): every device-dependent colour used in the file
/// must be resolved by the file's output intent. DeviceCMYK requires a CMYK output intent, DeviceRGB an
/// RGB one, and DeviceGray any output intent. PDF/X-4 mandates exactly one GTS_PDFX output intent
/// (<see cref="PdfxOutputIntentRule"/>) whose destination profile is, for print, almost always CMYK — so
/// uncalibrated DeviceRGB is the usual violation. Detection reuses the same content scan as the PDF/A
/// device-colour rule (<see cref="DeviceColourAnalysis"/>): ICCBased/CalRGB/CalGray/Lab colour is
/// device-independent and never flagged, and the scan's documented deferrals mean the rule can only
/// under-report, never raise a false positive.
/// </summary>
internal sealed class PdfxColourRule : IConformanceRule
{
    public string RuleId => "pdfx-device-colour";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfX4;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        DeviceColourAnalysis.Usage usage = DeviceColourAnalysis.Scan(context);
        OutputIntentColour intent = context.OutputIntentColourFamily;

        if (usage.Rgb && intent != OutputIntentColour.Rgb)
            yield return Error(context,
                "DeviceRGB colour is used, but the PDF/X-4 output intent has no RGB destination profile.");
        if (usage.Cmyk && intent != OutputIntentColour.Cmyk)
            yield return Error(context,
                "DeviceCMYK colour is used, but the PDF/X-4 output intent has no CMYK destination profile.");
        if (usage.Gray && intent == OutputIntentColour.None)
            yield return Error(context,
                "DeviceGray colour is used, but the file has no PDF/X-4 output intent.");
    }

    private Finding Error(ConformanceContext context, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.2.2"),
        Message = message,
    };
}
