using PdfLibrary.Builder;

namespace PdfLibrary.Tests.Builder;

public class PdfPageLabelTests
{
    [Fact]
    public void SetPageLabels_DecimalStyle_WritesPageLabels()
    {
        // Arrange & Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page 1", 100, 700))
            .AddPage(p => p.AddText("Page 2", 100, 700))
            .SetPageLabels(0, l => l.Decimal());

        // Assert
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Contains("/PageLabels", pdfContent);
        Assert.Contains("/Nums", pdfContent);
        Assert.Contains("/S /D", pdfContent);
    }

    [Fact]
    public void SetPageLabels_RomanStyle_WritesCorrectStyleCode()
    {
        // Arrange & Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Preface", 100, 700))
            .SetPageLabels(0, l => l.LowercaseRoman());

        // Assert
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Contains("/S /r", pdfContent);  // lowercase roman
    }

    [Fact]
    public void SetPageLabels_UppercaseRoman_WritesCorrectStyleCode()
    {
        // Arrange & Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Chapter", 100, 700))
            .SetPageLabels(0, l => l.UppercaseRoman());

        // Assert
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Contains("/S /R", pdfContent);  // uppercase roman
    }

    [Fact]
    public void SetPageLabels_LetterStyles_WriteCorrectCodes()
    {
        // Lowercase letters
        PdfDocumentBuilder builder1 = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Appendix", 100, 700))
            .SetPageLabels(0, l => l.LowercaseLetters());

        byte[] pdf1 = builder1.ToByteArray();
        string content1 = System.Text.Encoding.ASCII.GetString(pdf1);
        Assert.Contains("/S /a", content1);

        // Uppercase letters
        PdfDocumentBuilder builder2 = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Appendix", 100, 700))
            .SetPageLabels(0, l => l.UppercaseLetters());

        byte[] pdf2 = builder2.ToByteArray();
        string content2 = System.Text.Encoding.ASCII.GetString(pdf2);
        Assert.Contains("/S /A", content2);
    }

    [Fact]
    public void SetPageLabels_WithPrefix_WritesPrefix()
    {
        // Arrange & Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page", 100, 700))
            .SetPageLabels(0, l => l.Decimal().WithPrefix("Chapter-"));

        // Assert
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Contains("/P (Chapter-)", pdfContent);
    }

    [Fact]
    public void SetPageLabels_WithStartNumber_WritesStEntry()
    {
        // Arrange & Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page", 100, 700))
            .SetPageLabels(0, l => l.Decimal().StartingAt(5));

        // Assert
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Contains("/St 5", pdfContent);
    }

    [Fact]
    public void SetPageLabels_NoNumbering_OmitsStyleEntry()
    {
        // Arrange & Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Cover", 100, 700))
            .SetPageLabels(0, l => l.NoNumbering().WithPrefix("Cover"));

        // Assert
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Contains("/P (Cover)", pdfContent);
        // Style should not be present for NoNumbering
        Assert.DoesNotContain("/S /D", pdfContent);
    }

    [Fact]
    public void SetPageLabels_MultipleRanges_AllWritten()
    {
        // Arrange & Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Cover", 100, 700))       // 0
            .AddPage(p => p.AddText("Preface i", 100, 700))   // 1
            .AddPage(p => p.AddText("Preface ii", 100, 700))  // 2
            .AddPage(p => p.AddText("Chapter 1", 100, 700))   // 3
            .AddPage(p => p.AddText("Chapter 2", 100, 700))   // 4
            .SetPageLabels(0, l => l.NoNumbering().WithPrefix("Cover"))
            .SetPageLabels(1, l => l.LowercaseRoman())
            .SetPageLabels(3, l => l.Decimal());

        // Assert
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Contains("/PageLabels", pdfContent);
        Assert.Contains("0 <<", pdfContent);  // Cover
        Assert.Contains("1 <<", pdfContent);  // Roman
        Assert.Contains("3 <<", pdfContent);  // Decimal
    }

    [Fact]
    public void SetPageLabels_ShortOverload_SetsDecimalWithOptions()
    {
        // Arrange & Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page 1", 100, 700))
            .SetPageLabels(0, 1, "A-");  // startNumber=1, prefix="A-"

        // Assert
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Contains("/S /D", pdfContent);
        Assert.Contains("/P (A-)", pdfContent);
    }

