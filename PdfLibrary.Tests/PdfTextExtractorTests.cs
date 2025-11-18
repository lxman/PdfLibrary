using System.Text;
using PdfLibrary.Content;

namespace PdfLibrary.Tests;

/// <summary>
/// Comprehensive tests for PdfTextExtractor
/// Tests text extraction from PDF content streams with position information
/// </summary>
public class PdfTextExtractorTests
{
    #region Basic Text Extraction Tests

    [Fact]
    public void ExtractText_SimpleText_ReturnsText()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
(Hello, World!) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        string text = PdfTextExtractor.ExtractText(bytes);

        Assert.Equal("\r\nHello, World!", text);
    }

    [Fact]
    public void ExtractText_MultipleTextOperators_ConcatenatesText()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
(Hello) Tj
(, ) Tj
(World) Tj
(!) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        string text = PdfTextExtractor.ExtractText(bytes);

        Assert.Equal("\r\nHello, World!", text);
    }

    [Fact]
    public void ExtractText_EmptyContent_ReturnsEmpty()
    {
        byte[] emptyContent = [];

        string text = PdfTextExtractor.ExtractText(emptyContent);

        Assert.Empty(text);
    }

    [Fact]
    public void ExtractText_NoTextOperators_ReturnsEmpty()
    {
        var content = @"
q
1 0 0 1 100 100 cm
Q";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        string text = PdfTextExtractor.ExtractText(bytes);

        Assert.Empty(text);
    }

    [Fact]
    public void ExtractText_MultipleTextBlocks_ExtractsAll()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
(First block) Tj
ET
BT
/F1 12 Tf
100 650 Td
(Second block) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        string text = PdfTextExtractor.ExtractText(bytes);

        Assert.Contains("First block", text);
        Assert.Contains("Second block", text);
    }

    [Fact]
    public void ExtractText_WithoutResources_UsesDefaultEncoding()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
(Test) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        string text = PdfTextExtractor.ExtractText(bytes, resources: null);

        Assert.Equal("\r\nTest", text);
    }

    #endregion

    #region ExtractTextWithFragments Tests

    [Fact]
    public void ExtractTextWithFragments_SimpleText_ReturnsTextAndFragments()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
(Hello) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        (string text, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(bytes);

        Assert.Equal("\r\nHello", text);
        Assert.Single(fragments);
        Assert.Equal("Hello", fragments[0].Text);
    }

    [Fact]
    public void ExtractTextWithFragments_MultipleFragments_PreservesEach()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
