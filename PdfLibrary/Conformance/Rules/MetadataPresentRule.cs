namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A and PDF/X require the document catalog to carry an embedded XMP metadata stream via its
/// <c>/Metadata</c> key (ISO 19005-2, 6.6.2.1). This rule only checks that the stream is present;
/// verifying the XMP packet's well-formedness is left to a later slice.
/// </summary>
internal sealed class MetadataPresentRule : IConformanceRule
{
    public string RuleId => "metadata";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.All;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        if (context.Document.GetCatalog()?.GetMetadata() is null)
        {
            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "6.6.2.1"),
                Message = "The document catalog has no /Metadata stream; PDF/A and PDF/X require "
                          + "embedded XMP metadata.",
            };
        }
    }
}
