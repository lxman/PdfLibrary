using System.Text.RegularExpressions;
using PdfLibrary.Conformance.Xmp;
using PdfLibrary.Metadata;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A requires every predefined XMP property to carry the value type its schema defines
/// (ISO 19005-2, 6.6.2.3.1). This rule checks the two facets it can verify soundly from the (lossy)
/// packet parser without any risk of a false positive:
///
/// <list type="bullet">
///   <item><b>Value shape.</b> Every predefined non-struct type constrains the RDF shape: a scalar
///   simple type must be a simple value; a <c>lang alt</c> (or <c>alt …</c>) type must be an
///   alternatives array; a <c>bag …</c> must be an unordered array and a <c>seq …</c> an ordered one.
///   A conformant file always satisfies this, so a shape that does not match is a violation the
///   reference also reports.</item>
///   <item><b>Restrictive scalar values.</b> When a <c>boolean/integer/real/mimetype</c> value (or the
///   element of an array of one — in practice <c>seq integer</c>) is present in the right shape, it must
///   match the type's regex.</item>
/// </list>
///
/// <para>Structured types and arrays-of-struct are skipped entirely: the parser flattens them to text,
/// so neither their shape nor their contents can be judged. Unconstrained scalar values (date — an
/// ISO-8601 parse in the reference, not a regex; uri/url; gpscoordinate — a part-dependent regex; and
/// the "matches anything" types text/propername/agentname/rational/renditionclass/locale) have their
/// shape checked but not their content. Every omission only ever under-reports; none can add a false
/// positive.</para>
/// </summary>
internal sealed class XmpPropertyTypeRule : IConformanceRule
{
    private enum Shape { Skip, Simple, LangAlt, Bag, Seq }

    // Scalar simple types: the reference validates each with a validator that requires isSimple().
    private static readonly HashSet<string> ScalarSimpleTypes = new(StringComparer.Ordinal)
    {
        "boolean", "integer", "real", "mimetype", "text", "propername", "agentname",
        "rational", "renditionclass", "locale", "date", "uri", "url", "gpscoordinate",
    };

    public string RuleId => "pdfa-xmp-property-type";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        XmpPacket? packet = context.Xmp;
        if (packet is null)
            yield break;

        foreach (XmpProperty property in packet.Properties)
        {
            string? type = XmpPredefinedSchemas.TypeOf(property.NamespaceUri, property.LocalName);
            if (type is null)
                continue; // Not predefined — membership is the predefined rule's concern.

            (Shape shape, string? element) = Classify(type);

            switch (shape)
            {
                case Shape.Simple:
                    if (property.Kind != XmpValueKind.Simple)
                        yield return ShapeError(context, property, type, "a simple value");
                    else if (XmpPredefinedSchemas.RestrictiveMatcher(type) is { } m &&
                             !XmpPredefinedSchemas.ValueMatches(m, property.Value))
                        yield return ValueError(context, property, type, property.Value);
                    break;

                case Shape.LangAlt:
                    // Our parser reports both rdf:Alt (lang-alt) and "alt …" arrays as LangAlt.
                    if (property.Kind != XmpValueKind.LangAlt)
                        yield return ShapeError(context, property, type, "an alternatives array");
                    break;

                case Shape.Bag:
                    if (property.Kind != XmpValueKind.Array || property.Ordered)
                        yield return ShapeError(context, property, type, "an unordered array");
                    else if (ArrayElementMatcher(element) is { } bm)
                        foreach (Finding f in CheckItems(context, property, type, bm)) yield return f;
                    break;

                case Shape.Seq:
                    if (property.Kind != XmpValueKind.Array || !property.Ordered)
                        yield return ShapeError(context, property, type, "an ordered array");
                    else if (ArrayElementMatcher(element) is { } sm)
                        foreach (Finding f in CheckItems(context, property, type, sm)) yield return f;
                    break;
            }
        }
    }

    private IEnumerable<Finding> CheckItems(ConformanceContext context, XmpProperty property, string type, Regex matcher)
    {
        foreach (string item in property.Items)
            if (!XmpPredefinedSchemas.ValueMatches(matcher, item))
            {
                yield return ValueError(context, property, type, item);
                yield break; // One bad element is enough.
            }
    }

    // Maps a predefined type string to the value shape the reference requires. Single-token struct
    // types (and "any") fall through to Skip — the parser cannot represent them faithfully.
    private static (Shape, string? Element) Classify(string type)
    {
        if (type == "lang alt")
            return (Shape.LangAlt, null);
        if (StartsWith(type, "alt ", out _))
            return (Shape.LangAlt, null); // "alt …" arrays surface as LangAlt in the parser.
        if (StartsWith(type, "bag ", out string? bagEl))
            return (Shape.Bag, bagEl);
        if (StartsWith(type, "seq ", out string? seqEl))
            return (Shape.Seq, seqEl);
        if (ScalarSimpleTypes.Contains(type))
            return (Shape.Simple, type);
        return (Shape.Skip, null);
    }

    private static bool StartsWith(string type, string prefix, out string? element)
    {
        if (type.StartsWith(prefix, StringComparison.Ordinal))
        {
            element = type[prefix.Length..];
            return true;
        }
        element = null;
        return false;
    }

    // The matcher of an array element type when it is restrictive (in practice only "integer"); else null.
    private static Regex? ArrayElementMatcher(string? element) =>
        element is null ? null : XmpPredefinedSchemas.RestrictiveMatcher(element);

    private Finding ShapeError(ConformanceContext context, XmpProperty property, string type, string expectedShape) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.6.2.3.1"),
        Message = $"XMP property '{property.Prefix}:{property.LocalName}' is not {expectedShape} as its "
                  + $"predefined schema type '{type}' requires.",
    };

    private Finding ValueError(ConformanceContext context, XmpProperty property, string type, string? value) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.6.2.3.1"),
        Message = $"XMP property '{property.Prefix}:{property.LocalName}' has value '{value}', "
                  + $"which is not a valid '{type}' value as its predefined schema requires.",
    };
}
