using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A-2/3 clause 6.2.9 — prohibited XObject constructs, all of which defeat self-contained,
/// device-independent reproduction. Calibrated against veraPDF's three PDFA-2 rules for 6.2.9:
/// <list type="bullet">
///   <item>test 1 (<c>PDXForm</c>): a form XObject dictionary shall not contain <c>/OPI</c>, <c>/PS</c>,
///   or <c>/Subtype2</c> with the value <c>PS</c>;</item>
///   <item>test 2 (<c>PDXForm</c>): a form XObject shall not carry <c>/Ref</c> (a reference XObject that
///   imports external content);</item>
///   <item>test 3 (<c>PDXObject</c>): a PostScript XObject (<c>/Subtype PS</c>) is prohibited outright.</item>
/// </list>
/// One finding per offending XObject.
/// </summary>
internal sealed class ProhibitedXObjectRule : IConformanceRule
{
    public string RuleId => "prohibited-xobject";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        foreach (PdfStream stream in context.Streams)
        {
            string? subtype = context.ResolveName(stream.Dictionary.Get("Subtype"));

            // Test 3: a PostScript XObject (/Subtype PS) is prohibited outright.
            if (subtype == "PS")
            {
                yield return Prohibited(context, stream,
                    "A PostScript XObject (/Subtype PS) is prohibited in PDF/A (ISO 19005-2, 6.2.9).");
                continue;
            }

            if (subtype != "Form")
                continue;

            // Tests 1 and 2: prohibited entries on a form XObject dictionary.
            var prohibited = new List<string>();
            if (stream.Dictionary.Get("OPI") is not null)
                prohibited.Add("/OPI");
            if (stream.Dictionary.Get("PS") is not null)
                prohibited.Add("/PS");
            if (context.ResolveName(stream.Dictionary.Get("Subtype2")) == "PS")
                prohibited.Add("/Subtype2 with value PS");
            if (stream.Dictionary.Get("Ref") is not null)
                prohibited.Add("/Ref (reference XObject)");

            if (prohibited.Count == 0)
                continue;

            yield return Prohibited(context, stream,
                "A Form XObject dictionary contains prohibited entr" + (prohibited.Count == 1 ? "y" : "ies")
                + ": " + string.Join(", ", prohibited)
                + " — PDF/A forbids /OPI, /PS, /Subtype2 = PS, and /Ref on form XObjects.");
        }
    }

    private Finding Prohibited(ConformanceContext context, PdfStream stream, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.2.9"),
        Message = message,
        ObjectNumber = stream.IsIndirect ? stream.ObjectNumber : null,
    };
}
