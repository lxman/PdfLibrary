namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 forms (ISO 14289-1:2014, 7.15): a conforming file's interactive form must not use XFA — an XFA
/// form's content lives outside the tagged page content and structure tree, so it cannot be made accessible.
/// The presence of an <c>/XFA</c> entry in the AcroForm dictionary is the violation.
/// </summary>
internal sealed class UaXfaRule : IConformanceRule
{
    public string RuleId => "ua-xfa";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        if (context.Catalog?.GetAcroForm() is { } acroForm
            && context.Resolve(acroForm.Get("XFA")) is not null)
        {
            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "7.15"),
                Message = "The interactive form uses XFA (AcroForm /XFA), which PDF/UA does not permit.",
            };
        }
    }
}
