namespace PdfLibrary.Metadata;

/// <summary>Discriminates the three value shapes that XMP properties can take.</summary>
public enum XmpValueKind
{
    /// <summary>A single plain string value.</summary>
    Simple,

    /// <summary>An ordered (<c>rdf:Seq</c>) or unordered (<c>rdf:Bag</c>) list of strings.</summary>
    Array,

    /// <summary>A language-alternative map (<c>rdf:Alt</c>), keyed by language tag (e.g. <c>x-default</c>).</summary>
    LangAlt
}
