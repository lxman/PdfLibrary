using System.Globalization;
using PdfLibrary.Builder;
using PdfLibrary.Document;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests;

/// <summary>
/// PDF number syntax is fixed `.`-decimal (locale-independent). Creating and parsing PDFs must therefore
/// be culture-invariant. These tests run the create→parse round-trip under cultures whose decimal separator
/// is not `.` (de-DE uses `,` + `.` grouping; fr-FR uses `,` + space grouping), which previously broke both
/// the builder's number emission and the object-level real parser.
/// </summary>
public class CultureInvarianceTests
{
    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void CreateAndParse_UnderNonInvariantCulture_RoundTrips(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            // WRITE path: fractional MediaBox → an object-level real; fractional text x → a content-stream real.
            byte[] bytes = PdfDocumentBuilder.Create()
                .AddPage(new PdfSize(611.5, 791.5), p => p.AddText("kultur", 100.25, 700))
                .ToByteArray();

            // READ path: parse it back under the same culture.
            using PdfDocument doc = PdfDocument.Load(new MemoryStream(bytes));
            PdfPage page = doc.GetPage(0)!;

            Assert.Equal(611.5, page.Width, 1);   // object-level real survives (was 61150 / throw under the bug)
            Assert.Equal(791.5, page.Height, 1);
            Assert.Contains("kultur", page.ExtractText());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
