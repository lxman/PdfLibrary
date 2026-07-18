using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A-2/3 clause 6.1.12 — permissions dictionary + signature-reference constraints, calibrated against
/// veraPDF's PDFA-2 rules:
/// <list type="bullet">
///   <item>test 1 (<c>PDPerms</c>): the permissions dictionary (catalog <c>/Perms</c>) shall contain no
///   keys other than <c>/UR3</c> and <c>/DocMDP</c> (ISO 32000-1, 12.8.4, Table 258);</item>
///   <item>test 2 (<c>PDSigRef</c>): if <c>/Perms</c> declares <c>/DocMDP</c>, no signature reference
///   dictionary may contain <c>/DigestLocation</c>, <c>/DigestMethod</c>, or <c>/DigestValue</c>
///   (12.8.1, Table 253).</item>
/// </list>
/// </summary>
internal sealed class PermissionsRule : IConformanceRule
{
    public string RuleId => "permissions";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    private static readonly PdfName DocMDP = new("DocMDP");
    private static readonly string[] ProhibitedSigRefKeys = ["DigestLocation", "DigestMethod", "DigestValue"];

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        if (context.Resolve(context.Catalog?.Dictionary.Get("Perms")) is not PdfDictionary perms)
            yield break;

        // test 1: the permissions dictionary shall carry no keys other than /UR3 and /DocMDP.
        foreach (PdfName key in perms.Keys)
            if (key.Value is not ("UR3" or "DocMDP"))
                yield return Error(context,
                    $"The permissions dictionary contains key /{key.Value}; only /UR3 and /DocMDP are permitted.");

        // test 2: when /Perms declares /DocMDP, a signature reference dictionary must carry no Digest* key.
        if (!perms.ContainsKey(DocMDP))
            yield break;

        foreach (PdfDictionary sigRef in CollectSigRefs(context))
            foreach (string bad in ProhibitedSigRefKeys)
                if (sigRef.Get(bad) is not null)
                {
                    yield return Error(context,
                        $"A signature reference dictionary contains /{bad}, which is prohibited when the "
                        + "permissions dictionary declares /DocMDP.");
                    break; // one finding per offending signature reference
                }
    }

    /// <summary>Every signature reference dictionary in the document — the entries of any signature
    /// dictionary's /Reference array, plus any dictionary that declares /Type /SigRef. Deduplicated.</summary>
    private static IEnumerable<PdfDictionary> CollectSigRefs(ConformanceContext context)
    {
        var seen = new HashSet<int>();
        context.Document.MaterializeAllObjects();
        foreach (PdfObject obj in context.Document.Objects.Values)
        {
            PdfDictionary? dict = obj as PdfDictionary ?? (obj as PdfStream)?.Dictionary;
            if (dict is null)
                continue;

            if (context.Resolve(dict.Get("Reference")) is PdfArray references)
                foreach (PdfObject entry in references)
                    if (context.Resolve(entry) is PdfDictionary sigRef && Fresh(sigRef))
                        yield return sigRef;

            if (context.ResolveName(dict.Get("Type")) == "SigRef" && Fresh(dict))
                yield return dict;
        }

        bool Fresh(PdfDictionary d) => !d.IsIndirect || seen.Add(d.ObjectNumber);
    }

    private Finding Error(ConformanceContext context, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.1.12"),
        Message = message,
    };
}
