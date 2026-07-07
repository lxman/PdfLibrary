using System;
using System.Collections.Generic;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// Optional-content configuration requirements (ISO 19005, 6.9), applied to the default configuration
/// (/OCProperties /D) and every alternate configuration (/OCProperties /Configs):
/// <list type="bullet">
///   <item>test 1 — each configuration must have a non-empty /Name;</item>
///   <item>test 2 — configuration /Name values must be unique;</item>
///   <item>test 4 — a configuration must not contain the /AS (auto-state) key.</item>
/// </list>
/// Not implemented: 6.9-t3 (every OCG must be listed in /Order).
/// </summary>
internal sealed class OptionalContentRule : IConformanceRule
{
    private static readonly PdfName AS = new("AS");

    public string RuleId => "optional-content";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        if (context.Resolve(context.Catalog?.Dictionary.Get("OCProperties")) is not PdfDictionary ocProperties)
            yield break;

        var seenNames = new HashSet<string>();
        foreach (PdfDictionary config in Configurations(context, ocProperties))
        {
            // 6.9-t1: /Name present and non-empty.
            var name = context.Resolve(config.Get("Name")) as PdfString;
            if (name is null || name.Bytes.Length == 0)
            {
                yield return Error(context, "An optional-content configuration dictionary must have a non-empty /Name.");
            }
            // 6.9-t2: /Name values must be unique across configurations.
            else if (!seenNames.Add(Convert.ToHexString(name.Bytes)))
            {
                yield return Error(context, "Optional-content configuration /Name values must be unique, but one is duplicated.");
            }

            // 6.9-t4: no /AS key.
            if (config.ContainsKey(AS))
            {
                yield return Error(context, "An optional-content configuration dictionary must not contain the /AS key.");
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

    private Finding Error(ConformanceContext context, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.9"),
        Message = message,
    };
}
