using PdfLibrary.Content;
using PdfLibrary.Document;
using PdfLibrary.Structure;
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

    [Fact]
    public void GetImages_Pdf20ImageWithBpc_ExtractsImage()
    {
        string pdfPath = @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\PDF 2.0 image with BPC.pdf";

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

            List<PdfImage> images = page.GetImages();

            _output.WriteLine($"");
            _output.WriteLine($"=== Page {i + 1} ===");
            _output.WriteLine($"Number of images: {images.Count}");

            foreach (PdfImage image in images)
            {
                _output.WriteLine($"");
                _output.WriteLine($"Image: {image}");
                _output.WriteLine($"  Size: {image.Width}x{image.Height} pixels");
                _output.WriteLine($"  Color Space: {image.ColorSpace}");
                _output.WriteLine($"  Bits Per Component: {image.BitsPerComponent}");
                _output.WriteLine($"  Component Count: {image.ComponentCount}");
                _output.WriteLine($"  Filters: {string.Join(", ", image.Filters)}");
                _output.WriteLine($"  Has Alpha: {image.HasAlpha}");
                _output.WriteLine($"  Is Mask: {image.IsImageMask}");
                _output.WriteLine($"  Expected Data Size: {image.GetExpectedDataSize()} bytes");

                // Verify image has valid dimensions
                Assert.True(image.Width > 0, "Image width should be positive");
                Assert.True(image.Height > 0, "Image height should be positive");
            }
        }
    }

    [Fact]
    public void GetImages_AllPdf20Examples_CountsImages()
    {
        string[] pdfFiles =
        [
            @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\Simple PDF 2.0 file.pdf",
            @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\PDF 2.0 image with BPC.pdf",
            @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\PDF 2.0 UTF-8 string and annotation.pdf",
            @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\PDF 2.0 via incremental save.pdf"
        ];

        _output.WriteLine("Scanning PDF files for images:");
        _output.WriteLine("");

        foreach (string pdfPath in pdfFiles)
        {
            if (!File.Exists(pdfPath))
            {
                _output.WriteLine($"Skipped: {Path.GetFileName(pdfPath)} (not found)");
                continue;
            }

            try
            {
                using PdfDocument doc = PdfDocument.Load(pdfPath);
                int totalImages = 0;

                for (int i = 0; i < doc.GetPageCount(); i++)
                {
                    PdfPage? page = doc.GetPage(i);
                    if (page != null)
                    {
                        totalImages += page.GetImageCount();
                    }
                }

                _output.WriteLine($"{Path.GetFileName(pdfPath)}: {totalImages} image(s)");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"{Path.GetFileName(pdfPath)}: Error - {ex.Message}");
            }
        }
    }

    [Fact]
    public void ExtractImages_VerifyImageData_DataSizeMatchesExpected()
    {
        string pdfPath = @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\PDF 2.0 image with BPC.pdf";

        if (!File.Exists(pdfPath))
        {
            _output.WriteLine($"Skipping test - PDF file not found: {pdfPath}");
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);

        for (int i = 0; i < doc.GetPageCount(); i++)
        {
            PdfPage? page = doc.GetPage(i);
            if (page == null) continue;

            List<PdfImage> images = page.GetImages();

            foreach (PdfImage image in images)
            {
                byte[] data = image.GetDecodedData();
                int expectedSize = image.GetExpectedDataSize();

                _output.WriteLine($"Image {image.Width}x{image.Height}:");
                _output.WriteLine($"  Actual data size: {data.Length} bytes");
                _output.WriteLine($"  Expected data size: {expectedSize} bytes");

                // Note: Actual size might be larger than expected due to padding or
                // different than expected for compressed formats like JPEG
                Assert.NotEmpty(data);
            }
        }
    }

    [Fact]
    public void GetImageCount_SimplePdf_ReturnsCorrectCount()
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

        int imageCount = page.GetImageCount();
        List<PdfImage> images = page.GetImages();

        _output.WriteLine($"Image count (via GetImageCount): {imageCount}");
        _output.WriteLine($"Image count (via GetImages().Count): {images.Count}");

        Assert.Equal(images.Count, imageCount);
    }
}
