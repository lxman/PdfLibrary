using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 encryption permissions (ISO 14289-1:2014, 7.16; ISO 32000-1 7.6.3.2): unlike PDF/A, PDF/UA
/// permits encryption, but an encrypted conforming file's <c>/Encrypt</c> dictionary must grant the
/// accessibility permission — its <c>/P</c> integer must have bit 10 (value 512, "extract text and
/// graphics in support of accessibility") set — so assistive technology can read the content. An
/// unencrypted file is unconstrained.
/// </summary>
internal sealed class UaEncryptionRule : IConformanceRule
{
    private const long AccessibilityPermission = 512;

    public string RuleId => "ua-encryption";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        PdfDictionary trailer = context.Document.Trailer.Dictionary;
        if (!trailer.TryGetValue(new PdfName("Encrypt"), out PdfObject encryptRef) || encryptRef is PdfNull)
            yield break; // not encrypted — clause 7.16 does not apply
        if (context.Resolve(encryptRef) is not PdfDictionary encrypt)
            yield break; // encryption dictionary unreadable — skip rather than risk a false positive

        long? p = (context.Resolve(encrypt.Get("P")) as PdfInteger)?.LongValue;
        if (p is { } value && (value & AccessibilityPermission) == AccessibilityPermission)
            yield break; // accessibility extraction is permitted — conformant

        yield return new Finding
        {
            RuleId = RuleId,
            Severity = FindingSeverity.Error,
            Clause = ConformanceClauses.For(context.Target, "7.16"),
            Message = "The encrypted document's /Encrypt /P does not grant the accessibility permission "
                      + "(bit 10, value 512); PDF/UA-1 requires an encrypted file to permit extraction of "
                      + "text and graphics in support of accessibility.",
        };
    }
}
