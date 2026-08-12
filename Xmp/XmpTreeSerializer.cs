using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace PdfLibrary.Xmp;

/// <summary>The write half of <see cref="XmpTreeParser"/>. Emits the node tree as XMP-shaped
/// RDF/XML: structs as <c>rdf:parseType="Resource"</c>, arrays as <c>rdf:Seq</c>/<c>Bag</c>/<c>Alt</c>,
/// alt-text items carrying <c>xml:lang</c>.
///
/// <para>Every namespace used ANYWHERE in the tree is declared on the rdf:Description — including
/// struct-field namespaces such as <c>stEvt:</c>, which can appear without any top-level property
/// using them. Missing those declarations is how a faithful tree still serializes to a broken
/// packet.</para>
///
/// <para>Prefixes are re-assigned deterministically at write time rather than reused verbatim from
/// the parse: two different namespace URIs can have resolved to the same prefix string in different
/// parts of the source packet, and a default-namespace ("xmlns=...", no prefix) property has an
/// empty prefix that is not a legal attribute name. <see cref="AssignPrefixes"/> gives every URI a
/// unique, non-empty prefix — keeping the original when it is safe to reuse, synthesizing
/// <c>ns1</c>, <c>ns2</c>, … otherwise — so the emitted packet is internally consistent.</para></summary>
internal static class XmpTreeSerializer
{
    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace X = "adobe:ns:meta/";
    private static readonly XNamespace XmlNs = "http://www.w3.org/XML/1998/namespace";

    /// <summary>Padding lets a consumer rewrite the packet in place without moving the rest of the
    /// file; the XMP spec recommends roughly 2 KB. Matches the previous writer's behaviour.</summary>
    private const int PaddingBytes = 2048;

    internal static byte[] Serialize(IReadOnlyList<XmpNode> properties)
    {
        var description = new XElement(Rdf + "Description", new XAttribute(Rdf + "about", string.Empty));

        Dictionary<string, string> prefixByUri = AssignPrefixes(properties);
        foreach (KeyValuePair<string, string> ns in prefixByUri)
            description.Add(new XAttribute(XNamespace.Xmlns + ns.Value, ns.Key));

        foreach (XmpNode property in properties)
            description.Add(Emit(property));

        var meta = new XElement(X + "xmpmeta",
            new XAttribute(XNamespace.Xmlns + "x", X.NamespaceName),
            new XElement(Rdf + "RDF",
                new XAttribute(XNamespace.Xmlns + "rdf", Rdf.NamespaceName),
                description));

        var sb = new StringBuilder();
        sb.Append("<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n");
        sb.Append(meta);
        sb.Append('\n');
        sb.Append(' ', PaddingBytes);
        sb.Append("\n<?xpacket end=\"w\"?>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>Walks the whole tree (not just top-level properties — a struct field nested inside
    /// an array item can be the only user of its namespace) and assigns each distinct namespace URI
    /// a unique, non-empty prefix. The "rdf" and "x" prefixes are reserved for the ancestor
    /// <c>rdf:RDF</c>/<c>x:xmpmeta</c> declarations, so a colliding or empty source prefix is
    /// replaced with a synthesized <c>ns1</c>, <c>ns2</c>, … rather than reused.</summary>
    private static Dictionary<string, string> AssignPrefixes(IReadOnlyList<XmpNode> properties)
    {
        var uris = new List<string>();
        var preferredPrefix = new Dictionary<string, string>();
        var seen = new HashSet<string>();

        void Collect(XmpNode node)
        {
            if (!string.IsNullOrEmpty(node.NamespaceUri) && seen.Add(node.NamespaceUri))
            {
                uris.Add(node.NamespaceUri);
                preferredPrefix[node.NamespaceUri] = node.Prefix;
            }

            foreach (XmpNode child in node.Children)
                Collect(child);
        }

        foreach (XmpNode property in properties)
            Collect(property);

        var prefixByUri = new Dictionary<string, string>();
        var usedPrefixes = new HashSet<string> { "rdf", "x", "xml", "xmlns" };
        int synthesized = 0;

        foreach (string uri in uris)
        {
            string candidate = preferredPrefix[uri];
            if (!string.IsNullOrEmpty(candidate) && usedPrefixes.Add(candidate))
            {
                prefixByUri[uri] = candidate;
                continue;
            }

            string generated;
            do
            {
                synthesized++;
                generated = "ns" + synthesized;
            } while (!usedPrefixes.Add(generated));

            prefixByUri[uri] = generated;
        }

        return prefixByUri;
    }

    private static XElement Emit(XmpNode node)
    {
        XNamespace ns = node.NamespaceUri;
        var element = new XElement(ns + node.LocalName);
        EmitShape(node, element);
        return element;
    }

    /// <summary>An array item is an rdf:li whose content is the item's own shape — a struct item
    /// carries parseType="Resource" and its fields, an array item nests its own rdf:Bag/Seq/Alt, and
    /// a simple item carries text (plus xml:lang for alt-text). The item node's own name is not
    /// emitted; rdf:li replaces it.</summary>
    private static XElement EmitArrayItem(XmpNode item)
    {
        var li = new XElement(Rdf + "li");
        EmitShape(item, li);
        return li;
    }

    /// <summary>Fills <paramref name="element"/> with <paramref name="node"/>'s content according to
    /// its actual shape (array / struct / simple) — shared by <see cref="Emit"/> and
    /// <see cref="EmitArrayItem"/> so a shape this dispatch does not know about cannot silently fall
    /// through to the simple-text branch and vanish (an array item that is itself an array, in
    /// particular). The two callers differ only in how the element itself is named.</summary>
    private static void EmitShape(XmpNode node, XElement element)
    {
        if (node.IsArray)
        {
            string container = node.IsArrayAlternate ? "Alt" : node.IsArrayOrdered ? "Seq" : "Bag";
            var array = new XElement(Rdf + container);
            foreach (XmpNode item in node.Children)
                array.Add(EmitArrayItem(item));
            element.Add(array);
            return;
        }

        if (node.IsStruct)
        {
            element.Add(new XAttribute(Rdf + "parseType", "Resource"));
            foreach (XmpNode field in node.Children)
                element.Add(Emit(field));
            return;
        }

        if (node.HasXmlLang && node.XmlLang is { } lang)
            element.Add(new XAttribute(XmlNs + "lang", lang));

        element.Value = node.Value ?? string.Empty;
    }
}
