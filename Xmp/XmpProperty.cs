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

    /// <summary>When <see cref="Kind"/> is <see cref="XmpValueKind.Array"/>: <c>true</c> when the
    /// array is an <c>rdf:Alt</c> (a list of ALTERNATIVES, of which a consumer picks one) rather than
    /// an <c>rdf:Seq</c> or <c>rdf:Bag</c> (a list of VALUES, all of which belong).
    ///
    /// <para>Added 2026-08-13 with the D2 fix, which closed an ambiguity that fix itself created.
    /// Before it, an <c>rdf:Alt</c> always projected as <see cref="XmpValueKind.LangAlt"/> and only
    /// Seq/Bag reached <see cref="XmpValueKind.Array"/>, so the distinction was implicit in the Kind.
    /// Now a multi-item Alt with no languages projects as an Array too, and without this flag a
    /// consumer could no longer tell "pick one of these" from "these are all the values" — which
    /// changes meaning, not just shape. <c>UaTitleRule</c> is the live case: it must accept an
    /// alternatives list as a title and must keep rejecting a Seq of titles, exactly as it did before
    /// the projection changed.</para></summary>
    public bool IsAlternate { get; }

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
        // An rdf:Alt projects as a language alternative when it IS one — IsArrayAltText, the parser's
        // own marker that every item carries xml:lang — or when it holds at most one item.
        //
        // The single-item clause is load-bearing and must not be "simplified" away: a dc:title written
        // without xml:lang has to keep reaching PdfMetadata.Title and UaTitleRule, which is the whole
        // reason this branch ever accepted an untagged Alt. (An EMPTY Alt is already IsArrayAltText by
        // the parser's definition, so it is covered twice over.)
        //
        // What that old behaviour never intended was to let SIBLING items overwrite each other. Part 1
        // §6.3.4 defines Alt as a general-purpose alternatives array — language is one use of it, not
        // its definition — so a multi-item Alt with no languages is an ordinary array, and keying it
        // by `XmlLang ?? "x-default"` collapsed every item onto one key, last write winning. Three
        // items became one, silently.
        //
        // The DOCUMENT was never damaged by this: the serializer works from the node, so every item
        // always survived a round trip. The damage was confined to consumers of this projection — and
        // Pellucid's XmpDomain.ComparableValue reads exactly this to decide whether a rewrite would
        // narrow a value, so a fixer could judge, and rewrite, a property having seen one of its three
        // values. That is why this is worth changing a public projection over.
        if (node.IsArray && node.IsArrayAlternate && (node.IsArrayAltText || node.Children.Count <= 1))
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
            return new XmpProperty(node.NamespaceUri, node.Prefix, node.LocalName, items,
                                   node.IsArrayOrdered, node.IsArrayAlternate);
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
                         IReadOnlyList<string> items, bool ordered, bool isAlternate = false)
    {
        NamespaceUri = namespaceUri;
        Prefix       = prefix;
        LocalName    = localName;
        Kind         = XmpValueKind.Array;
        Items        = items;
        Ordered      = ordered;
        IsAlternate  = isAlternate;
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
