using System;
using System.Collections.Generic;
using PdfLibrary.Conformance.Xmp;
using PdfLibrary.Metadata;
using PdfLibrary.Xmp;

namespace PdfLibrary.Conformance;

/// <summary>One property's standing under ISO 19005-2 clause 6.6.2.3.1, as the rules judge it.
/// <paramref name="ExpectedType"/> is the type that governs — predefined wins over an extension
/// declaration, exactly as <c>XmpPropertyTypeRule</c> resolves it — and is null when neither knows
/// the property, in which case <paramref name="TypeConforms"/> is true because there is no type to
/// violate (the predefined rule, not the type rule, owns that case).
///
/// <para><b>The four SHAPE facets (2026-08-12, Task 7b) describe the property's RDF shape</b>, read
/// straight off the <c>XmpNode</c> the classifier walked. They exist because that node is internal to
/// the engine and the public <see cref="XmpProperty"/> projection is flat: a struct projects as a
/// Simple with an empty value, an array of structs as blank items, and a qualified value loses its
/// qualifiers entirely. A consumer with only the projection therefore had to infer shape from "does
/// it carry text anywhere", which is not a shape test at all — it cannot distinguish a struct from a
/// genuinely blank property, and it is blind to a qualified value (which DOES carry text). Both
/// mistakes destroyed real metadata before these facets existed.</para>
///
/// <para>They are read-only descriptions of the node as classified, and they follow the same
/// merged-superset contract as every other member — see <see cref="XmpConformance"/>'s class doc
/// comment: on a multi-island packet a verdict describes the MERGED node, because that is the node
/// the next <c>Serialize()</c> will write.</para>
///
/// <para><paramref name="IsLangAlt"/> REFINES <paramref name="IsArray"/> rather than replacing it —
/// a lang alt is an <c>rdf:Alt</c> whose items all carry <c>xml:lang</c>, so both are true.
/// <paramref name="CarriesRawXml"/> is a SUBTREE question: the parser snapshots an unmodelled
/// qualified value onto the node that owns it, which for an array is the <c>rdf:li</c> ITEM, not the
/// property — so this is true when this node or ANY descendant carries a snapshot. A fixer that
/// rebuilds such a property's container drops the snapshot and the qualifiers with it.</para></summary>
public readonly record struct XmpPropertyVerdict(
    string NamespaceUri, string Prefix, string LocalName,
    bool IsPredefined, bool IsDeclaredByExtension,
    string? ExpectedType, bool TypeConforms,
    bool IsStruct = false, bool IsArray = false, bool IsLangAlt = false, bool CarriesRawXml = false);

/// <summary>One extension-schema structure defect that Pellucid deliberately does not reconstruct.
/// The missing fields named here carry namespace, type, property, or audience semantics; filling them
/// merely to satisfy clause 6.6.2.3.3 would assert author intent that is not present in the packet.</summary>
public readonly record struct XmpExtensionSchemaStructureRefusal(
    string Level, string FieldName, string Reason);

/// <summary>Preview or apply result for the deliberately narrow extension-schema structure repair.
/// <see cref="AppliedCount"/> counts detector findings closed by conventional-prefix normalization
/// or by adding an empty human-readable description. <see cref="Refused"/> contains every
/// identity/type/category field that remains absent.</summary>
public sealed record XmpExtensionSchemaStructureRepairReport(
    int AppliedCount, IReadOnlyList<XmpExtensionSchemaStructureRefusal> Refused);

