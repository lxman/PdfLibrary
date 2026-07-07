using System.Collections.Generic;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// Document requirements (ISO 19005, 6.11, test 1): the document catalog must not contain a
/// /Requirements entry (which would declare processor features a reader must support).
/// </summary>
internal sealed class DocumentRequirementsRule : IConformanceRule
{
    private static readonly PdfName Requirements = new("Requirements");

    public string RuleId => "document-requirements";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        if (context.Catalog?.Dictionary.ContainsKey(Requirements) == true)
        {
            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "6.11"),
                Message = "The document catalog must not contain a /Requirements entry.",
            };
        }
    }
}
