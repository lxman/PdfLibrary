using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 role mapping (ISO 14289-1:2014, 7.1; ISO 32000-1 14.8.4): the structure tree root's
/// <c>/RoleMap</c> must not remap a standard structure type (test 7) and must not contain a circular
/// mapping (test 6). A standard type used as a RoleMap key hides its real role from assistive technology;
/// a cycle means a custom type never resolves to a standard one. Both are document-level properties of the
/// RoleMap dictionary, checked once per file (companion to the per-element <see cref="UaStandardTypeRule"/>).
/// </summary>
internal sealed class UaRoleMapRule : IConformanceRule
{
    public string RuleId => "ua-role-map";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        if (LogicalStructure.StructTreeRootDictionary(context.Document) is not { } root)
            yield break;
        if (context.Resolve(root.Get("RoleMap")) is not PdfDictionary roleMap)
            yield break;

        // Resolve the RoleMap to a name→name graph (drop entries whose value is not a name).
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (PdfName key in roleMap.Keys)
            if (context.ResolveName(roleMap.Get(key)) is { } target)
                map[key.Value] = target;

        // Test 7: a standard structure type must not be remapped (used as a RoleMap key).
        foreach (string source in map.Keys)
        {
            if (!LogicalStructure.IsStandardType(source))
                continue;
            yield return Error(context,
                $"The standard structure type '{source}' is remapped through /RoleMap (to '{map[source]}'); "
                + "PDF/UA-1 (ISO 32000-1 14.8.4) forbids remapping standard structure types.");
        }

        // Test 6: the mapping must not contain a cycle.
        if (HasCycle(map))
            yield return Error(context,
                "The document's /RoleMap contains a circular mapping; a role mapping must terminate at a "
                + "standard structure type.");
    }

    private static bool HasCycle(Dictionary<string, string> map)
    {
        foreach (string start in map.Keys)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string current = start;
            while (map.TryGetValue(current, out string? next))
            {
                if (!seen.Add(current))
                    return true;
                current = next;
            }
        }
        return false;
    }

    private Finding Error(ConformanceContext context, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "7.1"),
        Message = message,
    };
}
