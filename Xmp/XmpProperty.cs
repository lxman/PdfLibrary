using System;
using System.Collections.Generic;
using PdfLibrary.Xmp;

namespace PdfLibrary.Metadata;

/// <summary>
/// An immutable snapshot of a single XMP property.
/// </summary>
public sealed class XmpProperty
{
    /// <summary>The XML namespace URI, e.g. <c>http://purl.org/dc/elements/1.1/</c>.</summary>
    public string NamespaceUri { get; }

    /// <summary>The preferred namespace prefix, e.g. <c>dc</c>.</summary>
    public string Prefix { get; }

    /// <summary>The local element name, e.g. <c>title</c>.</summary>
    public string LocalName { get; }

    /// <summary>Value shape: Simple, Array, or LangAlt.</summary>
    public XmpValueKind Kind { get; }

    /// <summary>Non-null when <see cref="Kind"/> is <see cref="XmpValueKind.Simple"/>.</summary>
    public string? Value { get; }

    /// <summary>Non-empty when <see cref="Kind"/> is <see cref="XmpValueKind.Array"/>.</summary>
    public IReadOnlyList<string> Items { get; }

    /// <summary>
    /// When <see cref="Kind"/> is <see cref="XmpValueKind.Array"/>: <c>true</c> for <c>rdf:Seq</c>
    /// (ordered), <c>false</c> for <c>rdf:Bag</c> (unordered).
    /// </summary>
    public bool Ordered { get; }

    /// <summary>
    /// When <see cref="Kind"/> is <see cref="XmpValueKind.LangAlt"/>: map of lang → text.
    /// Always contains at least the key <c>x-default</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> LangAlt { get; }

    // ── Projection from the node tree ─────────────────────────────────────────

    /// <summary>
    /// Projects one <see cref="XmpNode"/> onto the flat Simple/Array/LangAlt shape this type
    /// exposes. The projection is lossy by construction — that is the point: a shape the flat model
    /// cannot express (a struct, or a struct-valued array item) reports what it can here while the
    /// node keeps the real data for <see cref="XmpPacket.Serialize"/> to emit faithfully.
    /// </summary>
    internal static XmpProperty FromNode(XmpNode node)
    {
        // An rdf:Alt is a language alternative: the items' xml:lang qualifiers key the map. An
        // alternate array whose items carry no xml:lang (IsArrayAltText false) is still surfaced as
        // a LangAlt under "x-default", which is how this type has always read one — dc:title in a
        // packet that omits xml:lang must keep reaching PdfMetadata.Title and UaTitleRule.
        if (node.IsArray && node.IsArrayAlternate)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (XmpNode item in node.Children)
                map[item.XmlLang ?? "x-default"] = item.Value ?? string.Empty;
            return new XmpProperty(node.NamespaceUri, node.Prefix, node.LocalName, map);
        }

        if (node.IsArray)
        {
            // A struct item has no scalar value; the legacy Items projection cannot express it, so it
            // contributes an empty string rather than a flattened blob. The node keeps the real data
            // and Serialize emits it faithfully — which is the whole point of this change.
            var items = new List<string>(node.Children.Count);
            foreach (XmpNode item in node.Children)
                items.Add(item.Value ?? string.Empty);
            return new XmpProperty(node.NamespaceUri, node.Prefix, node.LocalName, items, node.IsArrayOrdered);
        }

        // Simple, or a struct: a struct has no scalar value, so it projects to an empty simple value
        // rather than to the concatenation of its fields' text that the old flat parser produced.
        return new XmpProperty(node.NamespaceUri, node.Prefix, node.LocalName, node.Value ?? string.Empty);
    }

    // ── Simple ────────────────────────────────────────────────────────────────

    internal XmpProperty(string namespaceUri, string prefix, string localName, string value)
    {
        NamespaceUri = namespaceUri;
        Prefix       = prefix;
        LocalName    = localName;
        Kind         = XmpValueKind.Simple;
        Value        = value;
        Items        = Array.Empty<string>();
        LangAlt      = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    // ── Array (Seq / Bag) ────────────────────────────────────────────────────

    internal XmpProperty(string namespaceUri, string prefix, string localName,
                         IReadOnlyList<string> items, bool ordered)
    {
        NamespaceUri = namespaceUri;
        Prefix       = prefix;
        LocalName    = localName;
        Kind         = XmpValueKind.Array;
        Items        = items;
        Ordered      = ordered;
        LangAlt      = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    // ── LangAlt ───────────────────────────────────────────────────────────────

    internal XmpProperty(string namespaceUri, string prefix, string localName,
                         IReadOnlyDictionary<string, string> langAlt)
    {
        NamespaceUri = namespaceUri;
        Prefix       = prefix;
        LocalName    = localName;
        Kind         = XmpValueKind.LangAlt;
        Items        = Array.Empty<string>();
        LangAlt      = langAlt;
    }
}