/// <summary>Public read-only view of what the XMP conformance rules conclude about a packet's
/// properties. Exists so a remediation fixer can identify offending properties structurally instead
/// of regex-parsing a <c>Finding</c>'s free-text message — the same reason
/// <c>ConformanceClaim.Read</c> was factored out for the scan CLI.
///
/// <para>Mirrors <see cref="Rules.XmpPropertyPredefinedRule"/> and <see cref="Rules.XmpPropertyTypeRule"/>
/// exactly (same structural-namespace exclusion set, same predefined-beats-extension precedence, same
/// <see cref="XmpTypeContainer"/> dispatch) so the classifier cannot drift from what those rules
/// report. There is deliberately no profile parameter: <see cref="XmpPredefinedSchemas"/>'s predefined
/// table is one unconditional union with no per-profile branching, and both rules declare
/// <c>AppliesToProfiles => ConformanceProfile.AllPdfA</c> — a parameter here could not be honoured.</para>
///
/// <para><b>Multi-island packets: verdicts describe the packet as it would be SAVED, not necessarily
/// the packet as the rules see it today.</b> A packet may legally carry more than one sibling
/// <c>rdf:RDF</c> island under <c>x:xmpmeta</c> (the "DWC FX Generator" shape the official ZUGFeRD 2.5
/// examples use). <see cref="XmpPacket.Parse"/> has always merged every island into one property set —
/// that merged set is <see cref="XmpPacket"/>'s only model, and <see cref="XmpPacket.Serialize"/> always
/// writes it back as ONE island — so <see cref="ClassifyProperties"/>, which classifies exactly that
/// model via <see cref="XmpPacket.Nodes"/>, necessarily classifies every island merged together. The two
/// rules this mirrors instead read <c>ConformanceContext.XmpTree</c>, which parses the FIRST island
/// only. On a single-island packet (the overwhelming majority) the two trees are identical and verdicts
/// agree with findings exactly. On a genuine multi-island packet, this classifier's verdicts are a
/// SUPERSET of what the rules currently report: a property that lives only in island 2 is invisible to
/// today's rules but IS classified here — deliberately, because the next <c>Serialize()</c> merges it
/// into island 1, at which point the rules WOULD report it. A remediation fixer built on this classifier
/// is therefore correct to act on every verdict now, pre-empting a finding the rules have not raised yet
/// but will raise on the saved document. A caller that instead wants exactly what the rules report today,
/// island-for-island, must not use this method on a multi-island packet.</para>
/// </summary>
public static class XmpConformance
{
    private const string ExtensionNs = "http://www.aiim.org/pdfa/ns/extension/";
    private const string SchemaNs = "http://www.aiim.org/pdfa/ns/schema#";
    private const string PropertyNs = "http://www.aiim.org/pdfa/ns/property#";
    private const string TypeNs = "http://www.aiim.org/pdfa/ns/type#";
    private const string FieldNs = "http://www.aiim.org/pdfa/ns/field#";

    // Same set as XmpPropertyPredefinedRule.StructuralNamespaces, copied verbatim so the two cannot
    // drift: RDF/XML plumbing plus the PDF/A extension-schema description namespaces (the standard's
    // own container, not stray user properties).
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

    /// <summary>Classifies every top-level property of <paramref name="packet"/>'s merged node tree
    /// (see the class doc comment for the multi-island / superset contract this implies).</summary>
    public static IReadOnlyList<XmpPropertyVerdict> ClassifyProperties(XmpPacket packet)
    {
        if (packet is null) throw new ArgumentNullException(nameof(packet));

        // packet.Nodes is XmpPacket's one model — already every rdf:RDF island merged (XmpPacket.Parse
        // always parses with allRdfIslands: true). See the class doc comment: this is deliberately a
        // superset of ConformanceContext.XmpTree (first island only) on a multi-island packet.
        IReadOnlyList<XmpNode> topLevel = packet.Nodes;
        XmpExtensionSchemas extensions = XmpExtensionSchemas.Parse(topLevel);

        var verdicts = new List<XmpPropertyVerdict>();
        foreach (XmpNode node in topLevel)
        {
            // Same exclusion as XmpPropertyPredefinedRule: RDF plumbing and the extension-schema
            // description namespaces are not packet properties.
            if (string.IsNullOrEmpty(node.NamespaceUri) || IsStructural(node.NamespaceUri))
                continue;

            bool predefined = XmpPredefinedSchemas.IsPredefined(node.NamespaceUri, node.LocalName);
            bool declared = extensions.IsDeclared(node.NamespaceUri, node.LocalName);

            // Same resolution order as XmpPropertyTypeRule: predefined wins over an extension
            // declaration.
            string? type = null;
            XmpTypeContainer? container = null;
            if (XmpPredefinedSchemas.TypeOf(node.NamespaceUri, node.LocalName) is { } predefinedType)
            {
                type = predefinedType;
                container = XmpTypeContainer.Predefined23;
            }
            else if (extensions.TryGetType(node.NamespaceUri, node.LocalName, out string t, out XmpTypeContainer c))
            {
                type = t;
                container = c;
            }

            bool conforms = type is null || container!.Validate(node, type);

            // Shape facets, straight off the node this verdict is ABOUT — deliberately the same
            // `node` the classification above used, so the facets can never describe a different tree
            // from the one that produced IsPredefined/TypeConforms (the merged-superset contract in
            // the class doc comment applies to both alike). IsArrayAltText is the parser's own lang-alt
            // marker (an rdf:Alt whose items all carry xml:lang), not a re-derivation.
            verdicts.Add(new XmpPropertyVerdict(
                node.NamespaceUri, node.Prefix, node.LocalName, predefined, declared, type, conforms,
                IsStruct: node.IsStruct, IsArray: node.IsArray, IsLangAlt: node.IsArrayAltText,
                CarriesRawXml: CarriesRawXml(node)));
        }

        return verdicts;
    }

