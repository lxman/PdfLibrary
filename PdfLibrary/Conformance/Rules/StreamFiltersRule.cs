using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A permits only the standard stream filters of ISO 32000-1 Table 6 except <c>LZWDecode</c>, and
/// allows <c>Crypt</c> only when its decode-parameters <c>Name</c> is <c>Identity</c>
/// (ISO 19005-2, 6.1.7.2, test 1). Any other filter is a violation.
/// </summary>
internal sealed class StreamFiltersRule : IConformanceRule
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "ASCIIHexDecode", "ASCII85Decode", "FlateDecode", "RunLengthDecode",
        "CCITTFaxDecode", "JBIG2Decode", "DCTDecode", "JPXDecode",
    };

    public string RuleId => "stream-filters";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.All;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        foreach (PdfStream stream in context.Streams)
        {
            PdfObject? filterObj = context.Resolve(stream.Dictionary.Get("Filter"));
            if (filterObj is null)
                continue;

            PdfObject? parmsObj = context.Resolve(stream.Dictionary.Get("DecodeParms"));

            var index = 0;
            foreach (PdfName filter in Names(filterObj))
            {
                string name = filter.Value;
                bool ok = Allowed.Contains(name)
                          || (name == "Crypt" && IsIdentityCrypt(ParmsAt(parmsObj, index, context)));
                if (!ok)
                {
                    yield return new Finding
                    {
                        RuleId = RuleId,
                        Severity = FindingSeverity.Error,
                        Clause = ConformanceClauses.For(context.Target, "6.1.7.2"),
                        Message = $"Stream uses a filter that PDF/A does not permit: /{name}.",
                        ObjectNumber = stream.IsIndirect ? stream.ObjectNumber : null,
                    };
                }
                index++;
            }
        }
    }

    private static IEnumerable<PdfName> Names(PdfObject filterObj)
    {
        switch (filterObj)
        {
            case PdfName n:
                yield return n;
                break;
            case PdfArray arr:
                foreach (PdfObject o in arr)
                    if (o is PdfName n)
                        yield return n;
                break;
        }
    }

    private static PdfObject? ParmsAt(PdfObject? parmsObj, int index, ConformanceContext context) => parmsObj switch
    {
        PdfArray arr => index < arr.Count ? context.Resolve(arr[index]) : null,
        _ => index == 0 ? parmsObj : null,
    };

    private static bool IsIdentityCrypt(PdfObject? parms) =>
        parms is PdfDictionary d && d.Get("Name") is PdfName { Value: "Identity" };
}
