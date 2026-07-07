namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A and PDF/X prohibit encryption: the trailer must not contain an <c>/Encrypt</c> entry.
/// Detects the entry structurally (presence), independent of whether a decryptor could be built.
/// </summary>
internal sealed class NoEncryptionRule : IConformanceRule
{
    public string RuleId => "encrypt";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.All;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        if (context.Document.Trailer.Encrypt is null)
            yield break;

        yield return new Finding
        {
            RuleId = RuleId,
            Severity = FindingSeverity.Error,
            Clause = ClauseFor(context.Target),
            Message = "The document is encrypted (/Encrypt is present in the trailer). "
                      + "PDF/A and PDF/X do not permit encryption.",
        };
    }

    private static string ClauseFor(ConformanceProfile profile) => profile switch
    {
        ConformanceProfile.PdfA2b or ConformanceProfile.PdfA2u => "ISO 19005-2:2011, 6.1.3",
        ConformanceProfile.PdfA3b => "ISO 19005-3:2012, 6.1.3",
        ConformanceProfile.PdfX4 => "ISO 15930-7:2010, 6.2",
        _ => "—",
    };
}
