using System.Text;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Structure;

/// <summary>
/// The load stream is both the parser's lazy object source and, optionally, an owned resource.
/// Keeping those responsibilities separate is required for forward indirect stream lengths.
/// </summary>
public sealed class PdfDocumentStreamLifetimeTests
{
    [Fact]
    public void LeaveOpen_stream_remains_available_for_lazy_resolution_and_caller_ownership()
    {
        using var source = new MemoryStream(PdfWithForwardIndirectEmbeddedFileLength());
        PdfDocument document = PdfDocument.Load(source, leaveOpen: true);

        var embedded = Assert.Single(document.GetEmbeddedFiles());
        Assert.Equal("invoice.xml", embedded.FileName);
        Assert.Equal("text/xml", embedded.MimeType);
        Assert.Equal("payload", Encoding.ASCII.GetString(Assert.IsType<byte[]>(embedded.GetDataBytes())));

        document.Dispose();

        Assert.True(source.CanRead);
        source.Position = 0;
        Assert.Equal('%', source.ReadByte());
    }

    [Fact]
    public void Owned_stream_is_disposed_with_document()
    {
        var source = new MemoryStream(PdfWithForwardIndirectEmbeddedFileLength());
        PdfDocument document = PdfDocument.Load(source, leaveOpen: false);

        Assert.Equal("payload", Encoding.ASCII.GetString(
            Assert.IsType<byte[]>(Assert.Single(document.GetEmbeddedFiles()).GetDataBytes())));

        document.Dispose();

        Assert.False(source.CanRead);
    }

    private static byte[] PdfWithForwardIndirectEmbeddedFileLength()
    {
        const string payload = "payload";
        var bytes = new List<byte>();
        var offsets = new Dictionary<int, int>();

        void Append(string value) => bytes.AddRange(Encoding.ASCII.GetBytes(value));
        void StartObject(int number)
        {
            offsets[number] = bytes.Count;
            Append($"{number} 0 obj\n");
        }

        Append("%PDF-1.7\n");

        StartObject(1);
        Append("<</Type/Catalog/Names<</EmbeddedFiles 2 0 R>>>>\nendobj\n");

        StartObject(2);
        Append("<</Names[(invoice.xml) 3 0 R]>>\nendobj\n");

        StartObject(3);
        Append("<</Type/Filespec/F(invoice.xml)/UF(invoice.xml)/EF<</F 4 0 R>>>>\nendobj\n");

        StartObject(4);
        Append("<</Type/EmbeddedFile/Subtype/text#2Fxml/Length 5 0 R>>\nstream\n");
        Append(payload);
        Append("\nendstream\nendobj\n");

        // Deliberately follows object 4: parsing its stream must resolve this object on demand.
        StartObject(5);
        Append($"{payload.Length}\nendobj\n");

        int xrefOffset = bytes.Count;
        Append("xref\n0 6\n");
        Append("0000000000 65535 f\r\n");
        for (int number = 1; number <= 5; number++)
            Append($"{offsets[number]:D10} 00000 n\r\n");
        Append("trailer\n<</Size 6/Root 1 0 R>>\n");
        Append($"startxref\n{xrefOffset}\n%%EOF");

        return [.. bytes];
    }
}
