using System;
using System.Collections.Generic;
using System.Linq;
using PdfLibrary.Xmp;

namespace PdfLibrary.Metadata;

/// <summary>
/// A mutable, round-trip-stable XMP metadata packet.
/// Parse from raw stream bytes; mutate via Set*/Remove; serialize back via <see cref="Serialize"/>.
///
/// <para>The model is the faithful <see cref="XmpNode"/> tree; <see cref="XmpProperty"/> is a
/// computed projection of it. That is what makes an edit non-destructive: a struct — an
/// <c>xmpMM:History</c> entry with its <c>stEvt:action</c>/<c>when</c>/<c>softwareAgent</c> fields,
/// say — has no representation in the flat Simple/Array/LangAlt shape, so when the flat shape WAS
/// the model, re-serializing after any setter collapsed it into one whitespace blob. Now the node
/// keeps the real data, the projection reports what it can, and <see cref="Serialize"/> emits the
/// tree.</para>
/// </summary>
public sealed class XmpPacket
{
    // Keyed by (namespaceUri, localName), in insertion order — the parsed tree IS the model now.
    private readonly Dictionary<(string ns, string local), XmpNode> _nodes =
        new(EqualityComparer<(string, string)>.Default);

    private XmpPacket() { }

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>Creates an empty packet.</summary>
    public static XmpPacket CreateEmpty() => new();

    /// <summary>
    /// Parses an XMP packet from the raw bytes of a /Metadata stream.
    /// Tolerant: if the bytes are not valid XMP, returns an empty packet rather than throwing.
    /// This also covers the degenerate case of an &lt;x:xmpmeta&gt; root with no &lt;rdf:RDF&gt;
    /// island anywhere inside it — the parser yields no properties, so the result is
    /// deterministically an empty packet, never a throw.
    /// </summary>
    public static XmpPacket Parse(byte[] xmpBytes)
    {
        var pkt = new XmpPacket();

        // allRdfIslands: a packet may carry several sibling rdf:RDF islands (the "DWC FX Generator"
        // shape used by the official ZUGFeRD 2.5 examples splits properties across two); every one
        // must be merged, which this type has always done.
        foreach (XmpNode node in XmpTreeParser.Parse(xmpBytes, allRdfIslands: true))
            pkt._nodes[(node.NamespaceUri, node.LocalName)] = node;

        return pkt;
    }

    // ── Generic access ────────────────────────────────────────────────────────

    /// <summary>Returns the property matching the given namespace URI and local name, or null.</summary>
    public XmpProperty? Get(string namespaceUri, string localName) =>
        _nodes.TryGetValue((namespaceUri, localName), out XmpNode? node) ? XmpProperty.FromNode(node) : null;

    /// <summary>All properties in the packet.</summary>
    public IEnumerable<XmpProperty> Properties => _nodes.Values.Select(XmpProperty.FromNode);

    /// <summary>The parsed top-level nodes. Internal because <see cref="XmpNode"/> is internal — the
    /// conformance classifier in PdfLibrary needs the real tree (shape facets and children), which the
    /// flat <see cref="XmpProperty"/> projection deliberately cannot express.
    ///
    /// <para><b>The returned list is a copy, but the nodes it holds are the packet's own live
    /// <see cref="XmpNode"/> instances</b> — <see cref="XmpNode.Value"/>, every shape flag, and
    /// <see cref="XmpNode.Children"/> are all publicly mutable. This accessor is read-only BY CONTRACT
    /// ONLY: nothing stops a caller from mutating a returned node and corrupting the packet's state
    /// behind its setters' backs. Treat every node reached from here as immutable.</para></summary>
    internal IReadOnlyList<XmpNode> Nodes => _nodes.Values.ToList();

    // ── Setters ───────────────────────────────────────────────────────────────

    /// <summary>Sets or replaces a simple string property.</summary>
    public void SetSimple(string namespaceUri, string prefix, string localName, string value)
    {
        if (namespaceUri is null) throw new ArgumentNullException(nameof(namespaceUri));
        if (prefix is null) throw new ArgumentNullException(nameof(prefix));
        if (localName is null) throw new ArgumentNullException(nameof(localName));
        if (value is null) throw new ArgumentNullException(nameof(value));

        _nodes[(namespaceUri, localName)] = new XmpNode(namespaceUri, localName, prefix)
        {
            IsSimple = true,
            Value = value,
        };
    }

