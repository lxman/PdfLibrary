using PdfLibrary.Builder;

namespace PdfLibrary.Tests.Builder;

public class PdfAnnotationTests
{
    [Fact]
    public void AddLink_ToPage_CreatesLinkAnnotation()
    {
        // Arrange & Act
        var builder = PdfDocumentBuilder.Create()
            .AddPage(p =>
            {
                p.AddText("Page 1", 100, 700);
                p.AddLink(100, 700, 100, 20, 0);
            });

        // Assert
        byte[] pdf = builder.ToByteArray();
        Assert.True(pdf.Length > 0);
    }

    [Fact]
    public void AddExternalLink_CreatesUriAnnotation()
    {
        // Arrange & Act
        var builder = PdfDocumentBuilder.Create()
            .AddPage(p =>
            {
                p.AddText("Click here", 100, 700);
                p.AddExternalLink(100, 700, 100, 20, "https://example.com");
            });

        // Assert
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Contains("/Subtype /Link", pdfContent);
        Assert.Contains("/URI", pdfContent);
        Assert.Contains("example.com", pdfContent);
    }

    [Fact]
    public void AddNote_CreatesTextAnnotation()
    {
        // Arrange & Act
        var builder = PdfDocumentBuilder.Create()
            .AddPage(p =>
            {
                p.AddText("Content", 100, 700);
                p.AddNote(300, 700, "This is a note");
            });

        // Assert
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Contains("/Subtype /Text", pdfContent);
        Assert.Contains("This is a note", pdfContent);
    }

    [Fact]
    public void AddNote_WithConfiguration_SetsIcon()
    {
        // Arrange & Act
        var builder = PdfDocumentBuilder.Create()
            .AddPage(p =>
            {
                p.AddText("Content", 100, 700);
                p.AddNote(300, 700, "Help note", n => n
                    .WithIcon(PdfTextAnnotationIcon.Help)
                    .WithColor(PdfColor.Yellow)
                    .Open());
            });

        // Assert
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Contains("/Name /Help", pdfContent);
        Assert.Contains("/Open true", pdfContent);
    }

    [Fact]
    public void AddHighlight_CreatesHighlightAnnotation()
    {
        // Arrange & Act
        var builder = PdfDocumentBuilder.Create()
            .AddPage(p =>
            {
                p.AddText("Highlighted text", 100, 700);
                p.AddHighlight(100, 695, 100, 20, h => h.WithColor(PdfColor.Yellow));
            });

        // Assert
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Contains("/Subtype /Highlight", pdfContent);
        Assert.Contains("/QuadPoints", pdfContent);
    }

    [Fact]
    public void AddLink_MultiplePages_CorrectDestination()
    {
        // Arrange & Act
        var builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page 1", 100, 700))
            .AddPage(p => p.AddText("Page 2", 100, 700))
            .AddPage(p =>
            {
                p.AddText("Page 3 with link to page 1", 100, 700);
                p.AddLink(100, 700, 200, 20, 0);
            });

        // Assert
        byte[] pdf = builder.ToByteArray();
        Assert.True(pdf.Length > 0);
    }

    [Fact]
    public void AddLink_WithConfiguration_SetsHighlightMode()
    {
        // Arrange & Act
        var builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page 1", 100, 700))
            .AddPage(p =>
            {
                p.AddText("Link", 100, 700);
                p.AddLink(100, 700, 50, 20, 0, l => l
                    .WithHighlight(PdfLinkHighlightMode.Push)
                    .WithBorder(1));
            });

        // Assert
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Contains("/H /P", pdfContent);
    }

    [Fact]
    public void MultipleAnnotations_OnSamePage_AllWritten()
    {
        // Arrange & Act
        var builder = PdfDocumentBuilder.Create()
            .AddPage(p =>
            {
                p.AddText("Content", 100, 700);
                p.AddNote(50, 700, "Note 1");
                p.AddNote(100, 700, "Note 2");
                p.AddExternalLink(200, 700, 100, 20, "https://test.com");
            });

        // Assert
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Contains("Note 1", pdfContent);
        Assert.Contains("Note 2", pdfContent);
        Assert.Contains("/URI", pdfContent);
    }

