using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 reference XObjects (ISO 14289-1:2014, 7.20): a conforming file shall not contain any reference
/// XObjects — a Form XObject (a stream whose <c>/Subtype</c> is <c>Form</c>) that carries a <c>/Ref</c> key,
/// which imports content from an external PDF and so cannot participate in this document's logical structure.
/// Calibrated against veraPDF's PDF_UA-1 rule for clause 7.20 (<c>PDXForm</c>, test 1: <c>containsRef == false</c>).
/// One finding per offending XObject.
/// </summary>
internal sealed class UaReferenceXObjectRule : IConformanceRule
{
    public string RuleId => "ua-reference-xobject";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        foreach (PdfStream stream in context.Streams)
        {
            if (context.ResolveName(stream.Dictionary.Get("Subtype")) != "Form")
                continue;
            if (stream.Dictionary.Get("Ref") is null)
                continue;

            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "7.20"),
                Message = "The document contains a reference XObject (/Ref in a Form XObject), "
                          + "which is prohibited in PDF/UA-1.",
                ObjectNumber = stream.IsIndirect ? stream.ObjectNumber : null,
            };
        }
    }
}
