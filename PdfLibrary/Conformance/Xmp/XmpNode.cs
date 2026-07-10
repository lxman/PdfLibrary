using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PdfLibrary.Conformance.Xmp;

/// <summary>
/// A single node in a faithful XMP RDF value tree — the structure the clause 6.6.2.3.1 value-type
/// validators run against. Unlike the lossy <see cref="PdfLibrary.Metadata.XmpPacket"/> (which flattens
/// structs and arrays-of-struct to text), this preserves every RDF shape distinction the reference
/// validator (veraPDF) relies on: struct vs. array vs. simple, the array flavour (bag/seq/alt), the
/// alt-text (lang-alt) marker, and each child field's own namespace and name.
///
/// <para>The boolean shape flags mirror the Adobe XMP <c>PropertyOptions</c> facets the reference
/// consults — <c>isSimple / isStruct / isArray / isArrayOrdered / isArrayAlternate / isArrayAltText</c> —
/// so the ported validators can be a line-for-line translation.</para>
/// </summary>
internal sealed class XmpNode
{
    public XmpNode(string namespaceUri, string localName, string prefix)
    {
        NamespaceUri = namespaceUri;
        LocalName = localName;
        Prefix = prefix;
    }

    /// <summary>The property/field namespace URI (empty for an unqualified array item).</summary>
    public string NamespaceUri { get; }

    /// <summary>The property/field local name (empty for an array item, which has no qualified name).</summary>
    public string LocalName { get; }

    /// <summary>The namespace prefix as serialized (for diagnostics only).</summary>
    public string Prefix { get; }

    /// <summary>The scalar text value when <see cref="IsSimple"/>; otherwise null.</summary>
    public string? Value { get; set; }

    /// <summary>Struct field nodes, or array element nodes, in document order.</summary>
    public List<XmpNode> Children { get; } = [];

    // ── Shape facets (mirror Adobe XMP PropertyOptions) ─────────────────────────────────────────
    public bool IsSimple { get; set; }
    public bool IsStruct { get; set; }
    public bool IsArray { get; set; }
    public bool IsArrayOrdered { get; set; }    // rdf:Seq and rdf:Alt
    public bool IsArrayAlternate { get; set; }  // rdf:Alt
    public bool IsArrayAltText { get; set; }     // rdf:Alt whose items all carry xml:lang (a lang-alt)

    /// <summary>True when this array item carried an <c>xml:lang</c> qualifier.</summary>
    public bool HasXmlLang { get; set; }
}

/// <summary>
/// Parses the raw bytes of a /Metadata stream into a faithful XMP RDF node tree. Every top-level XMP
/// property (across all <c>rdf:Description</c> elements, in both attribute and element serialization)
/// becomes one <see cref="XmpNode"/>; struct fields and array items become descendant nodes. Tolerant:
/// any parse failure yields an empty property list (never throws), preserving the no-false-positive
/// contract — a packet that will not parse is simply not checked.
/// </summary>
internal static class XmpTreeParser
{
    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace Xml = "http://www.w3.org/XML/1998/namespace";

    /// <summary>The top-level XMP properties of a packet, or an empty list when it will not parse.</summary>
    public static IReadOnlyList<XmpNode> Parse(byte[]? metadataBytes)
    {
        var properties = new List<XmpNode>();
        if (metadataBytes is null || metadataBytes.Length == 0)
            return properties;

        try
        {
            byte[] bytes = metadataBytes;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                bytes = bytes[3..];

            XDocument doc;
            using (var reader = new StringReader(Encoding.UTF8.GetString(bytes)))
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Ignore,
                    IgnoreWhitespace = false,
                    ConformanceLevel = ConformanceLevel.Document,
                };
                using XmlReader xr = XmlReader.Create(reader, settings);
                doc = XDocument.Load(xr, LoadOptions.PreserveWhitespace);
            }

            XElement? rdf = FindRdf(doc);
            if (rdf is null)
                return properties;