    [Fact]
    public void SetPageLabels_OutParameter_ReturnsRange()
    {
        // Arrange & Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page 1", 100, 700))
            .SetPageLabels(0, out PdfPageLabelRange range, l => l.Decimal().WithPrefix("Test-"));

        // Assert
        Assert.NotNull(range);
        Assert.Equal(0, range.StartPageIndex);
        Assert.Equal(PdfPageLabelStyle.Decimal, range.Style);
        Assert.Equal("Test-", range.Prefix);
    }

    [Fact]
    public void Save_WithPageLabels_ProducesValidPdf()
    {
        // Arrange
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Introduction", 100, 700))
            .AddPage(p => p.AddText("Chapter 1", 100, 700))
            .AddPage(p => p.AddText("Chapter 2", 100, 700))
            .SetPageLabels(0, l => l.LowercaseRoman())
            .SetPageLabels(1, l => l.Decimal());

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
    public void PageWithoutLabels_NoPageLabelsInCatalog()
    {
        // Arrange & Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Simple page", 100, 700));

        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);

        // Assert
        Assert.DoesNotContain("/PageLabels", pdfContent);
    }

    [Fact]
    public void SetPageLabels_CombinedWithBookmarks_BothWork()
    {
        // Arrange & Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Cover", 100, 700))
            .AddPage(p => p.AddText("Chapter 1", 100, 700))
            .SetPageLabels(0, l => l.NoNumbering().WithPrefix("Cover"))
            .SetPageLabels(1, l => l.Decimal())
            .AddBookmark("Cover", 0)
            .AddBookmark("Chapter 1", 1);

        // Assert
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Contains("/PageLabels", pdfContent);
        Assert.Contains("/Outlines", pdfContent);
    }

    [Fact]
    public void AllPageLabelStyles_AreValid()
    {
        // Verify all enum values are handled
        PdfPageLabelStyle[] styles = Enum.GetValues<PdfPageLabelStyle>();
        Assert.Equal(6, styles.Length);  // None, Decimal, UppercaseRoman, LowercaseRoman, UppercaseLetters, LowercaseLetters

        // Test each style produces valid output
        foreach (PdfPageLabelStyle style in styles)
        {
            PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
                .AddPage(p => p.AddText("Test", 100, 700));

            // Use the builder pattern for each style
            switch (style)
            {
                case PdfPageLabelStyle.None:
                    builder.SetPageLabels(0, l => l.NoNumbering());
                    break;
                case PdfPageLabelStyle.Decimal:
                    builder.SetPageLabels(0, l => l.Decimal());
                    break;
                case PdfPageLabelStyle.UppercaseRoman:
                    builder.SetPageLabels(0, l => l.UppercaseRoman());
                    break;
                case PdfPageLabelStyle.LowercaseRoman:
                    builder.SetPageLabels(0, l => l.LowercaseRoman());
                    break;
                case PdfPageLabelStyle.UppercaseLetters:
                    builder.SetPageLabels(0, l => l.UppercaseLetters());
                    break;
                case PdfPageLabelStyle.LowercaseLetters:
                    builder.SetPageLabels(0, l => l.LowercaseLetters());
                    break;
            }

            byte[] pdf = builder.ToByteArray();
            Assert.True(pdf.Length > 0, $"Failed for style: {style}");
        }
    }

    [Fact]
    public void SetPageLabels_RangesOrderedCorrectly()
    {
        // Arrange - add ranges out of order
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page 1", 100, 700))
            .AddPage(p => p.AddText("Page 2", 100, 700))
            .AddPage(p => p.AddText("Page 3", 100, 700))
            .SetPageLabels(2, l => l.Decimal())  // Added first but should be last
            .SetPageLabels(0, l => l.LowercaseRoman());  // Added second but should be first

        // Act
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);

        // Assert - ranges should be ordered by page index in the PDF
        int pos0 = pdfContent.IndexOf("0 <<", StringComparison.Ordinal);
        int pos2 = pdfContent.IndexOf("2 <<", StringComparison.Ordinal);
        Assert.True(pos0 < pos2, "Page label ranges should be ordered by page index");
    }
}
