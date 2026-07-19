using PdfLibrary.Builder;
using PdfLibrary.Document;
using PdfLibrary.Editing;
using PdfLibrary.Metadata;
using PdfLibrary.Structure;
using System.Text;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class XmpAndOutputIntentAuthoringTests
{
    private const string PdfaIdNs = "http://www.aiim.org/pdfa/ns/id/";

    // Repo-relative fixture, reached the same way FontSubsetIntegrationTests.AllmandPath() reaches
    // an out-of-project source file: from the test binary's output directory, not the process's
    // current directory (which dotnet test does not guarantee to be the output directory).
    private static readonly string IccProfilePath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..",
        "PdfLibrary", "Rendering", "Icc", "Profiles", "SWOP_TR003_coated_3.icc"));

    private static MemoryStream OnePagePdf()
    {
        var ms = new MemoryStream();
        new PdfDocumentBuilder().AddPage(_ => { }).Save(ms);
        ms.Position = 0;
        return ms;
    }

    private static byte[] MinimalPacket() => Encoding.UTF8.GetBytes(
        "<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n" +
        "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n" +
        "  <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n" +
        "    <rdf:Description xmlns:pdfaid=\"" + PdfaIdNs + "\" rdf:about=\"\">\n" +
        "      <pdfaid:part>3</pdfaid:part>\n      <pdfaid:conformance>B</pdfaid:conformance>\n" +
        "    </rdf:Description>\n  </rdf:RDF>\n</x:xmpmeta>\n<?xpacket end=\"w\"?>");

    [Fact]
    public void SetRawXmp_Replaces_Metadata_Stream_Verbatim()
    {
        var src = OnePagePdf();
        byte[] saved;
        using (PdfDocumentEditor editor = PdfDocumentEditor.Open(src, leaveOpen: true))
        {
            editor.Metadata.SetRawXmp(MinimalPacket());
            var outMs = new MemoryStream();
            editor.Save(outMs);
            saved = outMs.ToArray();
        }
        using var doc = PdfDocument.Load(new MemoryStream(saved), "");
        XmpProperty? part = doc.Edit().Metadata.Xmp.Get(PdfaIdNs, "part");
        Assert.NotNull(part);
        Assert.Equal("3", part!.Value);
    }

    [Fact]
    public void SetRawXmp_After_Property_Setter_Wins()
    {
        var src = OnePagePdf();
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(src, leaveOpen: true);
        editor.Metadata.Title = "carrier title";   // property setter writes the model-based stream
        editor.Metadata.SetRawXmp(MinimalPacket()); // raw set LAST must win
        var outMs = new MemoryStream();
        editor.Save(outMs);
        using var doc = PdfDocument.Load(new MemoryStream(outMs.ToArray()), "");
        Assert.NotNull(doc.Edit().Metadata.Xmp.Get(PdfaIdNs, "part"));
    }

    [Fact]
    public void AddOutputIntent_RoundTrips_Via_Read_Side()
    {
        byte[] icc = File.ReadAllBytes(IccProfilePath);
        var src = OnePagePdf();
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(src, leaveOpen: true);
        editor.AddOutputIntent(icc, "CGATS TR 003", info: "SWOP");
        var outMs = new MemoryStream();
        editor.Save(outMs);
        using var doc = PdfDocument.Load(new MemoryStream(outMs.ToArray()), "");
        OutputIntentDescriptor intent = Assert.Single(doc.GetOutputIntents());
        Assert.Equal("GTS_PDFA1", intent.Subtype);
        Assert.Equal("CGATS TR 003", intent.OutputConditionIdentifier);
        Assert.Equal("SWOP", intent.Info);
        Assert.Equal(OutputIntentColorSpace.Cmyk, intent.ColorSpace);
        Assert.True(intent.HasDestProfile);
    }

    [Fact]
    public void AddOutputIntent_Rejects_Junk_Icc()
    {
        var src = OnePagePdf();
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(src, leaveOpen: true);
        Assert.Throws<ArgumentException>(() => editor.AddOutputIntent([1, 2, 3], "x"));
    }
}