    /// <summary>Classifies the repairable and refused clause 6.6.2.3.3 branches without changing the
    /// packet. This mirrors <c>XmpExtensionSchemaStructureRule</c>'s exact walk; it does not parse finding
    /// messages and does not treat an empty value as missing.</summary>
    public static XmpExtensionSchemaStructureRepairReport PreviewExtensionSchemaStructureRepairs(
        XmpPacket packet) => RepairExtensionSchemaStructureCore(packet, apply: false);

    /// <summary>Normalizes only conventional description-namespace prefixes and adds only absent
    /// human-readable description fields (as empty strings). Namespace URIs, declared prefixes,
    /// property/type/field names, value types, and property category are never invented.</summary>
    public static XmpExtensionSchemaStructureRepairReport RepairExtensionSchemaStructure(
        XmpPacket packet) => RepairExtensionSchemaStructureCore(packet, apply: true);

    private static XmpExtensionSchemaStructureRepairReport RepairExtensionSchemaStructureCore(
        XmpPacket packet, bool apply)
    {
        if (packet is null) throw new ArgumentNullException(nameof(packet));

        var applied = 0;
        var refused = new List<XmpExtensionSchemaStructureRefusal>();
        foreach (XmpNode schemas in packet.Nodes)
        {
            if (schemas.NamespaceUri != ExtensionNs || schemas.LocalName != "schemas" || !schemas.IsArray)
                continue;

            foreach (XmpNode schema in schemas.Children)
            {
                RepairLevel(schema, "schema", SchemaNs, "pdfaSchema",
                    ["namespaceURI", "prefix", "schema"], ["schema"], schemas.RawXml is not null,
                    apply, ref applied, refused);

                if (FindField(schema, SchemaNs, "property") is { IsArray: true } propertyBag)
                    foreach (XmpNode property in propertyBag.Children)
                        RepairLevel(property, "property", PropertyNs, "pdfaProperty",
                            ["name", "valueType", "category", "description"], ["description"],
                            schemas.RawXml is not null || schema.RawXml is not null || propertyBag.RawXml is not null,
                            apply, ref applied, refused);

                if (FindField(schema, SchemaNs, "valueType") is { IsArray: true } valueTypeBag)
                    foreach (XmpNode valueType in valueTypeBag.Children)
                    {
                        bool typeBlocked = schemas.RawXml is not null || schema.RawXml is not null
                                           || valueTypeBag.RawXml is not null;
                        RepairLevel(valueType, "value type", TypeNs, "pdfaType",
                            ["type", "namespaceURI", "prefix", "description"], ["description"],
                            typeBlocked, apply, ref applied, refused);

                        if (FindField(valueType, TypeNs, "field") is { IsArray: true } fieldBag)
                            foreach (XmpNode field in fieldBag.Children)
                                RepairLevel(field, "field", FieldNs, "pdfaField",
                                    ["name", "valueType", "description"], ["description"],
                                    typeBlocked || valueType.RawXml is not null || fieldBag.RawXml is not null,
                                    apply, ref applied, refused);
                    }
            }
        }

        return new XmpExtensionSchemaStructureRepairReport(applied, refused);
    }

    private static void RepairLevel(
        XmpNode item, string level, string namespaceUri, string expectedPrefix,
        IReadOnlyList<string> requiredFields, IReadOnlyList<string> descriptiveFields,
        bool ancestorIsVerbatim, bool apply, ref int applied,
        List<XmpExtensionSchemaStructureRefusal> refused)
    {
        bool itemIsVerbatim = ancestorIsVerbatim || item.RawXml is not null;
        List<XmpNode> wrongPrefix =
            [.. item.Children.Where(child => child.NamespaceUri == namespaceUri
                                             && child.Prefix != expectedPrefix)];
        if (wrongPrefix.Count > 0)
        {
            if (itemIsVerbatim || wrongPrefix.Any(child => child.RawXml is not null))
            {
                refused.Add(new XmpExtensionSchemaStructureRefusal(
                    level, "namespace prefix",
                    "The affected RDF item is preserved verbatim, so changing its parsed prefix would not change the saved bytes."));
            }
            else
            {
                applied++;
                if (apply)
                    foreach (XmpNode child in wrongPrefix)
                        child.Prefix = expectedPrefix;
            }
        }

        foreach (string required in requiredFields)
        {
            if (HasField(item, namespaceUri, required)) continue;

            if (descriptiveFields.Contains(required) && !itemIsVerbatim)
            {
                applied++;
                if (apply)
                    item.Children.Add(new XmpNode(namespaceUri, required, expectedPrefix)
                    {
                        IsSimple = true,
                        Value = string.Empty,
                    });
                continue;
            }

            string reason = itemIsVerbatim
                ? "The affected RDF item is preserved verbatim, so a model edit would not change the saved bytes."
                : SemanticRefusalReason(level, required);
            refused.Add(new XmpExtensionSchemaStructureRefusal(level, required, reason));
        }
    }

