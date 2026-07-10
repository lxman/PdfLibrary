namespace PdfLibrary.Conformance.Xmp;

/// <summary>
/// Parses the PDF/A extension-schema declarations (<c>pdfaExtension:schemas</c>) from the XMP node tree
/// into the set of custom (namespace, property) → value-type definitions a packet is allowed to use,
/// plus each schema's own value-type (struct) definitions. This lets the clause 6.6.2.3.1 rules resolve
/// extension-declared properties instead of standing down whenever an extension schema is present.
///
/// <para>It mirrors the reference builder (ISO 19005-2, 6.6.2.3.2): schemas live under
/// <c>…/ns/schema#</c> (namespaceURI + property array + optional valueType array), properties under
/// <c>…/ns/property#</c> (name + valueType), custom value types under <c>…/ns/type#</c> (type +
/// namespaceURI + field array), and fields under <c>…/ns/field#</c> (name + valueType). A property whose
/// valueType is missing or not a known type is <em>not</em> registered — so it stays undefined and is
/// reported, exactly as the reference does.</para>
/// </summary>
internal sealed class XmpExtensionSchemas
{
    private const string ExtensionNs = "http://www.aiim.org/pdfa/ns/extension/";
    private const string SchemaNs = "http://www.aiim.org/pdfa/ns/schema#";
    private const string PropertyNs = "http://www.aiim.org/pdfa/ns/property#";
    private const string TypeNs = "http://www.aiim.org/pdfa/ns/type#";
    private const string FieldNs = "http://www.aiim.org/pdfa/ns/field#";

    private sealed record SchemaDef(XmpTypeContainer Container, Dictionary<string, string> Properties);

    private readonly Dictionary<string, SchemaDef> _byNamespace = new(StringComparer.Ordinal);

    /// <summary>The empty set (no extension schemas declared).</summary>
    public static readonly XmpExtensionSchemas Empty = new();

    /// <summary>Parses every <c>pdfaExtension:schemas</c> declaration among the top-level properties.</summary>
    public static XmpExtensionSchemas Parse(IReadOnlyList<XmpNode> topLevel)
    {
        var result = new XmpExtensionSchemas();
        foreach (XmpNode node in topLevel)
            if (node.NamespaceUri == ExtensionNs && node.LocalName == "schemas" && node.IsArray)
                foreach (XmpNode schema in node.Children)
                    result.RegisterSchema(schema);
        return result;
    }

    /// <summary>True when an extension schema declares (<paramref name="ns"/>, <paramref name="name"/>).</summary>
    public bool IsDeclared(string ns, string name) =>
        _byNamespace.TryGetValue(ns, out SchemaDef? def) && def.Properties.ContainsKey(name);

    /// <summary>Resolves an extension-declared property to its value type and validating container.</summary>
    public bool TryGetType(string ns, string name, out string type, out XmpTypeContainer container)
    {
        if (_byNamespace.TryGetValue(ns, out SchemaDef? def) && def.Properties.TryGetValue(name, out string? t))
        {
            type = t;
            container = def.Container;
            return true;
        }
        type = string.Empty;
        container = XmpTypeContainer.Predefined23;
        return false;
    }

    private void RegisterSchema(XmpNode schema)
    {
        if (!schema.IsStruct)
            return;

        string? namespaceUri = null;
        XmpNode? propertyNode = null;
        XmpNode? valueTypeNode = null;
        foreach (XmpNode child in schema.Children)
        {
            if (child.NamespaceUri != SchemaNs)
                continue;
            switch (child.LocalName)
            {
                case "property" when child.IsArray:
                    propertyNode = child;
                    break;
                case "valueType" when child.IsArray:
                    valueTypeNode = child;
                    break;
                case "namespaceURI":
                    namespaceUri = child.Value;
                    break;
            }
        }

        if (namespaceUri is null || propertyNode is null)
            return;

        XmpTypeContainer container = XmpTypeContainer.Predefined23.Clone();
        if (valueTypeNode is not null)
            RegisterCustomTypes(valueTypeNode, container);

        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (XmpNode property in propertyNode.Children)
        {
            (string? name, string? valueType) = ReadNameAndValueType(property, PropertyNs);
            if (name is not null && valueType is not null && container.IsKnownType(valueType))
                properties[name] = valueType;
        }

        _byNamespace[namespaceUri] = new SchemaDef(container, properties);
    }

    private static void RegisterCustomTypes(XmpNode valueTypeNode, XmpTypeContainer container)
    {
        foreach (XmpNode typeNode in valueTypeNode.Children)
        {
            if (!typeNode.IsStruct)
                continue;

            string? name = null;
            string? namespaceUri = null;
            XmpNode? fieldsNode = null;
            foreach (XmpNode child in typeNode.Children)
            {
                if (child.NamespaceUri != TypeNs)
                    continue;
                switch (child.LocalName)
                {
                    case "type":
                        name = child.Value;
                        break;
                    case "namespaceURI":
                        namespaceUri = child.Value;
                        break;
                    case "field" when child.IsArray:
                        fieldsNode = child;
                        break;
                }
            }

            if (name is null)
                continue;
            if (fieldsNode is null || fieldsNode.Children.Count == 0)
            {
                container.RegisterSimpleText(name);
            }
            else if (namespaceUri is not null)
            {
                Dictionary<string, string> fields = ReadFieldMap(fieldsNode);
                if (fields.Count > 0)
                    container.RegisterStruct(name, namespaceUri, fields);
            }
        }
    }

    private static Dictionary<string, string> ReadFieldMap(XmpNode fieldsNode)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (XmpNode field in fieldsNode.Children)
        {
            (string? name, string? valueType) = ReadNameAndValueType(field, FieldNs);
            if (name is not null && valueType is not null)
                map[name] = valueType;
        }
        return map;
    }

    private static (string? Name, string? ValueType) ReadNameAndValueType(XmpNode node, string ns)
    {
        string? name = null;
        string? valueType = null;
        foreach (XmpNode child in node.Children)
        {
            if (child.NamespaceUri != ns)
                continue;
            switch (child.LocalName)
            {
                case "name":
                    name = child.Value;
                    break;
                case "valueType":
                    valueType = child.Value;
                    break;
            }
        }
        return (name, valueType);
    }
}
