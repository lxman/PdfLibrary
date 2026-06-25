using PdfLibrary;
using PdfLibrary.Parsing;
using PdfLibrary.Security;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests;

public class PdfExceptionTests
{
    // Consumers must be able to reference and catch PDF-specific failures by type.
    [Fact]
    public void PdfErrorTypes_ArePublic()
    {
        Assert.True(typeof(PdfException).IsPublic, "PdfException must be public");
        Assert.True(typeof(PdfParseException).IsPublic, "PdfParseException must be public");
        Assert.True(typeof(PdfSecurityException).IsPublic, "PdfSecurityException must be public");
    }

    [Fact]
    public void PdfErrorTypes_DeriveFromPdfException()
    {
        Assert.True(typeof(PdfException).IsAssignableFrom(typeof(PdfParseException)));
        Assert.True(typeof(PdfException).IsAssignableFrom(typeof(PdfSecurityException)));
    }

    [Fact]
    public void Load_MalformedInput_IsCatchableAsPdfException()
    {
        using var ms = new MemoryStream("not a pdf at all"u8.ToArray());
        Assert.ThrowsAny<PdfException>(() => PdfDocument.Load(ms));
    }
}
