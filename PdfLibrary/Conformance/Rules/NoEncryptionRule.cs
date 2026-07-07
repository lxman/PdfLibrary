using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A and PDF/X prohibit encryption: the trailer must not contain an <c>/Encrypt</c> entry.
/// Detects the entry structurally — present and not null, whether an indirect reference or a direct
/// dictionary — independent of whether the loader could build a decryptor for it.
/// </summary>
internal sealed class NoEncryptionRule : IConformanceRule
{
    public string RuleId => "encrypt";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.All;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        PdfDictionary trailer = context.Document.Trailer.Dictionary;
        if (!trailer.TryGetValue(new PdfName("Encrypt"), out PdfObject encrypt) || encrypt is PdfNull)
            yield break;

        yield return new Finding
        {
            RuleId = RuleId,
            Severity = FindingSeverity.Error,
            Clause = ConformanceClauses.FileStructure(context.Target),
            Message = "The document is encrypted (/Encrypt is present in the trailer). "
                      + "PDF/A and PDF/X do not permit encryption.",
        };
    }
}
