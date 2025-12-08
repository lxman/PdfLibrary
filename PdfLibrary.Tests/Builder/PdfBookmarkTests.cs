using PdfLibrary.Builder;
using PdfLibrary.Builder.Bookmark;
using PdfLibrary.Builder.Page;

namespace PdfLibrary.Tests.Builder;

public class PdfBookmarkTests
{
    [Fact]
    public void AddBookmark_CreatesBookmarkWithTitle()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page 1", 100, 700))
            .AddBookmark("Chapter 1", 0);

        // Assert - verify we can chain and build
        byte[] pdf = builder.ToByteArray();
        Assert.True(pdf.Length > 0);
    }

    [Fact]
    public void AddBookmark_WithConfiguration_SetsPageIndex()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page 1", 100, 700))
            .AddPage(p => p.AddText("Page 2", 100, 700))
            .AddBookmark("Chapter 2", b => b.ToPage(1));

        // Assert
        byte[] pdf = builder.ToByteArray();
        Assert.True(pdf.Length > 0);
    }

    [Fact]
    public void AddBookmark_WithOutParameter_ReturnsBookmark()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page 1", 100, 700))
            .AddBookmark("Chapter 1", out PdfBookmark bookmark);

        // Assert
        Assert.NotNull(bookmark);
        Assert.Equal("Chapter 1", bookmark.Title);
    }

    [Fact]
    public void AddBookmark_MultipleBookmarks_AllCreated()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page 1", 100, 700))
            .AddPage(p => p.AddText("Page 2", 100, 700))
            .AddPage(p => p.AddText("Page 3", 100, 700))
            .AddBookmark("Chapter 1", 0)
            .AddBookmark("Chapter 2", 1)
            .AddBookmark("Chapter 3", 2);

        // Assert
        byte[] pdf = builder.ToByteArray();
        Assert.True(pdf.Length > 0);
    }

    [Fact]
    public void PdfBookmarkBuilder_ToPage_SetsDestinationPage()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page 1", 100, 700))
            .AddPage(p => p.AddText("Page 2", 100, 700))
            .AddBookmark("Test", out PdfBookmark bookmark, b => b.ToPage(1));

        // Assert
        Assert.Equal(1, bookmark.Destination.PageIndex);
    }

    [Fact]
    public void PdfBookmarkBuilder_FitPage_SetsDestinationType()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page 1", 100, 700))
            .AddBookmark("Test", out PdfBookmark bookmark, b => b.FitPage());

        // Assert
        Assert.Equal(PdfDestinationType.Fit, bookmark.Destination.Type);
    }

    [Fact]
    public void PdfBookmarkBuilder_FitWidth_SetsDestinationType()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page 1", 100, 700))
            .AddBookmark("Test", out PdfBookmark bookmark, b => b.FitWidth(700));

        // Assert
        Assert.Equal(PdfDestinationType.FitH, bookmark.Destination.Type);
        Assert.Equal(700, bookmark.Destination.Top);
    }

    [Fact]
    public void PdfBookmarkBuilder_AtPosition_SetsXYZDestination()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page 1", 100, 700))
            .AddBookmark("Test", out PdfBookmark bookmark, b => b.AtPosition(100, 500, 1.5));

        // Assert
        Assert.Equal(PdfDestinationType.XYZ, bookmark.Destination.Type);
        Assert.Equal(100, bookmark.Destination.Left);
        Assert.Equal(500, bookmark.Destination.Top);
        Assert.Equal(1.5, bookmark.Destination.Zoom);
    }

    [Fact]
    public void PdfBookmarkBuilder_Collapsed_SetsIsOpenFalse()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page 1", 100, 700))
            .AddBookmark("Test", out PdfBookmark bookmark, b => b.Collapsed());

        // Assert
        Assert.False(bookmark.IsOpen);
    }

    [Fact]
    public void PdfBookmarkBuilder_Bold_SetsIsBoldTrue()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page 1", 100, 700))
            .AddBookmark("Test", out PdfBookmark bookmark, b => b.Bold());

        // Assert
        Assert.True(bookmark.IsBold);
    }

    [Fact]
    public void PdfBookmarkBuilder_Italic_SetsIsItalicTrue()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page 1", 100, 700))
            .AddBookmark("Test", out PdfBookmark bookmark, b => b.Italic());

        // Assert
        Assert.True(bookmark.IsItalic);
    }

    [Fact]
    public void PdfBookmarkBuilder_WithColor_SetsTextColor()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page 1", 100, 700))
            .AddBookmark("Test", out PdfBookmark bookmark, b => b.WithColor(PdfColor.Blue));

        // Assert
        Assert.NotNull(bookmark.TextColor);
    }

    [Fact]
    public void PdfBookmarkBuilder_AddChild_CreatesNestedBookmark()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page 1", 100, 700))
            .AddBookmark("Parent", out PdfBookmark parent, b => b
                .AddChild("Child 1")
                .AddChild("Child 2"));

        // Assert
        Assert.Equal(2, parent.Children.Count);
        Assert.Equal("Child 1", parent.Children[0].Title);
        Assert.Equal("Child 2", parent.Children[1].Title);
    }

    [Fact]
    public void PdfBookmarkBuilder_AddChild_WithConfiguration()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page 1", 100, 700))
            .AddPage(p => p.AddText("Page 2", 100, 700))
            .AddBookmark("Parent", out PdfBookmark parent, b => b
                .ToPage(0)
                .AddChild("Child", c => c.ToPage(1).Bold()));

        // Assert
        Assert.Single(parent.Children);
        Assert.Equal(1, parent.Children[0].Destination.PageIndex);
        Assert.True(parent.Children[0].IsBold);
    }

    [Fact]
    public void Save_WithBookmarks_ProducesValidPdf()
    {
        // Arrange
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Chapter 1", 100, 700))
            .AddPage(p => p.AddText("Chapter 2", 100, 700))
            .AddBookmark("Chapter 1", 0)
            .AddBookmark("Chapter 2", 1);

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
    public void Save_WithBookmarks_ContainsOutlines()
    {
        // Arrange
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Test", 100, 700))
            .AddBookmark("Test Bookmark", 0);

        // Act
        byte[] pdfData = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdfData);

        // Assert
        Assert.Contains("/Outlines", pdfContent);
        Assert.Contains("/Type /Outlines", pdfContent);
        Assert.Contains("Test Bookmark", pdfContent);
    }

    [Fact]
    public void Save_WithBookmarks_ContainsPageModeUseOutlines()
    {
        // Arrange
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Test", 100, 700))
            .AddBookmark("Bookmark", 0);

        // Act
        byte[] pdfData = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdfData);

        // Assert
        Assert.Contains("/PageMode /UseOutlines", pdfContent);
    }

    [Fact]
    public void Save_WithNestedBookmarks_ContainsHierarchy()
    {
        // Arrange
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Content", 100, 700))
            .AddBookmark("Parent", b => b
                .AddChild("Child 1")
                .AddChild("Child 2"));

        // Act
        byte[] pdfData = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdfData);

        // Assert
        Assert.Contains("Parent", pdfContent);
        Assert.Contains("Child 1", pdfContent);
        Assert.Contains("Child 2", pdfContent);
        Assert.Contains("/First", pdfContent);
        Assert.Contains("/Last", pdfContent);
    }

    [Fact]
    public void Save_WithBoldBookmark_ContainsFlagsEntry()
    {
        // Arrange
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Content", 100, 700))
            .AddBookmark("Bold Bookmark", b => b.Bold());

        // Act
        byte[] pdfData = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdfData);

        // Assert
        Assert.Contains("/F 2", pdfContent); // Bold flag = 2
    }

    [Fact]
    public void Save_WithItalicBookmark_ContainsFlagsEntry()
    {
        // Arrange
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Content", 100, 700))
            .AddBookmark("Italic Bookmark", b => b.Italic());

        // Act
        byte[] pdfData = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdfData);

        // Assert
        Assert.Contains("/F 1", pdfContent); // Italic flag = 1
    }

    [Fact]
    public void Save_WithBoldItalicBookmark_ContainsCombinedFlags()
    {
        // Arrange
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Content", 100, 700))
            .AddBookmark("Bold Italic", b => b.Bold().Italic());

        // Act
        byte[] pdfData = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdfData);

        // Assert
        Assert.Contains("/F 3", pdfContent); // Bold + Italic = 2 + 1 = 3
    }

    [Fact]
    public void Save_WithColoredBookmark_ContainsColorArray()
    {
        // Arrange
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Content", 100, 700))
            .AddBookmark("Red Bookmark", b => b.WithColor(PdfColor.Red));

        // Act
        byte[] pdfData = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdfData);

        // Assert
        Assert.Contains("/C [", pdfContent);
    }

    [Fact]
    public void Save_WithFitDestination_ContainsFitOperator()
    {
        // Arrange
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Content", 100, 700))
            .AddBookmark("Fit Page", b => b.FitPage());

        // Act
        byte[] pdfData = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdfData);

        // Assert
        Assert.Contains("/Fit]", pdfContent);
    }

    [Fact]
    public void Save_WithFitWidthDestination_ContainsFitHOperator()
    {
        // Arrange
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Content", 100, 700))
            .AddBookmark("Fit Width", b => b.FitWidth(700));

        // Act
        byte[] pdfData = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdfData);

        // Assert
        Assert.Contains("/FitH", pdfContent);
    }

    [Fact]
    public void Save_WithXYZDestination_ContainsXYZOperator()
    {
        // Arrange
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Content", 100, 700))
            .AddBookmark("Position", b => b.AtPosition(100, 500, 1.0));

        // Act
        byte[] pdfData = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdfData);

        // Assert
        Assert.Contains("/XYZ", pdfContent);
    }

    [Fact]
    public void Save_WithCollapsedBookmark_ContainsNegativeCount()
    {
        // Arrange
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Content", 100, 700))
            .AddBookmark("Parent", b => b
                .Collapsed()
                .AddChild("Child 1")
                .AddChild("Child 2"));

        // Act
        byte[] pdfData = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdfData);

        // Assert
        Assert.Contains("/Count -2", pdfContent); // Negative count = collapsed
    }

    [Fact]
    public void PageContent_WithoutBookmarks_StillWorks()
    {
        // Arrange & Act - document without bookmarks
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("No bookmarks here", 100, 700));

        byte[] pdfData = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdfData);

        // Assert
        Assert.NotNull(pdfData);
        Assert.True(pdfData.Length > 0);
        Assert.DoesNotContain("/Outlines", pdfContent);
    }

    [Fact]
    public void AddBookmark_DeepNesting_Works()
    {
        // Arrange & Act - 3 levels deep
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Content", 100, 700))
            .AddBookmark("Level 1", b => b
                .AddChild("Level 2", c => c
                    .AddChild("Level 3")));

        byte[] pdfData = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdfData);

        // Assert
        Assert.Contains("Level 1", pdfContent);
        Assert.Contains("Level 2", pdfContent);
        Assert.Contains("Level 3", pdfContent);
    }

    [Fact]
    public void PdfDestinationType_AllTypesSupported()
    {
        // Verify all destination types are defined
        Assert.Equal(8, Enum.GetValues<PdfDestinationType>().Length);
    }
}
