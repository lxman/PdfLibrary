using PdfLibrary.Conformance.Xmp;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A requires every predefined or extension-declared XMP property to carry the value type its schema
/// defines (ISO 19005-2, 6.6.2.3.1). This validates the property's whole value against that type via the
/// ported reference validators (<see cref="XmpTypeContainer"/>): simple-type regexes, array (bag/seq/alt)
/// shape and element types, lang-alt / uri / url, ISO-8601 <c>date</c> content, and structured value
/// types (including arrays of structs) with per-field name/namespace/type checking.
///
/// <para>The type is taken from the predefined schemas when the property is predefined, otherwise from
/// the packet's extension schema that declares it (using that schema's extended validator container so
/// custom value types resolve). A property that is neither predefined nor declared is not checked here —
/// its membership is <see cref="XmpPropertyPredefinedRule"/>'s concern. Because the check runs the same
/// algorithm as the reference over a faithful tree, a value it rejects is one the reference also
/// rejects.</para>
/// </summary>
internal sealed class XmpPropertyTypeRule : IConformanceRule
{
    public string RuleId => "pdfa-xmp-property-type";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        XmpExtensionSchemas extensions = context.XmpExtensions;

        foreach (XmpNode property in context.XmpTree)
        {
            string type;
            XmpTypeContainer container;

            if (XmpPredefinedSchemas.TypeOf(property.NamespaceUri, property.LocalName) is { } predefinedType)
            {
                type = predefinedType;
                container = XmpTypeContainer.Predefined23;
            }
            else if (extensions.TryGetType(property.NamespaceUri, property.LocalName, out string t, out XmpTypeContainer c))
            {
                type = t;
                container = c;
            }
            else
            {
                continue; // not predefined or declared — the predefined-membership rule's concern
            }

            if (!container.Validate(property, type))
            {
                yield return new Finding
                {
                    RuleId = RuleId,
                    Severity = FindingSeverity.Error,
                    Clause = ConformanceClauses.For(context.Target, "6.6.2.3.1"),
                    Message = $"XMP property '{property.Prefix}:{property.LocalName}' does not conform to "
                              + $"its schema value type '{type}'.",
                };
            }
        }
    }
}
