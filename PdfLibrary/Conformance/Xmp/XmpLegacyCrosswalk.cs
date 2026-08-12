using System.Collections.Generic;

namespace PdfLibrary.Conformance.Xmp;

/// <summary>Pre-2005 XMP property spellings and the predefined properties that superseded them.
/// These fire clause 6.6.2.3.1 because they are not predefined, but declaring them via an extension
/// schema would assert a private schema for something the standard already covers — the right fix is
/// to move the value to its modern equivalent. Measured on a 708-file corpus, these eight account for
/// 111 findings, of which 98.2% migrate with no collision.
///
/// <para>Note that 'xap' is the former NAME of the xmp namespace, not a different URI: xap:Title and
/// xmp:Title are the same property, so both appear here under the one xap/1.0 URI.</para>
///
/// <para><b>Three of the eight targets are not scalar.</b> <c>dc:title</c> and <c>dc:description</c>
/// are <c>lang alt</c>; <c>dc:creator</c> is <c>seq propername</c>. Every legacy source here is a
/// plain string, so a caller migrating a value must WRAP it to match — a Lang Alt needs an
/// <c>x-default</c> item, a Seq needs a one-item sequence — not copy the literal across. This table
/// intentionally carries no type; the target's predefined type is recoverable from
/// <see cref="XmpPredefinedSchemas.TypeOf"/> on the returned namespace/local name.</para></summary>
internal static class XmpLegacyCrosswalk
{
    private const string Pdf = "http://ns.adobe.com/pdf/1.3/";
    private const string Xmp = "http://ns.adobe.com/xap/1.0/";
    private const string Dc = "http://purl.org/dc/elements/1.1/";

    private static readonly Dictionary<(string ns, string name), (string ns, string prefix, string name)> Map =
        new()
        {
            [(Pdf, "Title")]        = (Dc,  "dc",  "title"),
            [(Pdf, "Author")]       = (Dc,  "dc",  "creator"),
            [(Pdf, "Subject")]      = (Dc,  "dc",  "description"),
            [(Pdf, "Creator")]      = (Xmp, "xmp", "CreatorTool"),
            [(Pdf, "CreationDate")] = (Xmp, "xmp", "CreateDate"),
            [(Pdf, "ModDate")]      = (Xmp, "xmp", "ModifyDate"),
            [(Xmp, "Title")]        = (Dc,  "dc",  "title"),
            [(Xmp, "Author")]       = (Dc,  "dc",  "creator"),
        };

    public static bool TryGet(string namespaceUri, string localName,
                              out (string ns, string prefix, string name) modern) =>
        Map.TryGetValue((namespaceUri, localName), out modern);
}
