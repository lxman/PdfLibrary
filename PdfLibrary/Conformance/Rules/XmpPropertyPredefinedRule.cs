using PdfLibrary.Conformance.Xmp;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A requires every property in the XMP packet to be either defined by one of the predefined
/// XMP-2005 / ISO 19005 schemas or declared through a PDF/A extension schema (ISO 19005-2, 6.6.2.3.1).
/// A property in neither is a violation.
///
/// <para>The property set is read from the faithful XMP node tree (<see cref="ConformanceContext.XmpTree"/>)
/// and measured against the predefined schemas <em>union</em> the packet's own extension-schema
/// declarations (<see cref="ConformanceContext.XmpExtensions"/>). Structural RDF/XML namespaces and the
/// PDF/A extension-schema description namespaces (the container the standard itself defines) are never
/// reported. A property that is neither predefined nor extension-declared is flagged.</para>
/// </summary>
internal sealed class XmpPropertyPredefinedRule : IConformanceRule
{
    // Namespaces that never carry a checkable schema property: the RDF/XML plumbing, and the PDF/A
    // extension-schema description namespaces (pdfaExtension:schemas and its nested schema/property/
    // type/field nodes are the standard's own container, not stray user properties).
    private static readonly HashSet<string> StructuralNamespaces = new(StringComparer.Ordinal)
    {
        "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
        "http://www.w3.org/XML/1998/namespace",
        "adobe:ns:meta/",
        "http://www.aiim.org/pdfa/ns/extension/",
        "http://www.aiim.org/pdfa/ns/schema#",
        "http://www.aiim.org/pdfa/ns/property#",
        "http://www.aiim.org/pdfa/ns/type#",
        "http://www.aiim.org/pdfa/ns/field#",
    };

    public string RuleId => "pdfa-xmp-property-predefined";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        XmpExtensionSchemas extensions = context.XmpExtensions;

        foreach (XmpNode property in context.XmpTree)
        {
            if (string.IsNullOrEmpty(property.NamespaceUri)
                || StructuralNamespaces.Contains(property.NamespaceUri))
            {
                continue;
            }

            if (XmpPredefinedSchemas.IsPredefined(property.NamespaceUri, property.LocalName))
                continue;

            if (extensions.IsDeclared(property.NamespaceUri, property.LocalName))
                continue;

            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "6.6.2.3.1"),
                Message = $"XMP property '{property.Prefix}:{property.LocalName}' "
                          + $"(namespace {property.NamespaceUri}) is not defined by any predefined PDF/A "
                          + "schema, nor declared by a PDF/A extension schema in the packet.",
            };
        }
    }
}
