using PdfLibrary.Builder;
using PdfLibrary.Conformance;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using System.Text;
using Xunit;

namespace PdfLibrary.Tests.Integration;

public class PdfA3AuthoringIntegrationTests
{
    // Repo-relative fixture, reached the same way XmpAndOutputIntentAuthoringTests.IccProfilePath
    // reaches an out-of-project source file: from the test binary's output directory, not the
    // process's current directory (which dotnet test does not guarantee to be the output directory).
    private static readonly string IccProfilePath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..",
        "PdfLibrary", "Rendering", "Icc", "Profiles", "SWOP_TR003_coated_3.icc"));

    // PDF/A identification + a Factur-X-shaped extension-schema block: the pdfaExtension
    // structure is exactly what SetRawXmp exists for (the XmpPacket model cannot express it).
    private static byte[] PdfA3Packet(string title) => Encoding.UTF8.GetBytes(
        "<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n" +
        "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n" +
        "  <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n" +
        "    <rdf:Description xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\" rdf:about=\"\">\n" +
        "      <pdfaid:part>3</pdfaid:part>\n" +
        "      <pdfaid:conformance>B</pdfaid:conformance>\n" +
        "    </rdf:Description>\n" +
        "    <rdf:Description xmlns:dc=\"http://purl.org/dc/elements/1.1/\" rdf:about=\"\">\n" +
        "      <dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">" + title + "</rdf:li></rdf:Alt></dc:title>\n" +
        "    </rdf:Description>\n" +
        "    <rdf:Description xmlns:pdfaExtension=\"http://www.aiim.org/pdfa/ns/extension/\"\n" +
        "        xmlns:pdfaSchema=\"http://www.aiim.org/pdfa/ns/schema#\"\n" +
        "        xmlns:pdfaProperty=\"http://www.aiim.org/pdfa/ns/property#\" rdf:about=\"\">\n" +
        "      <pdfaExtension:schemas>\n" +
        "        <rdf:Bag>\n" +
        "          <rdf:li rdf:parseType=\"Resource\">\n" +
        "            <pdfaSchema:schema>Factur-X PDFA Extension Schema</pdfaSchema:schema>\n" +
        "            <pdfaSchema:namespaceURI>urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#</pdfaSchema:namespaceURI>\n" +
        "            <pdfaSchema:prefix>fx</pdfaSchema:prefix>\n" +
        "            <pdfaSchema:property>\n" +
        "              <rdf:Seq>\n" +
        "                <rdf:li rdf:parseType=\"Resource\">\n" +
        "                  <pdfaProperty:name>DocumentFileName</pdfaProperty:name>\n" +
        "                  <pdfaProperty:valueType>Text</pdfaProperty:valueType>\n" +
        "                  <pdfaProperty:category>external</pdfaProperty:category>\n" +
        "                  <pdfaProperty:description>The name of the embedded XML document</pdfaProperty:description>\n" +
        "                </rdf:li>\n" +
        "              </rdf:Seq>\n" +
        "            </pdfaSchema:property>\n" +
        "          </rdf:li>\n" +
        "        </rdf:Bag>\n" +
        "      </pdfaExtension:schemas>\n" +
        "    </rdf:Description>\n" +
        "    <rdf:Description xmlns:fx=\"urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#\" rdf:about=\"\">\n" +
        "      <fx:DocumentFileName>factur-x.xml</fx:DocumentFileName>\n" +
        "    </rdf:Description>\n" +
        "  </rdf:RDF>\n" +
        "</x:xmpmeta>\n" +
        "<?xpacket end=\"w\"?>");

    [Fact]
    public void Builder_Plus_Authoring_Apis_Produce_A_Preflight_Clean_PdfA3b()
    {
        var ms = new MemoryStream();
        new PdfDocumentBuilder().AddPage(_ => { }).Save(ms);
        ms.Position = 0;

        byte[] icc = File.ReadAllBytes(IccProfilePath);

        byte[] result;
        using (PdfDocumentEditor editor = PdfDocumentEditor.Open(ms, leaveOpen: true))
        {
            editor.AddEmbeddedFile(new PdfEmbeddedFileSpec
            {
                Name = "factur-x.xml",
                Data = "<invoice/>"u8.ToArray(),
                MimeType = "text/xml",
                ModificationDate = DateTimeOffset.UtcNow,
                Relationship = PdfAfRelationship.Data,
                AssociateWithDocument = true,
            });
            editor.AddOutputIntent(icc, "CGATS TR 003");
            editor.Metadata.SetRawXmp(PdfA3Packet("Integration invoice"));
            var outMs = new MemoryStream();
            editor.Save(outMs);
            result = outMs.ToArray();
        }

        PreflightResult preflight = Preflighter.Check(result, ConformanceProfile.PdfA3b);
        Assert.True(preflight.Conforms,
            "PDF/A-3B preflight errors:\n" + string.Join("\n", preflight.Errors.Select(e => e.ToString())));
    }
}