    private static string SemanticRefusalReason(string level, string fieldName) =>
        (level, fieldName) switch
        {
            ("schema", "namespaceURI") =>
                "The missing namespace URI is the schema's identity and cannot be reconstructed safely.",
            ("schema", "prefix") =>
                "The missing declared prefix cannot be chosen without asserting the producer's namespace convention.",
            ("property", "name") =>
                "The missing property name cannot be matched safely to packet data.",
            ("property", "valueType") =>
                "The missing value type is an author declaration, not a serialization detail.",
            ("property", "category") =>
                "The missing internal/external category is an author-facing usage decision.",
            ("value type", "type") =>
                "The missing custom type name is part of the schema's type identity.",
            ("value type", "namespaceURI") =>
                "The missing custom type namespace is part of the schema's type identity.",
            ("value type", "prefix") =>
                "The missing custom type prefix cannot be chosen without asserting the producer's namespace convention.",
            ("field", "name") =>
                "The missing field name cannot be reconstructed from the remaining declaration.",
            ("field", "valueType") =>
                "The missing field value type is an author declaration, not a serialization detail.",
            _ => "The missing field carries schema semantics Pellucid will not invent.",
        };

    private static bool HasField(XmpNode node, string namespaceUri, string localName) =>
        node.Children.Any(child => child.NamespaceUri == namespaceUri && child.LocalName == localName);

    private static XmpNode? FindField(XmpNode node, string namespaceUri, string localName) =>
        node.Children.FirstOrDefault(child => child.NamespaceUri == namespaceUri && child.LocalName == localName);

    /// <summary>Whether <paramref name="node"/> or any descendant carries an unmodelled-shape snapshot.
    ///
    /// <para>It must be a SUBTREE walk, not a property-level test. <c>XmpTreeParser.SetArray</c> passes
    /// each <c>rdf:li</c> as its own capture root, so a qualified value inside an array — the
    /// <c>xmp:Identifier</c> / <c>xmpidq:Scheme</c> shape veraPDF fixture <c>6-6-2-3-1-t07-fail-j.pdf</c>
    /// carries — lands on the ITEM node while the property node's own <c>RawXml</c> stays null. A
    /// top-level-only test would report false there, and a caller trusting it would rebuild the array
    /// and destroy the qualifier, which is precisely the defect this facet exists to prevent.</para></summary>
    private static bool CarriesRawXml(XmpNode node)
    {
        if (node.RawXml is not null) return true;
        foreach (XmpNode child in node.Children)
            if (CarriesRawXml(child)) return true;
        return false;
    }

    private static bool IsStructural(string namespaceUri) => StructuralNamespaces.Contains(namespaceUri);

    /// <summary>The predefined property that supersedes a pre-2005 spelling, or null when the property
    /// has no standard equivalent and must be declared by an extension schema instead.
    /// <para>Three targets are not scalar — <c>dc:title</c> and <c>dc:description</c> are <c>lang
    /// alt</c>, <c>dc:creator</c> is <c>seq propername</c> — while every legacy source is a plain
    /// string, so a caller must wrap the value to match rather than copy it verbatim. This method does
    /// not report the type; look it up via <see cref="XmpPredefinedSchemas.TypeOf"/> on the returned
    /// namespace/local name before writing the migrated value.</para></summary>
    public static XmpModernEquivalent? ModernEquivalentOf(string namespaceUri, string localName) =>
        XmpLegacyCrosswalk.TryGet(namespaceUri, localName, out (string ns, string prefix, string name) m)
            ? new XmpModernEquivalent(m.ns, m.prefix, m.name)
            : null;
}

/// <summary>One property's modern predefined replacement.</summary>
public readonly record struct XmpModernEquivalent(string NamespaceUri, string Prefix, string LocalName);
