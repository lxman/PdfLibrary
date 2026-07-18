using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A-2/3 clause 6.1.8: after expansion of any <c>#XX</c> escapes, a name object's byte sequence shall
/// be a valid UTF-8 sequence. Calibrated against veraPDF's PDFA-2 rule (object <c>CosName</c>, test 1:
/// <c>isValidUtf8 == true</c>), which applies to every name. One finding per distinct offending name.
/// </summary>
internal sealed class NameUtf8Rule : IConformanceRule
{
    public string RuleId => "name-utf8";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        context.Document.MaterializeAllObjects();
        var reported = new HashSet<string>();
        foreach (PdfObject obj in context.Document.Objects.Values)
            foreach (PdfName name in NamesIn(obj))
                if (!IsValidUtf8(name.Value) && reported.Add(name.Value))
                    yield return new Finding
                    {
                        RuleId = RuleId,
                        Severity = FindingSeverity.Error,
                        Clause = ConformanceClauses.For(context.Target, "6.1.8"),
                        Message = "A name object is not a valid UTF-8 sequence after #-escape expansion.",
                    };
    }

    /// <summary>Every name reachable inside a directly-nested object (dict keys + values, array elements,
    /// stream dictionaries). Indirect references are not followed — their targets are separate top-level
    /// objects the caller already iterates.</summary>
    private static IEnumerable<PdfName> NamesIn(PdfObject? obj)
    {
        switch (obj)
        {
            case PdfName name:
                yield return name;
                break;
            case PdfDictionary dict:
                foreach (PdfName key in dict.Keys)
                    yield return key;
                foreach (PdfObject value in dict.Values)
                    foreach (PdfName n in NamesIn(value))
                        yield return n;
                break;
            case PdfStream stream:
                foreach (PdfName n in NamesIn(stream.Dictionary))
                    yield return n;
                break;
            case PdfArray array:
                foreach (PdfObject element in array)
                    foreach (PdfName n in NamesIn(element))
                        yield return n;
                break;
        }
    }

    /// <summary>PdfName stores one byte per char, so the value's Latin1 bytes are the expanded name bytes.</summary>
    private static bool IsValidUtf8(string nameValue)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(nameValue);
        try
        {
            StrictUtf8.GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