    /// <summary>Sets or replaces an array property (Seq when <paramref name="ordered"/>=true, Bag otherwise).</summary>
    public void SetArray(string namespaceUri, string prefix, string localName,
                         IEnumerable<string> items, bool ordered)
    {
        if (namespaceUri is null) throw new ArgumentNullException(nameof(namespaceUri));
        if (prefix is null) throw new ArgumentNullException(nameof(prefix));
        if (localName is null) throw new ArgumentNullException(nameof(localName));
        if (items is null) throw new ArgumentNullException(nameof(items));

        var node = new XmpNode(namespaceUri, localName, prefix)
        {
            IsArray = true,
            IsArrayOrdered = ordered,
        };
        foreach (string item in items)
            node.Children.Add(Item(item));

        _nodes[(namespaceUri, localName)] = node;
    }

    /// <summary>Sets or merges a language alternative property.</summary>
    public void SetLangAlt(string namespaceUri, string prefix, string localName,
                            string text, string lang = "x-default")
    {
        if (namespaceUri is null) throw new ArgumentNullException(nameof(namespaceUri));
        if (prefix is null) throw new ArgumentNullException(nameof(prefix));
        if (localName is null) throw new ArgumentNullException(nameof(localName));
        if (text is null) throw new ArgumentNullException(nameof(text));

        var node = new XmpNode(namespaceUri, localName, prefix)
        {
            IsArray = true,
            IsArrayOrdered = true,
            IsArrayAlternate = true,
        };

        // Merge against the existing ITEM NODES, never against their flat projection. Reading the
        // projected lang→string map and rebuilding from it would rewrite every sibling item as a
        // plain string — an item that is a struct would come back empty, which is precisely the
        // destruction this whole facade exists to stop. Carrying the nodes over touches only the
        // one language being set.
        if (_nodes.TryGetValue((namespaceUri, localName), out XmpNode? existing) &&
            existing is { IsArray: true, IsArrayAlternate: true })
        {
            node.Children.AddRange(existing.Children);
        }

        int index = node.Children.FindIndex(c => (c.XmlLang ?? "x-default") == lang);
        XmpNode item = AltItem(lang, text);
        if (index >= 0)
            node.Children[index] = item;
        else if (lang == "x-default")
            node.Children.Insert(0, item); // x-default leads, as the string writer this replaced emitted
        else
            node.Children.Add(item);

        // A lang-alt is an alt array whose items all carry xml:lang — the same rule the parser uses.
        node.IsArrayAltText = node.Children.TrueForAll(c => c.HasXmlLang);

        _nodes[(namespaceUri, localName)] = node;
    }

    /// <summary>Sets or replaces a struct-valued property (serialized as <c>rdf:parseType="Resource"</c>).
    /// Fields carry their own namespace/prefix because a struct's fields routinely live in a
    /// different namespace from the property itself — <c>stEvt:</c> fields inside an
    /// <c>xmpMM:History</c> item, for instance.</summary>
    public void SetStruct(string namespaceUri, string prefix, string localName, IReadOnlyList<XmpField> fields)
    {
        if (namespaceUri is null) throw new ArgumentNullException(nameof(namespaceUri));
        if (prefix is null) throw new ArgumentNullException(nameof(prefix));
        if (localName is null) throw new ArgumentNullException(nameof(localName));
        if (fields is null) throw new ArgumentNullException(nameof(fields));

        var node = new XmpNode(namespaceUri, localName, prefix) { IsStruct = true };
        foreach (XmpField field in fields)
            node.Children.Add(FieldNode(field));

        _nodes[(namespaceUri, localName)] = node;
    }

