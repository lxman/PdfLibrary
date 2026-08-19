using System;
using System.Collections.Generic;
using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A inline-image restrictions, calibrated against veraPDF's PDFA-2 profile (1.30.2) rather than
/// against the clause text, which reads narrower than the rule actually implemented:
///
/// <list type="bullet">
/// <item><b>6.1.10 test 1</b>, object <c>CosIIFilter</c> — a WHITELIST, not an LZW blacklist: "The value
/// of the F key in the Inline Image dictionary shall not be LZW, LZWDecode, Crypt, a value not listed in
/// ISO 32000-1:2008, Table 6, or an array containing any such value." So an unknown or misspelled filter
/// name fails just as LZW does, and the check applies element-wise to a filter array.</item>
/// <item><b>6.2.8 test 3</b>, object <c>PDXImage</c>, test <c>Interpolate == false</c> — "For an inline
/// image, the I key shall have a value of false." The image-XObject arm of this clause is covered by
/// <see cref="ImageDictionaryRule"/>; this rule adds only the inline arm.</item>
/// </list>
///
/// The walk is usage-sensitive (see <see cref="ContentWalk"/>): an inline image inside a Form XObject that
/// is never invoked is not reported, matching veraPDF and preserving the zero-false-positive invariant.
///
/// FP-safety: a key whose value has the wrong type (a <c>/F</c> that is neither a name nor an array, an
/// <c>/I</c> that is not a boolean) is IGNORED rather than reported. Absent <c>/I</c> defaults to false
/// and is conformant, so only an explicit true fails.
/// </summary>
internal sealed class InlineImageRule : IConformanceRule
{
    public string RuleId => "inline-image";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    // ISO 32000-1 Table 6 as veraPDF's 6.1.10 rule enumerates it: the six filters permitted on an inline
    // image, each in both its full and its abbreviated (Table 93) spelling.
    private static readonly HashSet<string> PermittedFilters = new(StringComparer.Ordinal)
    {
        "ASCIIHexDecode", "ASCII85Decode", "FlateDecode", "RunLengthDecode", "CCITTFaxDecode", "DCTDecode",
        "AHx", "A85", "Fl", "RL", "CCF", "DCT",
    };

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        var findings = new List<Finding>();

        foreach (PdfOperator op in ContentWalk.ReachableOperators(context))
        {
            if (op is not InlineImageOperator image)
                continue;

            PdfDictionary parameters = image.Parameters;

            // 6.2.8 t3 — the /I (Interpolate) key, when present, shall be false.
            PdfObject? interpolate = context.Resolve(parameters.Get("I")) ?? context.Resolve(parameters.Get("Interpolate"));
            if (interpolate is PdfBoolean { Value: true })
                findings.Add(new Finding
                {
                    RuleId = RuleId,
                    Severity = FindingSeverity.Error,
                    Clause = ConformanceClauses.For(context.Target, "6.2.8"),
                    Message = "An inline image sets /I (Interpolate) true; PDF/A requires the value false.",
                });

            // 6.1.10 t1 — every filter named by /F shall be one of the six permitted by ISO 32000-1 Table 6.
            foreach (string filter in FilterNames(context, parameters))
                if (!PermittedFilters.Contains(filter))
                    findings.Add(new Finding
                    {
                        RuleId = RuleId,
                        Severity = FindingSeverity.Error,
                        Clause = ConformanceClauses.For(context.Target, "6.1.10"),
                        Message = $"An inline image uses the filter /{filter}, which PDF/A does not permit "
                                  + "(only ASCIIHexDecode, ASCII85Decode, FlateDecode, RunLengthDecode, "
                                  + "CCITTFaxDecode and DCTDecode, or their abbreviations, are allowed).",
                    });
        }

        return findings;
    }

    /// <summary>The filter names on /F (or its full spelling /Filter), whether written as a single name or
    /// an array. A value of any other type carries no filter information and yields nothing.</summary>
    private static IEnumerable<string> FilterNames(ConformanceContext context, PdfDictionary parameters)
    {
        PdfObject? filter = context.Resolve(parameters.Get("F")) ?? context.Resolve(parameters.Get("Filter"));

        switch (filter)
        {
            case PdfName name:
                yield return name.Value;
                break;
            case PdfArray array:
                foreach (PdfObject element in array)
                    if (context.Resolve(element) is PdfName elementName)
                        yield return elementName.Value;
                break;
        }
    }
}