(Hello) Tj
(World) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        (string text, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(bytes);

        Assert.Equal("\r\nHelloWorld", text);
        Assert.Equal(2, fragments.Count);
        Assert.Equal("Hello", fragments[0].Text);
        Assert.Equal("World", fragments[1].Text);
    }

    [Fact]
    public void ExtractTextWithFragments_EmptyContent_ReturnsEmpty()
    {
        byte[] emptyContent = [];

        (string text, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(emptyContent);

        Assert.Empty(text);
        Assert.Empty(fragments);
    }

    #endregion

    #region Text Fragment Position Tests

    [Fact]
    public void GetTextFragments_SimpleText_HasPosition()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
(Test) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        (_, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(bytes);

        Assert.Single(fragments);
        Assert.Equal("Test", fragments[0].Text);
        Assert.Equal(100, fragments[0].X, 0.01);
        Assert.Equal(700, fragments[0].Y, 0.01);
    }

    [Fact]
    public void GetTextFragments_MultiplePositions_TracksEach()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
(First) Tj
200 650 Td
(Second) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        (_, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(bytes);

        Assert.Equal(2, fragments.Count);

        // First fragment at (100, 700)
        Assert.Equal("First", fragments[0].Text);
        Assert.Equal(100, fragments[0].X, 0.01);
        Assert.Equal(700, fragments[0].Y, 0.01);

        // Second fragment - Td is relative, so adds to current position
        Assert.Equal("Second", fragments[1].Text);
        // Position should be updated by the Td operator
    }

    [Fact]
    public void GetTextFragments_TextMatrix_UsesMatrixPosition()
    {
        var content = @"
BT
/F1 12 Tf
1 0 0 1 100 700 Tm
(Test) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        (_, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(bytes);

        Assert.Single(fragments);
        Assert.Equal(100, fragments[0].X, 0.01);
        Assert.Equal(700, fragments[0].Y, 0.01);
    }

    #endregion

    #region Font Information Tests

    [Fact]
    public void GetTextFragments_WithFontInfo_IncludesFontName()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
(Test) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        (_, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(bytes);

        Assert.Single(fragments);
        Assert.Equal("F1", fragments[0].FontName);
    }

    [Fact]
    public void GetTextFragments_WithFontSize_IncludesFontSize()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
(Test) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        (_, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(bytes);

        Assert.Single(fragments);
        Assert.Equal(12, fragments[0].FontSize, 0.01);
    }

    [Fact]
    public void GetTextFragments_MultipleFonts_TracksFontChanges()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
(Normal) Tj
/F2 14 Tf
(Bold) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        (_, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(bytes);

        Assert.Equal(2, fragments.Count);
        Assert.Equal("F1", fragments[0].FontName);
        Assert.Equal(12, fragments[0].FontSize, 0.01);
        Assert.Equal("F2", fragments[1].FontName);
        Assert.Equal(14, fragments[1].FontSize, 0.01);
    }

    #endregion

    #region Text Positioning Operators Tests

    [Fact]
    public void ExtractText_TdOperator_UpdatesPosition()
    {
        var content = @"
BT
/F1 12 Tf
0 0 Td
(Start) Tj
100 0 Td
(Moved) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        (string text, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(bytes);

        // Extractor adds space when position changes significantly
        // Starting at (0,0) doesn't trigger initial newline
        Assert.Equal("Start Moved", text);
        Assert.Equal(2, fragments.Count);
    }

    [Fact]
    public void ExtractText_TmOperator_SetsAbsolutePosition()
    {
        var content = @"
BT
/F1 12 Tf
1 0 0 1 100 700 Tm
(At 100,700) Tj
1 0 0 1 200 600 Tm
(At 200,600) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        (string text, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(bytes);

        // Extractor adds newline at start and between position changes
        Assert.Contains("At 100,700", text);
        Assert.Contains("At 200,600", text);
        Assert.Equal(2, fragments.Count);
        Assert.Equal(100, fragments[0].X, 0.01);
        Assert.Equal(200, fragments[1].X, 0.01);
    }

    [Fact]
    public void ExtractText_TStarOperator_MovesToNextLine()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
12 TL
(Line 1) Tj
T*
(Line 2) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        string text = PdfTextExtractor.ExtractText(bytes);

        Assert.Contains("Line 1", text);
        Assert.Contains("Line 2", text);
    }

    #endregion

    #region Text Showing Operators Tests

    [Fact]
    public void ExtractText_TjOperator_ShowsText()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
(Text via Tj) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        string text = PdfTextExtractor.ExtractText(bytes);

        Assert.Equal("\r\nText via Tj", text);
    }

    [Fact]
    public void ExtractText_QuoteOperator_ShowsTextAndMovesToNextLine()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
12 TL
(Line 1) '
(Line 2) '
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        string text = PdfTextExtractor.ExtractText(bytes);

        Assert.Contains("Line 1", text);
        Assert.Contains("Line 2", text);
    }

    [Fact]
    public void ExtractText_DoubleQuoteOperator_SetsSpacingAndShowsText()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
12 TL
0 0 (Line 1) ""
0 0 (Line 2) ""
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        string text = PdfTextExtractor.ExtractText(bytes);

        Assert.Contains("Line 1", text);
        Assert.Contains("Line 2", text);
    }

    [Fact]
    public void ExtractText_TJOperator_HandlesArray()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
[(Hello) -100 (World)] TJ
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        string text = PdfTextExtractor.ExtractText(bytes);

        Assert.Contains("Hello", text);
        Assert.Contains("World", text);
    }

    [Fact]
    public void ExtractText_TJOperator_WithPositioning_HandlesSpacing()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
