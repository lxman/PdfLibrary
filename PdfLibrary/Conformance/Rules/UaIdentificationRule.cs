using System.IO;
using System.Xml;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Metadata;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 identification (ISO 14289-1:2014, clause 5): a conforming file must declare itself through the
/// PDF/UA identification schema — the property <c>part</c> in the AIIM namespace
/// <c>http://www.aiim.org/pdfua/ns/id/</c> (prefix <c>pdfuaid</c>), carried in the XMP metadata — with the
/// value <c>1</c>. A missing metadata stream, an absent <c>pdfuaid:part</c>, or a different value is a violation.
/// The schema's properties (<c>part</c>/<c>amd</c>/<c>corr</c>) must also be serialized with the <c>pdfuaid</c>
/// prefix (clause 5, tests 3–5): a property carrying that namespace URI under a different prefix is flagged.
/// </summary>
internal sealed class UaIdentificationRule : IConformanceRule
{
    private const string PdfUaIdNs = "http://www.aiim.org/pdfua/ns/id/";

    public string RuleId => "ua-identification";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        PdfStream? metadata = context.Catalog?.GetMetadata();
        if (metadata is null)
        {
            yield return Error(context,
                "The XMP metadata is missing, so the PDF/UA identification (pdfuaid:part) cannot be verified.");
            yield break;
        }

        byte[] xmp = metadata.GetDecodedData(context.Document.Decryptor);
        XmpPacket packet = XmpPacket.Parse(xmp);
        string? part = packet.Get(PdfUaIdNs, "part")?.Value?.Trim();

        if (part != "1")
        {
            yield return Error(context, part is null
                ? "The XMP metadata lacks the PDF/UA identification property pdfuaid:part "
                  + "(namespace http://www.aiim.org/pdfua/ns/id/), which PDF/UA-1 requires."
                : $"pdfuaid:part is '{part}', but PDF/UA-1 requires the value 1.");
        }

        // Clause 5 (t3/t4/t5): the identification-schema properties part/amd/corr must be serialized with the
        // prefix "pdfuaid". veraPDF resolves each by the AIIM namespace URI and requires that literal prefix;
        // an absent (default-namespace) prefix is allowed. We must read each property's ACTUAL serialized
        // prefix, which needs a low-level reader: XLinq/XmpPacket collapse multiple prefixes bound to one URI
        // (t04/t05 declare both pdfuaid and pdfuaia for the AIIM namespace), losing which prefix a given
        // property actually used.
        foreach ((string local, string prefix) in MisPrefixedIdProperties(xmp))
        {
            yield return Error(context,
                $"The PDF/UA identification property '{local}' uses the namespace prefix '{prefix}' "
                + "instead of the required 'pdfuaid'.");
        }
    }

    // The part/amd/corr properties in the AIIM pdfuaid namespace whose serialized prefix is present but not
    // "pdfuaid". Reads element- and attribute-form properties via XmlReader (which exposes the real per-node
    // prefix). Tolerant: an unparseable packet yields nothing — never a false positive.
    private static IEnumerable<(string Local, string Prefix)> MisPrefixedIdProperties(byte[] xmp)
    {
        if (xmp is null)
            return [];

        var hits = new List<(string, string)>();
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(xmp, writable: false),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;

                Consider(reader.NamespaceURI, reader.LocalName, reader.Prefix, hits);
                if (!reader.HasAttributes)
                    continue;
                for (bool more = reader.MoveToFirstAttribute(); more; more = reader.MoveToNextAttribute())
                    Consider(reader.NamespaceURI, reader.LocalName, reader.Prefix, hits);
                reader.MoveToElement();
            }
        }
        catch (XmlException)
        {
            return [];
        }

        return hits;

        static void Consider(string ns, string local, string prefix, List<(string, string)> into)
        {
            if (ns == PdfUaIdNs && local is "part" or "amd" or "corr" && prefix is { Length: > 0 } && prefix != "pdfuaid")
                into.Add((local, prefix));
        }
    }

    private Finding Error(ConformanceContext context, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "5"),
        Message = message,
    };
}
