using System.Linq;
using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Test helper: attaches a valid PDF/A XMP <c>/Metadata</c> stream to a loaded document. The builder
/// emits no XMP, so builder-produced fixtures need one to satisfy the metadata rules (slice 3+).
/// </summary>
internal static class ConformanceXmp
{
    /// <summary>A minimal, well-formed XMP packet declaring pdfaid part 2, conformance B.</summary>
    public static byte[] ValidPdfaPacket() => Encoding.UTF8.GetBytes(
        "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\">" +
        "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">" +
        "<rdf:Description rdf:about=\"\" xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\" " +
        "pdfaid:part=\"2\" pdfaid:conformance=\"B\"/></rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>");

    /// <summary>Adds a /Metadata stream to the catalog of a loaded document, pointing at valid PDF/A XMP.</summary>
    public static void AttachValidPdfaMetadata(PdfDocument doc)
    {
        doc.MaterializeAllObjects();
        int objectNumber = doc.Objects.Keys.Max() + 1;
        doc.AddObject(objectNumber, 0, new PdfStream(new PdfDictionary(), ValidPdfaPacket()));
        doc.GetCatalog()!.Dictionary[new PdfName("Metadata")] = new PdfIndirectReference(objectNumber, 0);
    }
}
