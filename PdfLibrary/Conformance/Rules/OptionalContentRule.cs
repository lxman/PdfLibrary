using System;
using System.Collections.Generic;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// Optional-content configuration requirements, applied to the default configuration (/OCProperties /D) and
/// every alternate configuration (/OCProperties /Configs). Shared across PDF/A (ISO 19005, 6.9) and PDF/UA-1
/// (ISO 14289-1:2014, 7.10):
/// <list type="bullet">
///   <item>test 1 — each configuration must have a non-empty /Name (both PDF/A 6.9-t1 and PDF/UA 7.10-t1);</item>
///   <item>test 2 — configuration /Name values must be unique (<b>PDF/A only</b>, 6.9-t2). veraPDF's PDF/UA-1
///     7.10 has no uniqueness check, so it is profile-gated off for the UA target — a UA document with
///     repeated configuration names must not be flagged;</item>
///   <item>test 4 — a configuration must not contain the /AS (auto-state) key (PDF/A 6.9-t4 and PDF/UA 7.10-t2).</item>
/// </list>
/// The clause reference tracks the target (6.9 for PDF/A, 7.10 for PDF/UA-1). Not implemented: 6.9-t3 (every
/// OCG must be listed in /Order).
/// </summary>
internal sealed class OptionalContentRule : IConformanceRule
{
    private static readonly PdfName AS = new("AS");

    public string RuleId => "optional-content";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA | ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        if (context.Resolve(context.Catalog?.Dictionary.Get("OCProperties")) is not PdfDictionary ocProperties)
            yield break;

        // PDF/A cites 6.9; PDF/UA-1 cites 7.10 (identical checks minus the uniqueness test, gated below).
        string clause = context.Target == ConformanceProfile.PdfUA1 ? "7.10" : "6.9";

        var seenNames = new HashSet<string>();
        foreach (PdfDictionary config in Configurations(context, ocProperties))
        {
            // t1: /Name present and non-empty (all targets).
            var name = context.Resolve(config.Get("Name")) as PdfString;
            if (name is null || name.Bytes.Length == 0)
            {
                yield return Error(context, clause, "An optional-content configuration dictionary must have a non-empty /Name.");
            }
            // 6.9-t2: /Name values must be unique across configurations — PDF/A only (veraPDF UA 7.10 has no
            // uniqueness check, so repeated config names must not fire a UA false positive).
            else if (context.Target != ConformanceProfile.PdfUA1 && !seenNames.Add(Convert.ToHexString(name.Bytes)))
            {
                yield return Error(context, clause, "Optional-content configuration /Name values must be unique, but one is duplicated.");
            }

            // t4: no /AS key (all targets).
            if (config.ContainsKey(AS))
            {
                yield return Error(context, clause, "An optional-content configuration dictionary must not contain the /AS key.");
            }
        }
    }

    private static IEnumerable<PdfDictionary> Configurations(ConformanceContext context, PdfDictionary ocProperties)
    {
        if (context.Resolve(ocProperties.Get("D")) is PdfDictionary defaultConfig)
            yield return defaultConfig;

        if (context.Resolve(ocProperties.Get("Configs")) is PdfArray configs)
            foreach (PdfObject entry in configs)
                if (context.Resolve(entry) is PdfDictionary config)
                    yield return config;
    }

    private Finding Error(ConformanceContext context, string clause, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, clause),
        Message = message,
    };
}