    /// <summary>Sets or replaces an array-of-structs property (<c>rdf:Seq</c> when
    /// <paramref name="ordered"/>, <c>rdf:Bag</c> otherwise). Each item is one struct's fields.</summary>
    public void SetStructArray(string namespaceUri, string prefix, string localName,
                               IReadOnlyList<IReadOnlyList<XmpField>> items, bool ordered)
    {
        if (namespaceUri is null) throw new ArgumentNullException(nameof(namespaceUri));
        if (prefix is null) throw new ArgumentNullException(nameof(prefix));
        if (localName is null) throw new ArgumentNullException(nameof(localName));
        if (items is null) throw new ArgumentNullException(nameof(items));

        var node = new XmpNode(namespaceUri, localName, prefix)
        {
            IsArray = true,
            IsArrayOrdered = ordered,
        };

        foreach (IReadOnlyList<XmpField> item in items)
        {
            if (item is null) throw new ArgumentNullException(nameof(items));

            // An array item carries no qualified name of its own — rdf:li supplies it, same as
            // every other array item this facade emits (see Item(string) below).
            var element = new XmpNode(string.Empty, string.Empty, string.Empty) { IsStruct = true };
            foreach (XmpField field in item)
                element.Children.Add(FieldNode(field));
            node.Children.Add(element);
        }

        _nodes[(namespaceUri, localName)] = node;
    }

    // ── PDF/A extension schemas ───────────────────────────────────────────────

    // The three AIIM namespaces of the extension-schema container (ISO 19005-2, 6.6.2.3.2). They are
    // duplicated in PdfLibrary's XmpExtensionSchemas parser and XmpExtensionSchemaStructureRule; this
    // assembly cannot reference those types, so the URIs are repeated verbatim rather than shared.
    private const string ExtensionNs = "http://www.aiim.org/pdfa/ns/extension/";
    private const string SchemaNs = "http://www.aiim.org/pdfa/ns/schema#";
    private const string PropertyNs = "http://www.aiim.org/pdfa/ns/property#";

    /// <summary>Declares properties of <paramref name="namespaceUri"/> in the packet's
    /// <c>pdfaExtension:schemas</c> block, so PDF/A accepts properties the standard does not predefine.
    ///
    /// <para>MERGES rather than replaces: an existing schema item for the same namespace gains the new
    /// properties (an already-declared name is left alone), and other namespaces' schema items are
    /// untouched. Replacing the block wholesale would destroy a producer's own declarations. An empty
    /// <paramref name="properties"/> list declares nothing and leaves the packet untouched.</para>
    ///
    /// <para>The block is emitted with every field both consumers need — the parser's namespaceURI /
    /// property / name / valueType, and the structure rule's prefix / schema / category / description.
    /// A valueType string the type container does not recognise is silently ignored by the parser, so
    /// pass a type name the standard knows (e.g. "Text", "Date", "URI", "seq Text", "Lang Alt").</para></summary>
    /// <exception cref="ArgumentNullException">Any argument, or any member of any
    /// <see cref="XmpExtensionProperty"/>, is null.</exception>
    public void DeclareExtensionSchema(string namespaceUri, string prefix, string schemaDescription,
                                       IReadOnlyList<XmpExtensionProperty> properties)
    {
        if (namespaceUri is null) throw new ArgumentNullException(nameof(namespaceUri));
        if (prefix is null) throw new ArgumentNullException(nameof(prefix));
        if (schemaDescription is null) throw new ArgumentNullException(nameof(schemaDescription));
        if (properties is null) throw new ArgumentNullException(nameof(properties));

        // Declaring no properties is not a reason to plant a vacuous schema item in a packet that
        // carried no extension block at all.
        if (properties.Count == 0)
            return;

        XmpNode schemas = EnsureSchemasArray();
        XmpNode schema = EnsureSchemaFor(schemas, namespaceUri, prefix, schemaDescription);
        XmpNode propertyArray = EnsurePropertyArray(schema);

        foreach (XmpExtensionProperty p in properties)
        {
            if (HasPropertyNamed(propertyArray, PropertyMember(p.Name, nameof(XmpExtensionProperty.Name))))
                continue;
            propertyArray.Children.Add(PropertyItem(p));
        }
    }

