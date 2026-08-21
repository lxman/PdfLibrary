using System;
using System.Collections.Generic;
using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A clause 6.2.2 (ISO 19005-2:2011 / -3:2012, calibrated against veraPDF's <c>Op_Undefined</c> rule,
/// test <c>false</c>): a content stream shall not contain any operator that is not defined in ISO 32000-1,
/// <b>even when bracketed by the BX/EX compatibility operators</b>. The engine's content parser already
/// preserves an unrecognised operator token as a <see cref="GenericOperator"/> carrying its name (and never
/// special-cases BX/EX), so an operator is undefined exactly when its name is not one of the 73 ISO 32000-1
/// operators below; inline-image binary is collapsed into a single <c>BI</c> operator, so it never leaks.
///
/// The traversal is <see cref="ContentWalk"/>'s: usage-sensitive, mirroring veraPDF, which only models
/// content it reaches. A stray operator in a Form that is present in the resources but never invoked is
/// therefore not reported, preserving the 0-false-positive invariant.
///
/// KNOWN LIMITATION: the shared content lexer recovers from a malformed run-together operator by splitting
/// it into valid operators (e.g. <c>ref</c> → <c>re</c> + <c>f</c>, <c>sc0</c> → <c>sc</c> + <c>0</c>), so
/// such a stream never surfaces an undefined token here even though veraPDF tokenises it as one
/// <c>Op_Undefined</c>. Catching that needs spec-strict content tokenisation, a lexer change with rendering
/// robustness trade-offs, tracked separately. It only ever under-reports (never a false positive).
///
/// <para>Naming caution: the corpus files named 6-2-2-t04-* are NOT operator fixtures — they are
/// veraPDF's clause 6.2.2 test NUMBER 2 (explicitly associated /Resources) fixtures, handled by
/// ExplicitResourcesRule. A corpus filename names its section, not the test number that fires;
/// read verapdf-verdicts.json.</para>
/// </summary>
internal sealed class ContentStreamOperatorRule : IConformanceRule
{
    public string RuleId => "content-stream-operator";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    // The content-stream operators defined by ISO 32000-1:2008 (Annex A). Matches veraPDF's Operators set.
    private static readonly HashSet<string> Defined = new(StringComparer.Ordinal)
    {
        "b", "B", "b*", "B*", "BDC", "BI", "BMC", "BT", "BX", "c", "cm", "cs", "CS", "d", "d0", "d1", "Do", "DP",
        "EI", "EMC", "ET", "EX", "f", "F", "f*", "g", "G", "gs", "h", "i", "ID", "j", "J", "k", "K", "l", "m",
        "M", "MP", "n", "q", "Q", "re", "rg", "RG", "ri", "s", "S", "sc", "SC", "scn", "SCN", "sh", "T*", "Tc",
        "Td", "TD", "Tf", "Tj", "TJ", "TL", "Tm", "Tr", "Ts", "Tw", "Tz", "v", "w", "W", "W*", "y", "'", "\"",
    };

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal); // one finding per distinct undefined operator
        var findings = new List<Finding>();

        foreach (PdfOperator op in ContentWalk.ReachableOperators(context))
            if (!Defined.Contains(op.Name) && reported.Add(op.Name))
                findings.Add(Error(context, op.Name));

        return findings;
    }

    private Finding Error(ConformanceContext context, string operatorName) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.2.2"),
        Message = $"A content stream contains the operator '{operatorName}', which is not defined in "
                  + "ISO 32000-1 (PDF/A permits only standard operators, even inside BX/EX).",
    };
}
