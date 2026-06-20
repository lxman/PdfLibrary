namespace PdfLibrary.Metadata;

/// <summary>
/// Well-known XMP namespace URIs and their preferred prefixes.
/// </summary>
public static class XmpSchemas
{
    // ── Namespace URIs ────────────────────────────────────────────────────────

    /// <summary>Dublin Core — <c>http://purl.org/dc/elements/1.1/</c></summary>
    public const string Dc  = "http://purl.org/dc/elements/1.1/";

    /// <summary>XMP basic — <c>http://ns.adobe.com/xap/1.0/</c></summary>
    public const string Xmp = "http://ns.adobe.com/xap/1.0/";

    /// <summary>Adobe PDF schema — <c>http://ns.adobe.com/pdf/1.3/</c></summary>
    public const string Pdf = "http://ns.adobe.com/pdf/1.3/";

    /// <summary>RDF — <c>http://www.w3.org/1999/02/22-rdf-syntax-ns#</c></summary>
    public const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    /// <summary>Adobe XMP packet envelope — <c>adobe:ns:meta/</c></summary>
    public const string X   = "adobe:ns:meta/";

    // ── Preferred prefixes ────────────────────────────────────────────────────

    /// <summary>Preferred prefix for <see cref="Dc"/>.</summary>
    public const string DcPrefix  = "dc";

    /// <summary>Preferred prefix for <see cref="Xmp"/>.</summary>
    public const string XmpPrefix = "xmp";

    /// <summary>Preferred prefix for <see cref="Pdf"/>.</summary>
    public const string PdfPrefix = "pdf";

    /// <summary>Preferred prefix for <see cref="Rdf"/>.</summary>
    public const string RdfPrefix = "rdf";

    /// <summary>Preferred prefix for <see cref="X"/>.</summary>
    public const string XPrefix   = "x";
}
