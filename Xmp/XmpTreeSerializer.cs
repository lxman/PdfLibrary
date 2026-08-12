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
/// packet.</para></summary>
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

        var namespaces = new Dictionary<string, string>();   // uri -> prefix
        foreach (XmpNode property in properties)
            CollectNamespaces(property, namespaces);

        foreach (KeyValuePair<string, string> ns in namespaces)
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

    private static void CollectNamespaces(XmpNode node, IDictionary<string, string> into)
    {
        if (!string.IsNullOrEmpty(node.NamespaceUri) && !into.ContainsKey(node.NamespaceUri))
            into[node.NamespaceUri] = node.Prefix;

        foreach (XmpNode child in node.Children)
            CollectNamespaces(child, into);
    }

    private static XElement Emit(XmpNode node)
    {
        XNamespace ns = node.NamespaceUri;
        var element = new XElement(ns + node.LocalName);

        if (node.IsArray)
        {
            string container = node.IsArrayAlternate ? "Alt" : node.IsArrayOrdered ? "Seq" : "Bag";
            var array = new XElement(Rdf + container);
            foreach (XmpNode item in node.Children)
                array.Add(EmitArrayItem(item));
            element.Add(array);
            return element;
        }

        if (node.IsStruct)
        {
            element.Add(new XAttribute(Rdf + "parseType", "Resource"));
            foreach (XmpNode field in node.Children)
                element.Add(Emit(field));
            return element;
        }

        if (node.HasXmlLang && node.XmlLang is { } lang)
            element.Add(new XAttribute(XmlNs + "lang", lang));

        element.Value = node.Value ?? string.Empty;
        return element;
    }

    /// <summary>An array item is an rdf:li whose content is the item's own shape — a struct item
    /// carries parseType="Resource" and its fields, a simple item carries text (plus xml:lang for
    /// alt-text). The item node's own name is not emitted; rdf:li replaces it.</summary>
    private static XElement EmitArrayItem(XmpNode item)
    {
        var li = new XElement(Rdf + "li");

        if (item.IsStruct)
        {
            li.Add(new XAttribute(Rdf + "parseType", "Resource"));
            foreach (XmpNode field in item.Children)
                li.Add(Emit(field));
            return li;
        }

        if (item.HasXmlLang && item.XmlLang is { } lang)
            li.Add(new XAttribute(XmlNs + "lang", lang));

        li.Value = item.Value ?? string.Empty;
        return li;
    }
}