            foreach (XElement desc in rdf.Elements(Rdf + "Description"))
                ReadDescription(desc, properties);
        }
        catch
        {
            // Tolerant: an unparseable packet is not checked (never a false positive).
            return properties;
        }

        return properties;
    }

    private static XElement? FindRdf(XDocument doc)
    {
        XElement? meta = doc.Root?.Name.LocalName == "xmpmeta"
            ? doc.Root
            : doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "xmpmeta");
        XElement? child = meta?.Element(Rdf + "RDF");
        if (child is not null)
            return child;
        return doc.Root?.Name == Rdf + "RDF" ? doc.Root : null;
    }

    // Every property carried by one rdf:Description — attribute-form (always simple) then element-form.
    private static void ReadDescription(XElement desc, List<XmpNode> into)
    {
        foreach (XAttribute attr in desc.Attributes())
        {
            if (IsPropertyAttribute(attr))
                into.Add(SimpleFromAttribute(desc, attr));
        }

        foreach (XElement child in desc.Elements())
        {
            if (child.Name.Namespace == Rdf)
                continue; // structural (a stray rdf:* element is not a property)
            into.Add(ParseProperty(child));
        }
    }

    // A property element → a node whose shape is determined from its serialization.
    private static XmpNode ParseProperty(XElement el)
    {
        string ns = el.Name.NamespaceName;
        var node = new XmpNode(ns, el.Name.LocalName, PrefixOf(el, el.Name.Namespace));
        DetermineValue(el, node);
        return node;
    }

    // Classifies el's value as array, struct, or simple and populates the node accordingly.
    private static void DetermineValue(XElement el, XmpNode node)
    {
        XElement? container = el.Elements().FirstOrDefault(e =>
            e.Name.Namespace == Rdf && e.Name.LocalName is "Bag" or "Seq" or "Alt");
        if (container is not null)
        {
            SetArray(node, container);
            return;
        }

        XElement? descChild = el.Element(Rdf + "Description");
        string? parseType = el.Attribute(Rdf + "parseType")?.Value;
        if (parseType == "Resource" || descChild is not null || HasStructContent(el))
        {
            XElement source = descChild ?? el;

            // A general qualified value: an rdf:value field carries the actual value and the sibling
            // fields are qualifiers. The Adobe XMP model surfaces this as the rdf:value's own kind (a
            // simple value with qualifiers is still simple), so parse the rdf:value and drop qualifiers.
            XElement? rdfValue = source.Element(Rdf + "value");
            if (rdfValue is not null)
            {
                DetermineValue(rdfValue, node);
                return;
            }
            if (source.Attribute(Rdf + "value") is { } valueAttr)
            {
                node.IsSimple = true;
                node.Value = valueAttr.Value;
                return;
            }

            SetStruct(node, source);
            return;
        }

        node.IsSimple = true;
        node.Value = SimpleText(el);
    }

    private static void SetArray(XmpNode node, XElement container)
    {
        node.IsArray = true;
        switch (container.Name.LocalName)
        {
            case "Seq":
                node.IsArrayOrdered = true;
                break;
            case "Alt":
                node.IsArrayOrdered = true;
                node.IsArrayAlternate = true;
                break;
            // Bag: unordered, non-alternate.
        }

        foreach (XElement li in container.Elements(Rdf + "li"))
        {
            var item = new XmpNode(string.Empty, string.Empty, string.Empty)
            {
                HasXmlLang = li.Attribute(Xml + "lang") is not null,
            };
            DetermineValue(li, item);
            node.Children.Add(item);
        }

        // A lang-alt is an alt array whose items all carry xml:lang (an empty alt qualifies too).
        if (node.IsArrayAlternate)
            node.IsArrayAltText = node.Children.Count == 0 || node.Children.TrueForAll(c => c.HasXmlLang);
    }

    private static void SetStruct(XmpNode node, XElement source)
    {
        node.IsStruct = true;

        foreach (XElement field in source.Elements())
        {
            if (field.Name.Namespace == Rdf)
                continue; // rdf:type qualifier and the like are not struct fields
            node.Children.Add(ParseProperty(field));
        }

        foreach (XAttribute attr in source.Attributes())
        {
            if (IsPropertyAttribute(attr))
                node.Children.Add(SimpleFromAttribute(source, attr));
        }
    }

    // True when el has field content (a non-rdf child element or a qualified property attribute).
    private static bool HasStructContent(XElement el)
    {
        foreach (XElement child in el.Elements())
            if (child.Name.Namespace != Rdf)
                return true;
        foreach (XAttribute attr in el.Attributes())
            if (IsPropertyAttribute(attr))
                return true;
        return false;
    }

    // An attribute that carries an actual XMP property/field value (not a namespace decl, not rdf:*,
    // not xml:lang, not an unqualified attribute).
    private static bool IsPropertyAttribute(XAttribute attr)
    {
        if (attr.IsNamespaceDeclaration)
            return false;
        XNamespace ns = attr.Name.Namespace;
        if (ns == Rdf || ns == Xml)
            return false;
        return !string.IsNullOrEmpty(attr.Name.NamespaceName);
    }

    private static XmpNode SimpleFromAttribute(XElement owner, XAttribute attr)
    {
        var node = new XmpNode(attr.Name.NamespaceName, attr.Name.LocalName, PrefixOf(owner, attr.Name.Namespace))
        {
            IsSimple = true,
            Value = attr.Value,
        };
        return node;
    }

    private static string SimpleText(XElement el)
    {
        string text = el.Value;
        if (string.IsNullOrEmpty(text) && el.Attribute(Rdf + "resource") is { } resource)
            return resource.Value; // rdf:resource URI-reference form
        return text;
    }

    private static string PrefixOf(XElement scope, XNamespace ns)
    {
        if (string.IsNullOrEmpty(ns.NamespaceName))
            return string.Empty;
        return scope.GetPrefixOfNamespace(ns) ?? string.Empty;
    }
}
