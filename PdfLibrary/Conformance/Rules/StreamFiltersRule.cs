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

            // /Filter is a single name or an array of names; iterate by position so DecodeParms stay
            // aligned, resolving each entry (array elements may be indirect references).
            int count = filterObj is PdfArray arr ? arr.Count : 1;
            for (var i = 0; i < count; i++)
            {
                PdfObject? entry = filterObj is PdfArray a ? context.Resolve(a[i]) : filterObj;
                if (entry is not PdfName filter)
                    continue; // malformed filter entry — not this rule's concern

                string name = filter.Value;
                bool ok = Allowed.Contains(name)
                          || (name == "Crypt" && IsIdentityCrypt(ParmsAt(parmsObj, i, context)));
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
            }
        }
    }

    private static PdfObject? ParmsAt(PdfObject? parmsObj, int index, ConformanceContext context) => parmsObj switch
    {
        PdfArray arr => index < arr.Count ? context.Resolve(arr[index]) : null,
        _ => index == 0 ? parmsObj : null,
    };

    // The default Crypt filter name is Identity (ISO 32000-1, 7.4.10), so absent or Name-less decode
    // parameters mean Identity; only an explicit non-Identity name is a violation.
    private static bool IsIdentityCrypt(PdfObject? parms) => parms switch
    {
        null => true,
        PdfDictionary d => d.Get("Name") is not PdfName n || n.Value == "Identity",
        _ => true,
    };
}
