using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Parsing;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Parsing;

public class MalformedXrefTests
{
    [Fact]
    public void Traditional_xref_repairs_object_zero_head_under_nonzero_subsection_start()
    {
        PdfXrefTable table = ParseXref(
            "xref\r" +
            "1 3\r" +
            "0000000000 65535 f \r" +
            "0000000009 00000 n \r" +
            "0000000058 00000 n \r" +
            "trailer\r" +
            "<< /Size 3 /Root 1 0 R >>\r" +
            "startxref\r123\r%%EOF\r");

        Assert.False(table.GetEntry(0)!.IsInUse);
        Assert.Equal(9, table.GetEntry(1)!.ByteOffset);
        Assert.Equal(58, table.GetEntry(2)!.ByteOffset);
        Assert.Null(table.GetEntry(3));
    }

    [Fact]
    public void Traditional_xref_preserves_nonzero_subsection_when_declared_range_fits_size()
    {
        PdfXrefTable table = ParseXref(
            "xref\n" +
            "1 2\n" +
            "0000000000 65535 f \n" +
            "0000000042 00000 n \n" +
            "trailer\n" +
            "<< /Size 3 /Root 2 0 R >>\n" +
            "startxref\n123\n%%EOF\n");

        Assert.Null(table.GetEntry(0));
        Assert.False(table.GetEntry(1)!.IsInUse);
        Assert.Equal(42, table.GetEntry(2)!.ByteOffset);
    }

    [Fact]
    public void Traditional_xref_preserves_overflowing_subsection_without_object_zero_head()
    {
        PdfXrefTable table = ParseXref(
            "xref\n" +
            "1 2\n" +
            "0000000000 00001 f \n" +
            "0000000042 00000 n \n" +
            "trailer\n" +
            "<< /Size 2 /Root 1 0 R >>\n" +
            "startxref\n123\n%%EOF\n");

        Assert.Null(table.GetEntry(0));
        Assert.Equal(1, table.GetEntry(1)!.GenerationNumber);
        Assert.Equal(42, table.GetEntry(2)!.ByteOffset);
    }

    [Fact]
    public void Document_loads_cr_only_pdf_with_misnumbered_xref_subsection()
    {
        using PdfDocument document = PdfDocument.Load(new MemoryStream(BuildMalformedPdf()));

        Assert.Equal(1, document.PageCount);
        Assert.Equal("Catalog", ((PdfName)document.CatalogDictionary![PdfName.TypeName]).Value);
    }

    private static PdfXrefTable ParseXref(string text)
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(text));
        var parser = new PdfXrefParser(stream);
        PdfXrefParseResult result = parser.Parse();

        // The look-ahead used for validation must leave the trailer available to its normal caller.
        var trailerParser = new PdfTrailerParser(stream);
        (PdfTrailer trailer, _) = trailerParser.Parse();
        Assert.NotNull(trailer.Size);

        return result.Table;
    }

    private static byte[] BuildMalformedPdf()
    {
        var bytes = new List<byte>();
        var offsets = new int[4];

        void Append(string value) => bytes.AddRange(Encoding.ASCII.GetBytes(value));
        void AddObject(int number, string body)
        {
            offsets[number] = bytes.Count;
            Append($"{number} 0 obj\r{body}\rendobj\r");
        }

        Append("%PDF-1.3\r");
        AddObject(1, "<< /Type /Catalog /Pages 2 0 R >>");
        AddObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        AddObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 10 10] >>");

        int xrefOffset = bytes.Count;
        Append("xref\r1 4\r");
        Append("0000000000 65535 f \r");
        for (var objectNumber = 1; objectNumber <= 3; objectNumber++)
            Append($"{offsets[objectNumber]:D10} 00000 n \r");
        Append($"trailer\r<< /Size 4 /Root 1 0 R >>\rstartxref\r{xrefOffset}\r%%EOF\r");

        return bytes.ToArray();
    }
}
