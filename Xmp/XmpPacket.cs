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

        // If a LangAlt property already exists, merge into it; otherwise create fresh.
        Dictionary<string, string> map;
        if (_nodes.TryGetValue((namespaceUri, localName), out XmpNode? existing) &&
            XmpProperty.FromNode(existing) is { Kind: XmpValueKind.LangAlt } existingProp)
        {
            map = existingProp.LangAlt.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);
        }
        else
        {
            map = new Dictionary<string, string>(StringComparer.Ordinal);
        }
        map[lang] = text;

        var node = new XmpNode(namespaceUri, localName, prefix)
        {
            IsArray = true,
            IsArrayOrdered = true,
            IsArrayAlternate = true,
            IsArrayAltText = true,
        };

        // x-default first if present, then the others sorted for stability — the emission order the
        // string writer this facade replaced used.
        if (map.TryGetValue("x-default", out string? defText))
            node.Children.Add(AltItem("x-default", defText));
        foreach (KeyValuePair<string, string> entry in map
                     .Where(kv => kv.Key != "x-default")
                     .OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            node.Children.Add(AltItem(entry.Key, entry.Value));
        }

        _nodes[(namespaceUri, localName)] = node;
    }

    /// <summary>Removes a property. No-op if absent.</summary>
    public void Remove(string namespaceUri, string localName) =>
        _nodes.Remove((namespaceUri, localName));

    // ── Serialize ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Serializes the packet to a full xpacket-wrapped, UTF-8-encoded byte array with ~2 KB of
    /// padding and a trailing <c>&lt;?xpacket end="w"?&gt;</c> instruction.
    /// </summary>
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
}
