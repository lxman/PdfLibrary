using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PdfLibrary.Metadata;

/// <summary>
/// A mutable, round-trip-stable XMP metadata packet.
/// Parse from raw stream bytes; mutate via Set*/Remove; serialize back via <see cref="Serialize"/>.
/// </summary>
public sealed class XmpPacket
{
    // Keyed by (namespaceUri, localName).
    // We store the full XmpProperty to carry prefix + value shape together.
    private readonly Dictionary<(string ns, string local), XmpProperty> _props =
        new(EqualityComparer<(string, string)>.Default);

    // Namespace-uri → preferred prefix; populated during parse, used during serialize.
    private readonly Dictionary<string, string> _prefixMap = new(StringComparer.Ordinal);

    private XmpPacket() { }

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>Creates a packet with the standard well-known namespace prefixes seeded.</summary>
    public static XmpPacket CreateEmpty()
    {
        var pkt = new XmpPacket();
        pkt.SeedWellKnownPrefixes();
        return pkt;
    }

    /// <summary>
    /// Parses an XMP packet from the raw bytes of a /Metadata stream.
    /// Tolerant: if the bytes are not valid XMP, returns an empty packet rather than throwing.
    /// </summary>
    public static XmpPacket Parse(byte[] xmpBytes)
    {
        var pkt = new XmpPacket();
        pkt.SeedWellKnownPrefixes();
        if (xmpBytes is null || xmpBytes.Length == 0) return pkt;

        try
        {
            // Strip leading UTF-8 BOM if present (the xpacket PI may already embed one as a char).
            ReadOnlySpan<byte> span = xmpBytes;
            if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
                xmpBytes = xmpBytes[3..];

            // XDocument tolerates the <?xpacket?> PIs (they're parsed as XProcessingInstruction).
            XDocument doc;
            using (var reader = new StringReader(Encoding.UTF8.GetString(xmpBytes)))
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Ignore,
                    IgnoreWhitespace = false,
                    ConformanceLevel = ConformanceLevel.Document
                };
                using XmlReader xr = XmlReader.Create(reader, settings);
                doc = XDocument.Load(xr, LoadOptions.PreserveWhitespace);
            }

            // Find x:xmpmeta (or fall through to rdf:RDF at root)
            XElement? rdfRdf = FindRdfRdf(doc);
            if (rdfRdf is null) return pkt;

