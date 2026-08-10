using System.Text;
using System.Xml;
using System.Xml.Linq;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Metadata;
using PdfLibrary.Structure;

namespace PdfLibrary.Conformance;

/// <summary>Reads the conformance standard a document CLAIMS in its XMP identification
/// schemas. Precedence when several are claimed: PDF/A, then PDF/X-4, then PDF/UA-1.
/// Returns null when nothing the engine supports is claimed or the XMP is unreadable —
/// claiming nothing is not an error, so this never throws on bad metadata.</summary>
public static class ConformanceClaim
{
    // Same namespace URIs and property names PdfaIdentificationRule / PdfxVersionRule /
    // UaIdentificationRule check when verifying a document AGAINST a target profile. This type
    // reads the same schemas but with no target in play, to discover what the document claims.
    private const string PdfaIdNs = "http://www.aiim.org/pdfa/ns/id/";
    private const string PdfxIdNs = "http://www.npes.org/pdfx/ns/id/";
    private const string PdfxVersionProperty = "GTS_PDFXVersion";
    private const string PdfUaIdNs = "http://www.aiim.org/pdfua/ns/id/";

    /// <summary>Reads the profile the document claims, or null if none the engine supports is
    /// claimed (including when the metadata is missing or unparseable).</summary>
    public static ConformanceProfile? Read(PdfDocument document) => Read(document, out _);

    /// <summary><paramref name="xmpUnreadable"/> is true only when a /Metadata stream EXISTS but
    /// its bytes do not parse as XML — the caller (pellucid scan) reports that on the row instead
    /// of silently treating it as "claims nothing". It is false when there is simply no metadata,
    /// or when the metadata parses but names none of the three supported schemas.</summary>
    public static ConformanceProfile? Read(PdfDocument document, out bool xmpUnreadable)
    {
        ArgumentNullException.ThrowIfNull(document);
        xmpUnreadable = false;

        PdfStream? metadata;
        try
        {
            metadata = document.GetCatalog()?.GetMetadata();
        }
        catch
        {
            // Never throw on bad document structure either; absent-metadata semantics apply.
            return null;
        }

        if (metadata is null)
            return null;

        byte[] xmpBytes;
        try
        {
            xmpBytes = metadata.GetDecodedData(document.Decryptor);
        }
        catch
        {
            // The stream exists but couldn't be decoded (bad filter/decrypt) -- unreadable, not "no claim".
            xmpUnreadable = true;
            return null;
        }

        if (!IsWellFormedXml(xmpBytes))
        {
            xmpUnreadable = true;
            return null;
        }

        // XmpPacket.Parse is itself tolerant of malformed XML (returns an empty packet rather than
        // throwing), which is why the well-formedness check above runs first: it is the only way to
        // distinguish "parsed fine, no supported schema" (xmpUnreadable = false) from "the bytes
        // were not XML at all" (xmpUnreadable = true).
        XmpPacket packet = XmpPacket.Parse(xmpBytes);

        if (ReadPdfAClaim(packet) is { } pdfA)
            return pdfA;
        if (ReadPdfXClaim(packet))
            return ConformanceProfile.PdfX4;
        if (ReadPdfUaClaim(packet))
            return ConformanceProfile.PdfUA1;

        return null;
    }

    private static ConformanceProfile? ReadPdfAClaim(XmpPacket packet)
    {
        string? part = packet.Get(PdfaIdNs, "part")?.Value?.Trim();
        string? conformance = packet.Get(PdfaIdNs, "conformance")?.Value?.Trim();
        return part switch
        {
            "3" when conformance == "B" => ConformanceProfile.PdfA3b,
            "2" when conformance == "B" => ConformanceProfile.PdfA2b,
            "2" when conformance is "U" or "A" => ConformanceProfile.PdfA2u,
            _ => null,
        };
    }

    private static bool ReadPdfXClaim(XmpPacket packet)
    {
        string? version = packet.Get(PdfxIdNs, PdfxVersionProperty)?.Value?.Trim();
        return version is not null && version.StartsWith("PDF/X-4", StringComparison.Ordinal);
    }

    private static bool ReadPdfUaClaim(XmpPacket packet) =>
        packet.Get(PdfUaIdNs, "part")?.Value?.Trim() == "1";

    // Tolerant well-formedness probe -- mirrors the BOM handling and lenient XmlReaderSettings
    // XmpPacket.Parse itself uses, but (unlike that method) reports success/failure instead of
    // silently degrading to an empty packet, so the caller can tell "no claim" apart from
    // "unreadable metadata".
    private static bool IsWellFormedXml(byte[] xmpBytes)
    {
        if (xmpBytes is null || xmpBytes.Length == 0)
            return false;

        ReadOnlySpan<byte> span = xmpBytes;
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
            xmpBytes = xmpBytes[3..];

        try
        {
            using var reader = new StringReader(Encoding.UTF8.GetString(xmpBytes));
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                IgnoreWhitespace = false,
                ConformanceLevel = ConformanceLevel.Document,
            };
            using XmlReader xr = XmlReader.Create(reader, settings);
            XDocument.Load(xr);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