    /// <summary>Removes a property. No-op if absent.</summary>
    public void Remove(string namespaceUri, string localName) =>
        _nodes.Remove((namespaceUri, localName));

    // ── Serialize ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Serializes the packet to a full xpacket-wrapped, UTF-8-encoded byte array with ~2 KB of
    /// padding and a trailing <c>&lt;?xpacket end="w"?&gt;</c> instruction.
    ///
    /// <para>Never fails because of a property VALUE. A value carrying a character XML 1.0 forbids
    /// (a NUL or other stray control character) or an unpaired surrogate is sanitized to U+FFFD on
    /// the way out, so a document whose existing packet contains such garbage — or a caller passing
    /// it — still saves, and the emitted packet still re-parses. This is deliberate: the setters on
    /// <c>PdfMetadata</c> re-serialize the packet on every assignment, and a property setter must
    /// not throw over document data.</para>
    ///
    /// <para>A property NAME is held to the stricter standard, because an illegal one can only come
    /// from the caller and cannot be repaired into anything meaningful.</para>
    /// </summary>
    /// <exception cref="ArgumentException">A property or struct-field local name in the packet is
    /// not a legal XML name. Namespace prefixes are exempt — an illegal or colliding prefix is
    /// replaced with a synthesized one rather than throwing.</exception>
    public byte[] Serialize() => XmpTreeSerializer.Serialize(_nodes.Values.ToList());

    // ── Internals ─────────────────────────────────────────────────────────────

    // An array item carries no qualified name of its own — rdf:li supplies it.
    private static XmpNode Item(string value) =>
        new(string.Empty, string.Empty, string.Empty) { IsSimple = true, Value = value };

    private static XmpNode AltItem(string lang, string text)
    {
        XmpNode item = Item(text);
        item.HasXmlLang = true;
        item.XmlLang = lang;
        return item;
    }

    private static XmpNode FieldNode(XmpField field)
    {
        if (field.NamespaceUri is null)
            throw new ArgumentNullException(nameof(field), $"{nameof(XmpField.NamespaceUri)} must not be null.");
        if (field.Prefix is null)
            throw new ArgumentNullException(nameof(field), $"{nameof(XmpField.Prefix)} must not be null.");
        if (field.LocalName is null)
            throw new ArgumentNullException(nameof(field), $"{nameof(XmpField.LocalName)} must not be null.");
        if (field.Value is null)
            throw new ArgumentNullException(nameof(field), $"{nameof(XmpField.Value)} must not be null.");

        return new XmpNode(field.NamespaceUri, field.LocalName, field.Prefix) { IsSimple = true, Value = field.Value };
    }

    // ── Extension-schema node building ─────────────────────────────────────────
    //
    // The container nests array → struct → array → struct, which SetStructArray (two levels) cannot
    // express, so the nodes are built directly. Every node in the SchemaNs/PropertyNs namespaces
    // carries the conventional prefix because XmpExtensionSchemaStructureRule reports any other
    // prefix on those namespaces as non-conformant.

    /// <summary>The packet's <c>pdfaExtension:schemas</c> array, created on first use. An existing
    /// array is REUSED so a producer's own declarations survive.</summary>
    private XmpNode EnsureSchemasArray()
    {
        if (_nodes.TryGetValue((ExtensionNs, "schemas"), out XmpNode? existing) && existing.IsArray)
            return existing;

        var schemas = new XmpNode(ExtensionNs, "schemas", "pdfaExtension")
        {
            IsArray = true,
            IsArrayOrdered = false,
        };
        _nodes[(ExtensionNs, "schemas")] = schemas;
        return schemas;
    }

