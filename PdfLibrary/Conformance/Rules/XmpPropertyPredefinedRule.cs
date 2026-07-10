using System.Text;
using PdfLibrary.Conformance.Xmp;
using PdfLibrary.Metadata;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A requires every property in the XMP packet to be either defined by one of the predefined
/// XMP-2005 / ISO 19005 schemas or declared through a PDF/A extension schema (ISO 19005-2, 6.6.2.3.1).
/// A property in neither is a violation.
///
/// <para>Extension schemas are nested XMP structures the (lossy) packet parser cannot represent, so
/// this rule cannot resolve extension-declared properties. To stay strictly on the safe side of the
/// no-false-positive invariant, it disengages entirely for any packet that declares an extension
/// schema (detected by the presence of the extension namespace URI in the raw metadata bytes) and
/// only reports non-predefined properties when the packet declares no extension schema at all.</para>
/// </summary>
internal sealed class XmpPropertyPredefinedRule : IConformanceRule
{
    private const string ExtensionSchemaNs = "http://www.aiim.org/pdfa/ns/extension/";

    // Structural XMP/RDF namespaces that never carry schema properties — the packet parser can surface
    // rdf/xml/x nodes that the reference does not treat as XMP properties, so never report them.
    private static readonly HashSet<string> StructuralNamespaces = new(StringComparer.Ordinal)
    {
        "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
        "http://www.w3.org/XML/1998/namespace",
        "adobe:ns:meta/",
    };

    public string RuleId => "pdfa-xmp-property-predefined";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        XmpPacket? packet = context.Xmp;
        if (packet is null)
            yield break; // MetadataPresentRule already reports a missing /Metadata stream.

        // Any extension-schema declaration means custom properties may be legitimately defined; the
        // parser cannot resolve them, so disengage rather than risk a false positive.
        if (DeclaresExtensionSchema(context.XmpMetadataBytes))
            yield break;

        foreach (XmpProperty property in packet.Properties)
        {
            if (StructuralNamespaces.Contains(property.NamespaceUri))
                continue;

            if (XmpPredefinedSchemas.IsPredefined(property.NamespaceUri, property.LocalName))
                continue;

            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "6.6.2.3.1"),
                Message = $"XMP property '{property.Prefix}:{property.LocalName}' "
                          + $"(namespace {property.NamespaceUri}) is not defined by any predefined PDF/A "
                          + "schema, and the packet declares no PDF/A extension schema that could define it.",
            };
        }
    }

    // The extension namespace URI is pure ASCII; a byte scan of the decoded packet is a reliable
    // signal independent of how the (struct-valued) extension schema is serialized.
    private static bool DeclaresExtensionSchema(byte[]? metadataBytes)
    {
        if (metadataBytes is null || metadataBytes.Length == 0)
            return false;
        string text = Encoding.Latin1.GetString(metadataBytes);
        return text.Contains(ExtensionSchemaNs, StringComparison.Ordinal);
    }
}
