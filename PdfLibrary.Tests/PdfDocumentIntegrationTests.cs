using PdfLibrary.Content;
using PdfLibrary.Document;
using PdfLibrary.Structure;
using Xunit;
using Xunit.Abstractions;

namespace PdfLibrary.Tests;

/// <summary>
/// Integration tests demonstrating complete PDF document loading and text extraction
/// </summary>
public class PdfDocumentIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public PdfDocumentIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void LoadPdf_SimplePdf20File_LoadsSuccessfully()
    {
        string pdfPath = @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\Simple PDF 2.0 file.pdf";

        if (!File.Exists(pdfPath))
        {
            _output.WriteLine($"Skipping test - PDF file not found: {pdfPath}");
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);

        Assert.NotNull(doc);
        Assert.NotNull(doc.XrefTable);
        Assert.NotNull(doc.Trailer);
        _output.WriteLine($"Loaded PDF version: {doc.Version}");
        _output.WriteLine($"Number of objects: {doc.Objects.Count}");
    }

    [Fact]
    public void ExtractText_SimplePdf20File_ExtractsText()
    {
        string pdfPath = @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\Simple PDF 2.0 file.pdf";

        if (!File.Exists(pdfPath))
        {
            _output.WriteLine($"Skipping test - PDF file not found: {pdfPath}");
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);
        int pageCount = doc.GetPageCount();

        _output.WriteLine($"PDF has {pageCount} page(s)");
        Assert.True(pageCount > 0, "PDF should have at least one page");

        PdfPage? page = doc.GetPage(0);
        Assert.NotNull(page);

        string text = page.ExtractText();

        _output.WriteLine($"Extracted text length: {text.Length} characters");
        _output.WriteLine($"Extracted text: {text}");

        Assert.NotEmpty(text);
    }

    [Fact]
    public void ExtractTextWithFragments_SimplePdf20File_ReturnsPositionInformation()
    {
        string pdfPath = @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\Simple PDF 2.0 file.pdf";

        if (!File.Exists(pdfPath))
        {
            _output.WriteLine($"Skipping test - PDF file not found: {pdfPath}");
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);
        PdfPage? page = doc.GetPage(0);
        Assert.NotNull(page);

        (string text, List<TextFragment> fragments) = page.ExtractTextWithFragments();

        _output.WriteLine($"Extracted text: {text}");
        _output.WriteLine($"Number of text fragments: {fragments.Count}");
        _output.WriteLine("");
        _output.WriteLine("Text fragments with position information:");

        foreach (TextFragment fragment in fragments)
        {
            _output.WriteLine($"  Text: '{fragment.Text}'");
            _output.WriteLine($"  Position: ({fragment.X:F2}, {fragment.Y:F2})");
            _output.WriteLine($"  Font: {fragment.FontName ?? "unknown"} {fragment.FontSize:F1}pt");
            _output.WriteLine("");
        }

        // Verify we got positioning information
        Assert.NotEmpty(text);
        Assert.NotEmpty(fragments);

        // Each fragment should have position information
        foreach (TextFragment fragment in fragments)
        {
            Assert.NotNull(fragment.Text);
            Assert.NotEmpty(fragment.Text);
            // Position should be set (not default 0,0 for all fragments)
        }
    }

    [Fact]
    public void ExtractTextFromMultiplePages_Utf8TestPdf_ExtractsAllPages()
    {
        string pdfPath = @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\pdf20-utf8-test.pdf";

        if (!File.Exists(pdfPath))
        {
            _output.WriteLine($"Skipping test - PDF file not found: {pdfPath}");
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);
        int pageCount = doc.GetPageCount();

        _output.WriteLine($"PDF has {pageCount} page(s)");

        for (int i = 0; i < pageCount; i++)
        {
            PdfPage? page = doc.GetPage(i);
            Assert.NotNull(page);

            (string text, List<TextFragment> fragments) = page.ExtractTextWithFragments();

            _output.WriteLine($"");
            _output.WriteLine($"=== Page {i + 1} ===");
            _output.WriteLine($"Text length: {text.Length} characters");
            _output.WriteLine($"Fragment count: {fragments.Count}");

            if (fragments.Count > 0)
            {
                _output.WriteLine($"First fragment: '{fragments[0].Text}' at ({fragments[0].X:F2}, {fragments[0].Y:F2})");
                if (fragments.Count > 1)
                    _output.WriteLine($"Last fragment: '{fragments[^1].Text}' at ({fragments[^1].X:F2}, {fragments[^1].Y:F2})");
            }
        }
    }

    [Fact]
    public void GetPageDimensions_SimplePdf20File_ReturnsValidDimensions()
    {
        string pdfPath = @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\Simple PDF 2.0 file.pdf";

        if (!File.Exists(pdfPath))
        {
            _output.WriteLine($"Skipping test - PDF file not found: {pdfPath}");
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);
        PdfPage? page = doc.GetPage(0);
        Assert.NotNull(page);

        double width = page.Width;
        double height = page.Height;

        _output.WriteLine($"Page dimensions: {width:F2} x {height:F2} points");
        _output.WriteLine($"Page dimensions: {width / 72:F2} x {height / 72:F2} inches");

        Assert.True(width > 0, "Width should be positive");
        Assert.True(height > 0, "Height should be positive");
    }

    [Fact]
    public void ExtractTextWithFragments_VerifyPositionAccuracy_FragmentsWithinPageBounds()
    {
        string pdfPath = @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\Simple PDF 2.0 file.pdf";

        if (!File.Exists(pdfPath))
        {
            _output.WriteLine($"Skipping test - PDF file not found: {pdfPath}");
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);
        PdfPage? page = doc.GetPage(0);
        Assert.NotNull(page);

        double pageWidth = page.Width;
        double pageHeight = page.Height;

        (string text, List<TextFragment> fragments) = page.ExtractTextWithFragments();

        _output.WriteLine($"Page size: {pageWidth:F2} x {pageHeight:F2}");
        _output.WriteLine($"Checking {fragments.Count} fragments are within page bounds");

        foreach (TextFragment fragment in fragments)
        {
            _output.WriteLine($"  Fragment '{fragment.Text}' at ({fragment.X:F2}, {fragment.Y:F2})");

            // Positions should be within reasonable page bounds
            // (allowing for text that might extend slightly beyond due to transforms)
            Assert.True(fragment.X >= -100, $"X position {fragment.X} too far left");
            Assert.True(fragment.X <= pageWidth + 100, $"X position {fragment.X} too far right");
            Assert.True(fragment.Y >= -100, $"Y position {fragment.Y} too far below");
            Assert.True(fragment.Y <= pageHeight + 100, $"Y position {fragment.Y} too far above");
        }
    }
}
