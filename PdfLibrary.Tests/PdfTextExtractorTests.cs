using System.Text;
using PdfLibrary.Content;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

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

        Assert.Equal("\nHello, World!", text);
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

        Assert.Equal("\nHello, World!", text);
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

        Assert.Equal("\nTest", text);
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

        Assert.Equal("\nHello", text);
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

        Assert.Equal("\nHelloWorld", text);
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

    #region Text Fragment Width Tests

    [Fact]
    public void Fragment_Width_PopulatedFromFallback_WhenNoFontResources()
    {
        // No resources → CalculateTextWidth falls back to bytes.Length * fontSize * 0.5.
        // "Test" = 4 bytes at 12pt → 4 * 12 * 0.5 = 24.0 exactly.
        var content = @"
BT
/F1 12 Tf
100 700 Td
(Test) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        (_, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(bytes);

        Assert.Single(fragments);
        Assert.Equal(24.0, fragments[0].Width, precision: 6);
    }

    [Fact]
    public void Fragment_Width_PopulatedPerString_InTJArrays()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
[(Hello) -250 (World)] TJ
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        (_, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(bytes);

        Assert.Equal(2, fragments.Count);
        Assert.Equal(5 * 12 * 0.5, fragments[0].Width, precision: 6);   // "Hello" fallback width
        Assert.Equal(5 * 12 * 0.5, fragments[1].Width, precision: 6);   // "World" fallback width
    }

    [Fact]
    public void Fragment_Width_ScaledByTextMatrix()
    {
        // Documents that set Tf size 1 and put the real size in the text matrix (e.g.
        // PDF20_AN001-BPC.pdf headings: "/F1 1 Tf" + "28 0 0 28 54 527 Tm") must get advances
        // scaled by the matrix — FontSize already was (effectiveFontSize), Width was not
        // (2026-07-04 smoke: a whole 28pt heading's fragments spanned ~15 units, so selection/
        // search highlight boxes for the line compressed into the width of the first glyph).
        // Fallback width for "Test" = 4 bytes * 1pt * 0.5 = 2.0 in text space → * 28 = 56.
        var content = @"
BT
/F1 1 Tf
28 0 0 28 54 527 Tm
(Test) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        (_, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(bytes);

        Assert.Single(fragments);
        Assert.Equal(28.0, fragments[0].FontSize, 0.01);   // effective size (already worked)
        Assert.Equal(56.0, fragments[0].Width, 0.01);      // advance must carry the Tm X-scale
    }

    [Fact]
    public void Fragment_PenAdvance_ScaledByTextMatrix_AcrossRuns()
    {
        // The second run's X must sit one full SCALED advance right of the first.
        var content = @"
BT
/F1 1 Tf
28 0 0 28 54 527 Tm
(Te) Tj
(st) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        (_, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(bytes);

        Assert.Equal(2, fragments.Count);
        Assert.Equal(54.0, fragments[0].X, 0.01);
        // "Te" fallback = 2 * 1 * 0.5 = 1.0 text-space → * 28 = 28.0 user-space
        Assert.Equal(54.0 + 28.0, fragments[1].X, 0.01);
    }

    [Fact]
    public void TJKerning_Adjustment_ScaledByTextMatrix()
    {
        // TJ numeric adjustments are thousandths of TEXT space — they must be scaled by
        // font size AND the text-matrix X-scale, like every other advance.
        var content = @"
BT
/F1 1 Tf
28 0 0 28 54 527 Tm
[(Te) -500 (st)] TJ
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        (_, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(bytes);

        Assert.Equal(2, fragments.Count);
        // "Te" advance = 28.0 (see above) plus kern: -(-500)/1000 * 1pt * 28 = +14.0
        Assert.Equal(54.0 + 28.0 + 14.0, fragments[1].X, 0.01);
    }

    #endregion

    #region Pen Cursor / TextOffset Tests

    [Fact]
    public void TJElements_GetAdvancingX_NotTheStaleMatrixPosition()
    {
        // "Hello"(5 bytes) then "World": fragment 2 must start where fragment 1 ended.
        var content = @"
BT
/F1 12 Tf
100 700 Td
[(Hello) (World)] TJ
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        (_, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(bytes);

        Assert.Equal(2, fragments.Count);
        Assert.Equal(100.0, fragments[0].X, precision: 4);
        Assert.Equal(100.0 + 5 * 12 * 0.5, fragments[1].X, precision: 4);   // 130.0
    }

    [Fact]
    public void TJKerningAdjustment_ShiftsTheCursor()
    {
        // -250/1000 * 12 = -(-3) => +3.0 to the right after "Hello".
        var content = @"
BT
/F1 12 Tf
100 700 Td
[(Hello) -250 (World)] TJ
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        (_, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(bytes);

        Assert.Equal(2, fragments.Count);
        Assert.Equal(100.0 + 30.0 + 3.0, fragments[1].X, precision: 4);
    }

    [Fact]
    public void ConsecutiveTj_WithoutRepositioning_AdvancesToo()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
(AB) Tj
(CD) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        (_, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(bytes);

        Assert.Equal(2, fragments.Count);
        Assert.Equal(100.0 + 2 * 12 * 0.5, fragments[1].X, precision: 4);   // 112.0
    }

    [Fact]
    public void TextOffset_MapsFragmentsIntoAssembledText()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
[(Hel) (lo)] TJ
0 -20 Td
(World) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        (string text, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(bytes);

        foreach (TextFragment f in fragments)
            Assert.Equal(f.Text, text.Substring(f.TextOffset, f.Text.Length));

        // Kern-split halves are adjacent in the assembled text (no separator injected between them)
        Assert.Contains("Hello", text);
    }

    [Fact]
    public void Positioning_ResetsCursorToMatrixPosition()
    {
        var content = @"
BT
/F1 12 Tf
100 700 Td
(Hello) Tj
0 -20 Td
(World) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        (_, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(bytes);

        Assert.Equal(2, fragments.Count);
        Assert.Equal(100.0, fragments[1].X, precision: 4);   // Td restarted the line
        Assert.Equal(680.0, fragments[1].Y, precision: 4);
    }

    [Fact]
    public void TextOffset_XObjectHostedFragments_RebaseOntoOuterAssembledText()
    {
        // Outer content shows text, invokes a Form XObject that itself shows text, then shows
        // more outer text. Every fragment's TextOffset (outer AND XObject-hosted) must point at
        // its own text within the final assembled string.
        var outerContent = @"
BT
/F1 12 Tf
100 700 Td
(Outer start) Tj
ET
/Fm1 Do
BT
/F1 12 Tf
100 650 Td
(Outer end) Tj
ET";
        byte[] outerBytes = Encoding.ASCII.GetBytes(outerContent);

        var formContent = @"
BT
/F1 12 Tf
50 50 Td
(Inside XObject) Tj
ET";
        byte[] formBytes = Encoding.ASCII.GetBytes(formContent);

        var formDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Form")
        };
        var formStream = new PdfStream(formDict, formBytes);

        var xobjectDict = new PdfDictionary
        {
            [new PdfName("Fm1")] = formStream
        };
        var resourcesDict = new PdfDictionary
        {
            [new PdfName("XObject")] = xobjectDict
        };
        var resources = new PdfResources(resourcesDict);

        (string text, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(outerBytes, resources);

        Assert.Contains("Outer start", text);
        Assert.Contains("Inside XObject", text);
        Assert.Contains("Outer end", text);

        foreach (TextFragment f in fragments)
        {
            Assert.Equal(f.Text, text.Substring(f.TextOffset, f.Text.Length));
        }
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

        Assert.Equal("\nText via Tj", text);
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

        Assert.Equal("\nText content", text);
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
        string text = PdfTextExtractor.ExtractText(null!);

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
        Assert.Equal("\nInside BT/ET", text);
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