    [Fact]
    public void AnnotationWithBorder_WritesBorderArray()
    {
        // Arrange & Act
        var builder = PdfDocumentBuilder.Create()
            .AddPage(p =>
            {
                p.AddText("Link", 100, 700);
                p.AddLink(100, 700, 50, 20, 0, l => l.WithBorder(2));
            });

        // Assert
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Contains("/Border", pdfContent);
    }

    [Fact]
    public void AnnotationPrintable_SetsPrintFlag()
    {
        // Arrange & Act
        var builder = PdfDocumentBuilder.Create()
            .AddPage(p =>
            {
                p.AddText("Content", 100, 700);
                p.AddNote(300, 700, "Printable note", n => n.Printable());
            });

        // Assert
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Contains("/F 4", pdfContent); // Print flag = 4
    }

    [Fact]
    public void Save_WithAnnotations_ProducesValidPdf()
    {
        // Arrange
        var builder = PdfDocumentBuilder.Create()
            .AddPage(p =>
            {
                p.AddText("Page with annotations", 100, 700);
                p.AddNote(50, 700, "A note");
                p.AddExternalLink(100, 680, 100, 15, "https://example.org");
            });

        // Act
        byte[] pdfData = builder.ToByteArray();

        // Assert
        Assert.NotNull(pdfData);
        Assert.True(pdfData.Length > 0);

        // Verify PDF header
        string header = System.Text.Encoding.ASCII.GetString(pdfData, 0, 8);
        Assert.StartsWith("%PDF-", header);
    }

    [Fact]
    public void PageWithAnnotationsAndFormFields_BothInAnnotsArray()
    {
        // Arrange & Act
        var builder = PdfDocumentBuilder.Create()
            .AddPage(p =>
            {
                p.AddTextField("name", 100, 700, 200, 30);
                p.AddNote(300, 700, "Fill this form");
            })
            .WithAcroForm(f => f.SetNeedAppearances(true));

        // Assert
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Contains("/Annots", pdfContent);
        Assert.Contains("/FT /Tx", pdfContent);  // Text field
        Assert.Contains("/Subtype /Text", pdfContent);  // Text annotation
    }

    [Fact]
    public void HighlightAnnotation_MultipleRegions_WritesAllQuadPoints()
    {
        // Arrange & Act
        var builder = PdfDocumentBuilder.Create()
            .AddPage(p =>
            {
                p.AddText("Line 1", 100, 700);
                p.AddText("Line 2", 100, 680);
                p.AddHighlight(100, 695, 100, 20, h => h
                    .WithColor(PdfColor.Green)
                    .AddRegion(100, 675, 200, 695));
            });

        // Assert
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Contains("/QuadPoints", pdfContent);
    }

    [Fact]
    public void TextAnnotation_AllIcons_Valid()
    {
        // Arrange - test all icon types
        var icons = new[] {
            PdfTextAnnotationIcon.Comment,
            PdfTextAnnotationIcon.Key,
            PdfTextAnnotationIcon.Note,
            PdfTextAnnotationIcon.Help,
            PdfTextAnnotationIcon.NewParagraph,
            PdfTextAnnotationIcon.Paragraph,
            PdfTextAnnotationIcon.Insert
        };

        foreach (var icon in icons)
        {
            var builder = PdfDocumentBuilder.Create()
                .AddPage(p => p.AddNote(100, 700, $"Icon: {icon}", n => n.WithIcon(icon)));

            // Act
            byte[] pdf = builder.ToByteArray();

            // Assert
            Assert.True(pdf.Length > 0, $"Failed for icon: {icon}");
        }
    }

    [Fact]
    public void LinkHighlightModes_AllValid()
    {
        // Arrange - test all highlight modes
        var modes = new[] {
            PdfLinkHighlightMode.None,
            PdfLinkHighlightMode.Invert,
            PdfLinkHighlightMode.Outline,
            PdfLinkHighlightMode.Push
        };

        foreach (var mode in modes)
        {
            var builder = PdfDocumentBuilder.Create()
                .AddPage(p => p.AddText("Page 1", 100, 700))
                .AddPage(p => p.AddLink(100, 700, 100, 20, 0, l => l.WithHighlight(mode)));

            // Act
            byte[] pdf = builder.ToByteArray();

            // Assert
            Assert.True(pdf.Length > 0, $"Failed for mode: {mode}");
        }
    }
}
