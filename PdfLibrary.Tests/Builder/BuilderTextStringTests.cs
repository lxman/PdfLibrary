using PdfLibrary.Builder;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Builder;

public class BuilderTextStringTests
{
    [Fact]
    public void Builder_NonAsciiTitle_RoundTripsThroughParser()
    {
        const string title = "Café — 日本語";

        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .WithMetadata(m => m.SetTitle(title))
            .AddPage(p => p.AddText("Hello", 100, 700));

        string tempFile = Path.GetTempFileName();
        try
        {
            new PdfDocumentWriter().Write(builder, tempFile);
            using PdfDocument doc = PdfDocument.Load(tempFile);
            string? roundTripped = doc.Edit().Metadata.Title;
            Assert.Equal(title, roundTripped);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void Builder_NonAsciiAuthor_RoundTripsThroughParser()
    {
        const string author = "著者 Müller";

        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .WithMetadata(m => m.SetAuthor(author))
            .AddPage(p => p.AddText("Hello", 100, 700));

        string tempFile = Path.GetTempFileName();
        try
        {
            new PdfDocumentWriter().Write(builder, tempFile);
            using PdfDocument doc = PdfDocument.Load(tempFile);
            string? roundTripped = doc.Edit().Metadata.Author;
            Assert.Equal(author, roundTripped);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