    /// <summary>The schema item describing <paramref name="namespaceUri"/>, created and appended when
    /// absent. An existing item is reused; any of the three required description fields it happens to
    /// LACK is added (never overwritten — a producer's own wording is its own).</summary>
    private static XmpNode EnsureSchemaFor(XmpNode schemas, string namespaceUri, string prefix,
                                           string schemaDescription)
    {
        foreach (XmpNode item in schemas.Children)
        {
            if (!item.IsStruct)
                continue;
            if (!HasFieldValue(item, SchemaNs, "namespaceURI", namespaceUri))
                continue;

            AddMissingSchemaField(item, "prefix", prefix);
            AddMissingSchemaField(item, "schema", schemaDescription);
            return item;
        }

        // An array item carries no qualified name of its own — rdf:li supplies it.
        var schema = new XmpNode(string.Empty, string.Empty, string.Empty) { IsStruct = true };
        schema.Children.Add(SchemaField("namespaceURI", namespaceUri));
        schema.Children.Add(SchemaField("prefix", prefix));
        schema.Children.Add(SchemaField("schema", schemaDescription));
        schemas.Children.Add(schema);
        return schema;
    }

    /// <summary>The schema item's <c>pdfaSchema:property</c> array, created when absent.</summary>
    private static XmpNode EnsurePropertyArray(XmpNode schema)
    {
        foreach (XmpNode child in schema.Children)
            if (child.NamespaceUri == SchemaNs && child.LocalName == "property" && child.IsArray)
                return child;

        var array = new XmpNode(SchemaNs, "property", "pdfaSchema") { IsArray = true };
        schema.Children.Add(array);
        return array;
    }

    private static bool HasPropertyNamed(XmpNode propertyArray, string name)
    {
        foreach (XmpNode item in propertyArray.Children)
            if (HasFieldValue(item, PropertyNs, "name", name))
                return true;
        return false;
    }

    private static XmpNode PropertyItem(XmpExtensionProperty property)
    {
        var item = new XmpNode(string.Empty, string.Empty, string.Empty) { IsStruct = true };
        item.Children.Add(PropertyField("name",
            PropertyMember(property.Name, nameof(XmpExtensionProperty.Name))));
        item.Children.Add(PropertyField("valueType",
            PropertyMember(property.ValueType, nameof(XmpExtensionProperty.ValueType))));
        item.Children.Add(PropertyField("category",
            PropertyMember(property.Category, nameof(XmpExtensionProperty.Category))));
        item.Children.Add(PropertyField("description",
            PropertyMember(property.Description, nameof(XmpExtensionProperty.Description))));
        return item;
    }

    private static XmpNode SchemaField(string localName, string value) =>
        new(SchemaNs, localName, "pdfaSchema") { IsSimple = true, Value = value };

    private static XmpNode PropertyField(string localName, string value) =>
        new(PropertyNs, localName, "pdfaProperty") { IsSimple = true, Value = value };

    private static void AddMissingSchemaField(XmpNode schema, string localName, string value)
    {
        foreach (XmpNode child in schema.Children)
            if (child.NamespaceUri == SchemaNs && child.LocalName == localName)
                return;
        schema.Children.Add(SchemaField(localName, value));
    }

    private static bool HasFieldValue(XmpNode node, string ns, string localName, string value)
    {
        foreach (XmpNode child in node.Children)
            if (child.NamespaceUri == ns && child.LocalName == localName && child.Value == value)
                return true;
        return false;
    }

    // A default-constructed record struct has null members; they would serialize as empty fields that
    // both consumers would accept while meaning nothing, so they are rejected at the boundary.
    private static string PropertyMember(string value, string memberName) =>
        value ?? throw new ArgumentNullException(nameof(XmpExtensionProperty),
            $"{nameof(XmpExtensionProperty)}.{memberName} must not be null.");
}

/// <summary>One property of an authored PDF/A extension schema. All four members are mandatory:
/// the parser needs Name and ValueType to register the property, and XmpExtensionSchemaStructureRule
/// requires Category and Description to be present — emitting less would close one rule and open
/// another. Category is conventionally "internal" (producer-private) or "external".</summary>
public readonly record struct XmpExtensionProperty(
    string Name, string ValueType, string Category, string Description);

/// <summary>One field of an authored XMP struct. Fields carry their own namespace/prefix because a
/// struct's fields routinely live in a different namespace from the property that holds them — an
/// <c>xmpMM:History</c> item's fields are all <c>stEvt:</c>, for example.</summary>
public readonly record struct XmpField(string NamespaceUri, string Prefix, string LocalName, string Value);