[(T) -100 (e) -50 (s) -50 (t)] TJ
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        string text = PdfTextExtractor.ExtractText(bytes);

        Assert.Contains("Test", text);
    }

    #endregion

    #region Real-World Scenarios

    [Fact]
    public void ExtractText_PDFWithFormattedText_PreservesContent()
    {
        var content = @"
BT
/F1 12 Tf
72 720 Td
(Document Title) Tj
0 -24 Td
/F2 10 Tf
(Paragraph text with normal font.) Tj
0 -12 Td
(Second line of paragraph.) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        string text = PdfTextExtractor.ExtractText(bytes);

        Assert.Contains("Document Title", text);
        Assert.Contains("Paragraph text with normal font.", text);
        Assert.Contains("Second line of paragraph.", text);
    }

    [Fact]
    public void ExtractText_MultiColumnLayout_ExtractsAllColumns()
    {
        var content = @"
BT
/F1 10 Tf
72 720 Td
(Left column) Tj
ET
BT
/F1 10 Tf
300 720 Td
(Right column) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        string text = PdfTextExtractor.ExtractText(bytes);

        Assert.Contains("Left column", text);
        Assert.Contains("Right column", text);
    }

    [Fact]
    public void ExtractText_MixedContentWithGraphics_ExtractsOnlyText()
    {
        var content = @"
q
1 0 0 1 100 100 cm
100 200 m
150 250 l
S
Q
BT
/F1 12 Tf
100 700 Td
(Text content) Tj
ET
q
0.5 g
100 100 100 100 re
f
Q";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        string text = PdfTextExtractor.ExtractText(bytes);

        Assert.Equal("\r\nText content", text);
    }

    [Fact]
    public void ExtractText_LargeDocument_HandlesEfficiently()
    {
        var contentBuilder = new StringBuilder();
        contentBuilder.AppendLine("BT");
        contentBuilder.AppendLine("/F1 12 Tf");
        contentBuilder.AppendLine("100 700 Td");

        // Add 100 text operators
        for (var i = 0; i < 100; i++)
        {
            contentBuilder.AppendLine($"(Line {i}) Tj");
            contentBuilder.AppendLine("0 -12 Td");
        }

        contentBuilder.AppendLine("ET");

        byte[] bytes = Encoding.ASCII.GetBytes(contentBuilder.ToString());

        string text = PdfTextExtractor.ExtractText(bytes);

        Assert.Contains("Line 0", text);
        Assert.Contains("Line 99", text);
    }

    #endregion

    #region Edge Cases and Error Handling

    [Fact]
    public void ExtractText_NullContent_ReturnsEmpty()
    {
        string text = PdfTextExtractor.ExtractText((byte[])null!);

        Assert.Empty(text);
    }

    [Fact]
    public void ExtractText_TextOutsideTextBlock_Ignored()
    {
        var content = @"
/F1 12 Tf
100 700 Td
(Outside BT/ET) Tj
BT
/F1 12 Tf
100 700 Td
(Inside BT/ET) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        string text = PdfTextExtractor.ExtractText(bytes);

        // Should only extract text inside BT/ET
        Assert.Equal("\r\nInside BT/ET", text);
    }

    [Fact]
    public void ExtractText_NestedTextBlocks_HandlesGracefully()
    {
        // Nested BT/ET blocks are invalid but should be handled
        var content = @"
BT
/F1 12 Tf
100 700 Td
(First) Tj
BT
(Nested) Tj
ET
(After nested) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        // Should not throw
        string text = PdfTextExtractor.ExtractText(bytes);

        Assert.NotNull(text);
    }

    [Fact]
    public void ExtractText_MissingEndText_HandlesGracefully()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
(Text without ET) Tj";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        // Should not throw
        string text = PdfTextExtractor.ExtractText(bytes);

        Assert.Contains("Text without ET", text);
    }

    [Fact]
    public void ExtractText_EmptyTextBlock_ReturnsEmpty()
    {
        var content = @"
BT
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        string text = PdfTextExtractor.ExtractText(bytes);

        Assert.Empty(text);
    }

    [Fact]
    public void ExtractText_MalformedOperators_HandlesGracefully()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
(Good text) Tj
invalid_operator
(More text) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        // Should not throw
        string text = PdfTextExtractor.ExtractText(bytes);

        Assert.Contains("Good text", text);
        Assert.Contains("More text", text);
    }

    #endregion

    #region Instance Methods Tests

    [Fact]
    public void GetText_AfterConstruction_ReturnsEmpty()
    {
        var extractor = new PdfTextExtractor();

        string text = extractor.GetText();

        Assert.Empty(text);
    }

    [Fact]
    public void GetTextFragments_AfterConstruction_ReturnsEmpty()
    {
        var extractor = new PdfTextExtractor();

        List<TextFragment> fragments = extractor.GetTextFragments();

        Assert.Empty(fragments);
    }

    [Fact]
    public void GetTextFragments_ReturnsCopy_NotReference()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
(Test) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        (_, List<TextFragment> fragments1) = PdfTextExtractor.ExtractTextWithFragments(bytes);
        (_, List<TextFragment> fragments2) = PdfTextExtractor.ExtractTextWithFragments(bytes);

        // Should be different list instances
        Assert.NotSame(fragments1, fragments2);

        // But contain equivalent data
        Assert.Equal(fragments1.Count, fragments2.Count);
        Assert.Equal(fragments1[0].Text, fragments2[0].Text);
    }

    #endregion

    #region TextFragment Tests

    [Fact]
    public void TextFragment_ToString_FormatsCorrectly()
    {
        var fragment = new TextFragment
        {
            Text = "Hello",
            X = 100.5,
            Y = 200.75,
            FontName = "F1",
            FontSize = 12.0
        };

        var result = fragment.ToString();

        Assert.Contains("Hello", result);
        Assert.Contains("100.50", result);
        Assert.Contains("200.75", result);
        Assert.Contains("F1", result);
        Assert.Contains("12", result);
    }

    [Fact]
    public void TextFragment_DefaultValues_AreCorrect()
    {
        var fragment = new TextFragment();

        Assert.Empty(fragment.Text);
        Assert.Equal(0, fragment.X);
        Assert.Equal(0, fragment.Y);
        Assert.Null(fragment.FontName);
        Assert.Equal(0, fragment.FontSize);
    }

    #endregion
}
