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
