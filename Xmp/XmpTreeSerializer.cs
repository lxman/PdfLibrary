using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
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
    /// file; the XMP spec recommends roughly 2 KB — ≈2 KB, as the previous writer emitted. The shape
    /// differs: the previous writer spread its padding across 80-char lines, where this emits it as
    /// 2048 spaces on one line. (The `begin=` BOM is unaffected by this change and is still
    /// emitted.)</summary>
    private const int PaddingBytes = 2048;

    /// <summary>
    /// Emits the tree as a complete xpacket-wrapped UTF-8 packet.
    ///
    /// <para>Serializing is TOTAL over property VALUES: a value carrying a character XML 1.0 does not
    /// permit (a NUL or other stray control character) or an unpaired surrogate is sanitized on the
    /// way out rather than throwing — see <see cref="Sanitize"/>. A PDF library meets that kind of
    /// garbage in real packets it did not write, and a save must not fail because of it.</para>
    ///
    /// <para>An invalid property or field NAME is a different matter — it is caller error, not
    /// document data — and throws <see cref="ArgumentException"/>. An invalid namespace PREFIX never
    /// throws; <see cref="AssignPrefixes"/> synthesizes a legal one.</para>
    /// </summary>
    /// <exception cref="ArgumentException">A node's <see cref="XmpNode.LocalName"/> is not a legal
    /// XML name.</exception>
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
        // The begin attribute carries the Unicode BOM character U+FEFF, which UTF-8-encodes to the
        // canonical three bytes EF BB BF. This is what Adobe writes and what a scanner looking for
        // the packet header expects; XmpPacketRegressionTests pins both the canonical form and the
        // absence of the six-byte "ï»¿" mojibake a naive Latin-1 escape produces.
        sb.Append("<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n");
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
            if (IsLegalName(candidate) && usedPrefixes.Add(candidate))
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
        // A shape this node model cannot express was preserved verbatim at parse time; re-emit that
        // subtree unchanged rather than routing it through the normal name/shape machinery, which has
        // nowhere to put it. XElement.Parse re-declares whatever namespaces the fragment's own
        // elements/attributes use that were only in scope via an ancestor in the source document, so
        // the fragment stays self-contained and valid wherever it lands in the rewritten tree.
        if (node.RawXml is { } raw)
            return XElement.Parse(raw);

        XNamespace ns = node.NamespaceUri;
        var element = new XElement(ns + VerifyName(node.LocalName));
        EmitShape(node, element);
        return element;
    }

    /// <summary>A property or struct-field name must be a legal XML name — unlike a value, it cannot
    /// be sanitized into something meaningful, and a name that is not a name means the caller built a
    /// nonsense node. Thrown as <see cref="ArgumentException"/> so the contract is this type's and
    /// stable, rather than whatever <see cref="System.Xml.Linq"/> happens to surface (which is
    /// variously <see cref="XmlException"/> or <see cref="ArgumentException"/> depending on how the
    /// name is malformed).</summary>
    private static string VerifyName(string localName)
    {
        if (IsLegalName(localName))
            return localName;

        throw new ArgumentException(
            $"'{localName}' is not a legal XML name, so it cannot be written as an XMP property or "
            + "field name.", nameof(localName));
    }

    private static bool IsLegalName(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        try
        {
            XmlConvert.VerifyNCName(name);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    /// <summary>
    /// Makes a value string writable. XML 1.0 admits only tab, LF, CR, and the ranges
    /// #x20-#xD7FF / #xE000-#xFFFD / #x10000-#x10FFFF; a lone surrogate is not a character at all.
    /// Everything outside that becomes U+FFFD (REPLACEMENT CHARACTER), which is what a decoder would
    /// have produced anyway and keeps the emitted packet re-parseable.
    /// </summary>
    /// <remarks>The string writer this serializer replaced was accidentally total here: it
    /// concatenated the raw value and let <c>Encoding.UTF8.GetBytes</c> turn lone surrogates into
    /// U+FFFD. It did NOT strip control characters, so a value carrying a NUL produced a packet that
    /// no longer parsed. Sanitizing keeps the "never throws on a value" guarantee that behaviour
    /// implied while also fixing the unparseable-output half of it.</remarks>
    private static string Sanitize(string value)
    {
        if (value.Length == 0)
            return value;

        StringBuilder? clean = null;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];

            // A well-formed surrogate pair is a legal character (planes 1-16); copy it whole.
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                clean?.Append(c).Append(value[i + 1]);
                i++;
                continue;
            }

            bool legal = c is '\t' or '\n' or '\r'
                         || (c >= ' ' && c <= '\uD7FF')
                         || (c >= '\uE000' && c <= '\uFFFD');

            if (legal)
            {
                clean?.Append(c);
                continue;
            }

            clean ??= new StringBuilder(value.Length).Append(value, 0, i);
            clean.Append('\uFFFD');
        }

        return clean?.ToString() ?? value;
    }

    /// <summary>An array item is an rdf:li whose content is the item's own shape — a struct item
    /// carries parseType="Resource" and its fields, an array item nests its own rdf:Bag/Seq/Alt, and
    /// a simple item carries text (plus xml:lang for alt-text). The item node's own name is not
    /// emitted; rdf:li replaces it.</summary>
    private static XElement EmitArrayItem(XmpNode item)
    {
        // As in Emit: an unmodelled array item (its raw capture already includes its own rdf:li tag)
        // is re-emitted verbatim rather than re-wrapped.
        if (item.RawXml is { } raw)
            return XElement.Parse(raw);

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
        // xml:lang is a qualifier on the ITEM, independent of the item's own value shape — an
        // rdf:Alt entry (e.g. a pdfaExtension description) can be struct- or array-shaped and still
        // carry xml:lang. Emitting it before the shape dispatch below means every branch (array,
        // struct, simple) carries it, instead of only the simple fall-through path reaching it.
        if (node.HasXmlLang && node.XmlLang is { } lang)
            element.Add(new XAttribute(XmlNs + "lang", Sanitize(lang)));

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

        element.Value = Sanitize(node.Value ?? string.Empty);
    }
}