            foreach (XElement desc in rdfRdf.Elements(Rdf("Description")))
            {
                pkt.ParseDescription(desc);
            }
        }
        catch
        {
            // Tolerant: any exception → return what we have (likely empty)
        }

        return pkt;
    }

    // ── Generic access ────────────────────────────────────────────────────────

    /// <summary>Returns the property matching the given namespace URI and local name, or null.</summary>
    public XmpProperty? Get(string namespaceUri, string localName) =>
        _props.TryGetValue((namespaceUri, localName), out XmpProperty? p) ? p : null;

    /// <summary>All properties in the packet.</summary>
    public IEnumerable<XmpProperty> Properties => _props.Values;

    // ── Setters ───────────────────────────────────────────────────────────────

    /// <summary>Sets or replaces a simple string property.</summary>
    public void SetSimple(string namespaceUri, string prefix, string localName, string value)
    {
        ArgumentNullException.ThrowIfNull(namespaceUri);
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(localName);
        ArgumentNullException.ThrowIfNull(value);
        RegisterPrefix(namespaceUri, prefix);
        _props[(namespaceUri, localName)] = new XmpProperty(namespaceUri, prefix, localName, value);
    }

    /// <summary>Sets or replaces an array property (Seq when <paramref name="ordered"/>=true, Bag otherwise).</summary>
    public void SetArray(string namespaceUri, string prefix, string localName,
                         IEnumerable<string> items, bool ordered)
    {
        ArgumentNullException.ThrowIfNull(namespaceUri);
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(localName);
        ArgumentNullException.ThrowIfNull(items);
        RegisterPrefix(namespaceUri, prefix);
        IReadOnlyList<string> list = items.ToList();
        _props[(namespaceUri, localName)] = new XmpProperty(namespaceUri, prefix, localName, list, ordered);
    }

    /// <summary>Sets or merges a language alternative property.</summary>
    public void SetLangAlt(string namespaceUri, string prefix, string localName,
                            string text, string lang = "x-default")
    {
        ArgumentNullException.ThrowIfNull(namespaceUri);
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(localName);
        ArgumentNullException.ThrowIfNull(text);
        RegisterPrefix(namespaceUri, prefix);

        // If a LangAlt property already exists, merge into it; otherwise create fresh.
        Dictionary<string, string> map;
        if (_props.TryGetValue((namespaceUri, localName), out XmpProperty? existing) &&
            existing.Kind == XmpValueKind.LangAlt)
        {
            map = new Dictionary<string, string>(existing.LangAlt, StringComparer.Ordinal);
        }
        else
        {
            map = new Dictionary<string, string>(StringComparer.Ordinal);
        }
        map[lang] = text;
        // Ensure x-default mirrors the value when lang is x-default
        _props[(namespaceUri, localName)] = new XmpProperty(namespaceUri, prefix, localName, map);
    }

    /// <summary>Removes a property. No-op if absent.</summary>
    public void Remove(string namespaceUri, string localName) =>
        _props.Remove((namespaceUri, localName));

    // ── Serialize ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Serializes the packet to a full xpacket-wrapped, UTF-8-encoded byte array with ~2 KB of
    /// padding and a trailing <c>&lt;?xpacket end="w"?&gt;</c> instruction.
    /// All formatting uses <see cref="CultureInfo.InvariantCulture"/>.
    /// </summary>
    public byte[] Serialize()
    {
        var sb = new StringBuilder();

        // Header PI (the "﻿" is the Unicode BOM character U+FEFF embedded as UTF-8 text, per XMP spec)
        sb.Append("<?xpacket begin=\"\xEF\xBB\xBF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n");

        // x:xmpmeta
        sb.Append("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n");
        sb.Append("  <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n");
        sb.Append("    <rdf:Description rdf:about=\"\"");

        // Declare all namespaces (except rdf/x which are already declared above)
        foreach ((string nsUri, string prefix) in _prefixMap)
        {
            if (nsUri == XmpSchemas.Rdf || nsUri == XmpSchemas.X) continue;
            sb.Append($"\n        xmlns:{XmlEncode(prefix)}=\"{XmlEncode(nsUri)}\"");
        }
        sb.Append(">\n");

        // Properties
        foreach (XmpProperty prop in _props.Values)
        {
            string qname = $"{prop.Prefix}:{prop.LocalName}";
            switch (prop.Kind)
            {
                case XmpValueKind.Simple:
                    sb.Append($"      <{qname}>{XmlEncode(prop.Value!)}</{qname}>\n");
                    break;

                case XmpValueKind.Array:
                {
                    string container = prop.Ordered ? "rdf:Seq" : "rdf:Bag";
                    sb.Append($"      <{qname}><{container}>\n");
                    foreach (string item in prop.Items)
                        sb.Append($"          <rdf:li>{XmlEncode(item)}</rdf:li>\n");
                    sb.Append($"        </{container}></{qname}>\n");
                    break;
                }

                case XmpValueKind.LangAlt:
                {
                    sb.Append($"      <{qname}><rdf:Alt>\n");
                    // x-default first if present, then others sorted for stability
                    if (prop.LangAlt.TryGetValue("x-default", out string? defText))
                        sb.Append($"          <rdf:li xml:lang=\"x-default\">{XmlEncode(defText)}</rdf:li>\n");
                    foreach ((string lang, string text) in prop.LangAlt
                                 .Where(kv => kv.Key != "x-default")
                                 .OrderBy(kv => kv.Key, StringComparer.Ordinal))
                        sb.Append($"          <rdf:li xml:lang=\"{XmlEncode(lang)}\">{XmlEncode(text)}</rdf:li>\n");
                    sb.Append($"        </rdf:Alt></{qname}>\n");
                    break;
                }
            }
        }

        sb.Append("    </rdf:Description>\n");
        sb.Append("  </rdf:RDF>\n");
        sb.Append("</x:xmpmeta>\n");

        // ~2 KB padding of ASCII spaces (in 80-char lines) before trailing PI
        int paddingChars = 2048;
        int lineLen = 80;
        while (paddingChars > 0)
        {
            int take = Math.Min(paddingChars, lineLen);
            sb.Append(' ', take);
            sb.Append('\n');
            paddingChars -= take;
        }

        // Trailer PI
        sb.Append("<?xpacket end=\"w\"?>");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private static XName Rdf(string local) =>
        XName.Get(local, XmpSchemas.Rdf);

    private static XElement? FindRdfRdf(XDocument doc)
    {
        // x:xmpmeta / rdf:RDF
        XElement? meta = doc.Root?.Name.LocalName == "xmpmeta" ? doc.Root : null;
        if (meta is null)
        {
            // Fallback: search the whole document
            meta = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "xmpmeta");
        }
        XElement? rdfChild = meta?.Element(Rdf("RDF"));
        if (rdfChild is not null) return rdfChild;
        // Fallback: root is rdf:RDF itself
        return doc.Root?.Name == Rdf("RDF") ? doc.Root : null;
    }

    private void ParseDescription(XElement desc)
    {
        // Collect namespace declarations on this element
        foreach (XAttribute nsDecl in desc.Attributes().Where(a => a.IsNamespaceDeclaration))
        {
            string prefix = nsDecl.Name.LocalName == "xmlns" ? "" : nsDecl.Name.LocalName;
            if (!string.IsNullOrEmpty(prefix))
                RegisterPrefix(nsDecl.Value, prefix);
        }

        // (a) Attribute-form properties (skip rdf:about and xmlns:* declarations)
        foreach (XAttribute attr in desc.Attributes()
            .Where(a => !a.IsNamespaceDeclaration && a.Name.LocalName != "about"))
        {
            string ns = attr.Name.NamespaceName;
            string local = attr.Name.LocalName;
            if (string.IsNullOrEmpty(ns) || ns == XmpSchemas.Rdf) continue;
            string prefix = PrefixFor(ns, local);
            _props[(ns, local)] = new XmpProperty(ns, prefix, local, attr.Value);
        }

        // (b/c/d/e) Child element-form properties
        foreach (XElement child in desc.Elements())
        {
            string ns = child.Name.NamespaceName;
            string local = child.Name.LocalName;
            if (string.IsNullOrEmpty(ns)) continue;

            // Collect any namespace declarations on the child element too
            foreach (XAttribute nsDecl in child.Attributes().Where(a => a.IsNamespaceDeclaration))
            {
                string pref = nsDecl.Name.LocalName == "xmlns" ? "" : nsDecl.Name.LocalName;
                if (!string.IsNullOrEmpty(pref))
                    RegisterPrefix(nsDecl.Value, pref);
            }

            string prefix = PrefixFor(ns, local);

            // Does this element contain an rdf:Alt / rdf:Seq / rdf:Bag?
            XElement? altEl = child.Element(Rdf("Alt"));
            XElement? seqEl = child.Element(Rdf("Seq"));
            XElement? bagEl = child.Element(Rdf("Bag"));

            if (altEl is not null)
            {
                // LangAlt
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (XElement li in altEl.Elements(Rdf("li")))
                {
                    XAttribute? langAttr = li.Attribute(XNamespace.Xml + "lang");
                    string lang = langAttr?.Value ?? "x-default";
                    map[lang] = li.Value;
                }
                _props[(ns, local)] = new XmpProperty(ns, prefix, local, map);
            }
            else if (seqEl is not null)
            {
                // Ordered array
                List<string> items = seqEl.Elements(Rdf("li")).Select(li => li.Value).ToList();
                _props[(ns, local)] = new XmpProperty(ns, prefix, local, items, ordered: true);
            }
            else if (bagEl is not null)
            {
                // Unordered array
                List<string> items = bagEl.Elements(Rdf("li")).Select(li => li.Value).ToList();
                _props[(ns, local)] = new XmpProperty(ns, prefix, local, items, ordered: false);
            }
            else
            {
                // Simple element text
                _props[(ns, local)] = new XmpProperty(ns, prefix, local, child.Value);
            }
        }
    }

    private void SeedWellKnownPrefixes()
    {
        RegisterPrefix(XmpSchemas.Dc,  XmpSchemas.DcPrefix);
        RegisterPrefix(XmpSchemas.Xmp, XmpSchemas.XmpPrefix);
        RegisterPrefix(XmpSchemas.Pdf, XmpSchemas.PdfPrefix);
        RegisterPrefix(XmpSchemas.Rdf, XmpSchemas.RdfPrefix);
        RegisterPrefix(XmpSchemas.X,   XmpSchemas.XPrefix);
    }

    private void RegisterPrefix(string ns, string prefix)
    {
        if (!_prefixMap.ContainsKey(ns))
            _prefixMap[ns] = prefix;
    }

    private string PrefixFor(string ns, string localNameHint)
    {
        if (_prefixMap.TryGetValue(ns, out string? p)) return p;
        // Generate a fallback prefix from the last path segment of the URI
        string fallback = "ns" + Math.Abs(ns.GetHashCode() % 1000).ToString(CultureInfo.InvariantCulture);
        _prefixMap[ns] = fallback;
        return fallback;
    }

    private static string XmlEncode(string s)
    {
        // Minimal XML entity encoding for element text and attribute values
        return s
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }
}
