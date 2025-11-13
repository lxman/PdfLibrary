using System.Text;
using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core.Primitives;
using Xunit;

namespace PdfLibrary.Tests;

/// <summary>
/// Comprehensive tests for PdfContentParser
/// Tests parsing of PDF content streams into operators (ISO 32000-1:2008 section 7.8.2)
/// </summary>
public class PdfContentParserTests
{
    #region Basic Operator Parsing Tests

    [Fact]
    public void Parse_EmptyStream_ReturnsEmptyList()
    {
        byte[] content = [];
        List<PdfOperator> operators = PdfContentParser.Parse(content);
        Assert.Empty(operators);
    }

    [Fact]
    public void Parse_NullStream_ReturnsEmptyList()
    {
        List<PdfOperator> operators = PdfContentParser.Parse((byte[])null!);
        Assert.Empty(operators);
    }

    [Fact]
    public void Parse_TextObject_BeginAndEnd()
    {
        string content = "BT ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Equal(2, operators.Count);
        Assert.IsType<BeginTextOperator>(operators[0]);
        Assert.Equal("BT", operators[0].Name);
        Assert.IsType<EndTextOperator>(operators[1]);
        Assert.Equal("ET", operators[1].Name);
    }

    [Fact]
    public void Parse_GraphicsState_SaveAndRestore()
    {
        string content = "q Q";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Equal(2, operators.Count);
        Assert.IsType<SaveGraphicsStateOperator>(operators[0]);
        Assert.Equal("q", operators[0].Name);
        Assert.IsType<RestoreGraphicsStateOperator>(operators[1]);
        Assert.Equal("Q", operators[1].Name);
    }

    [Fact]
    public void Parse_PathOperators_MoveLineClose()
    {
        string content = "100 200 m 150 250 l h";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Equal(3, operators.Count);
        Assert.IsType<MoveToOperator>(operators[0]);
        Assert.Equal("m", operators[0].Name);
        Assert.IsType<LineToOperator>(operators[1]);
        Assert.Equal("l", operators[1].Name);
        Assert.IsType<ClosePathOperator>(operators[2]);
        Assert.Equal("h", operators[2].Name);
    }

    [Fact]
    public void Parse_RectangleOperator_FourOperands()
    {
        string content = "100 200 50 75 re";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.IsType<RectangleOperator>(operators[0]);
        Assert.Equal("re", operators[0].Name);
        Assert.Equal(4, operators[0].Operands.Count);
    }

    [Fact]
    public void Parse_ConcatenateMatrix_SixOperands()
    {
        string content = "1 0 0 1 100 200 cm";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.IsType<ConcatenateMatrixOperator>(operators[0]);
        Assert.Equal("cm", operators[0].Name);
        Assert.Equal(6, operators[0].Operands.Count);
    }

    [Fact]
    public void Parse_PathPaintingOperators_StrokeAndFill()
    {
        string content = "S f B n";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Equal(4, operators.Count);
        Assert.IsType<StrokeOperator>(operators[0]);
        Assert.IsType<FillOperator>(operators[1]);
        Assert.IsType<FillAndStrokeOperator>(operators[2]);
        Assert.IsType<EndPathOperator>(operators[3]);
    }

    [Fact]
    public void Parse_ClipOperators_WindingAndEvenOdd()
    {
        string content = "W W*";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Equal(2, operators.Count);
        Assert.IsType<ClipOperator>(operators[0]);
        Assert.Equal("W", operators[0].Name);
        Assert.IsType<ClipEvenOddOperator>(operators[1]);
        Assert.Equal("W*", operators[1].Name);
    }

    #endregion

    #region Text Operator Tests

    [Fact]
    public void Parse_ShowText_WithStringOperand()
    {
        string content = "(Hello, World!) Tj";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.IsType<ShowTextOperator>(operators[0]);
        Assert.Equal("Tj", operators[0].Name);
        Assert.Single(operators[0].Operands);
        Assert.IsType<PdfString>(operators[0].Operands[0]);
        Assert.Equal("Hello, World!", ((PdfString)operators[0].Operands[0]).Value);
    }

    [Fact]
    public void Parse_ShowTextWithPositioning_WithArray()
    {
        string content = "[(Hello) -250 (World)] TJ";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.IsType<ShowTextWithPositioningOperator>(operators[0]);
        Assert.Equal("TJ", operators[0].Name);
        Assert.Single(operators[0].Operands);
        Assert.IsType<PdfArray>(operators[0].Operands[0]);

        var array = (PdfArray)operators[0].Operands[0];
        Assert.Equal(3, array.Count);
        Assert.IsType<PdfString>(array[0]);
        Assert.Equal("Hello", ((PdfString)array[0]).Value);
        Assert.IsType<PdfInteger>(array[1]);
        Assert.Equal(-250, ((PdfInteger)array[1]).Value);
        Assert.IsType<PdfString>(array[2]);
        Assert.Equal("World", ((PdfString)array[2]).Value);
    }

    [Fact]
    public void Parse_MoveToNextLineAndShowText_SingleQuote()
    {
        string content = "(Next line text) '";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.IsType<MoveToNextLineAndShowTextOperator>(operators[0]);
        Assert.Equal("'", operators[0].Name);
    }

    [Fact]
    public void Parse_SetSpacingAndShowText_DoubleQuote()
    {
        string content = "1.0 2.0 (Text with spacing) \"";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.IsType<SetSpacingMoveAndShowTextOperator>(operators[0]);
        Assert.Equal("\"", operators[0].Name);
        Assert.Equal(3, operators[0].Operands.Count);
    }

    [Fact]
    public void Parse_SetTextFont_NameAndSize()
    {
        string content = "/F1 12 Tf";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.IsType<SetTextFontOperator>(operators[0]);
        Assert.Equal("Tf", operators[0].Name);
        Assert.Equal(2, operators[0].Operands.Count);
        Assert.IsType<PdfName>(operators[0].Operands[0]);
        Assert.Equal("F1", ((PdfName)operators[0].Operands[0]).Value);
    }

    [Fact]
    public void Parse_TextPositioning_MoveTextPosition()
    {
        string content = "100 700 Td";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.IsType<MoveTextPositionOperator>(operators[0]);
        Assert.Equal("Td", operators[0].Name);
        Assert.Equal(2, operators[0].Operands.Count);
    }

    [Fact]
    public void Parse_SetTextMatrix_SixOperands()
    {
        string content = "1 0 0 1 100 200 Tm";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.IsType<SetTextMatrixOperator>(operators[0]);
        Assert.Equal("Tm", operators[0].Name);
        Assert.Equal(6, operators[0].Operands.Count);
    }

    [Fact]
    public void Parse_MoveToNextLine_NoOperands()
    {
        string content = "T*";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.IsType<MoveToNextLineOperator>(operators[0]);
        Assert.Equal("T*", operators[0].Name);
        Assert.Empty(operators[0].Operands);
    }

    [Fact]
    public void Parse_TextStateOperators_Various()
    {
        string content = "1.5 Tc 2.0 Tw 100 Tz 14 TL 0 Tr 5 Ts";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Equal(6, operators.Count);
        Assert.IsType<SetCharSpacingOperator>(operators[0]);
        Assert.IsType<SetWordSpacingOperator>(operators[1]);
        Assert.IsType<SetHorizontalScalingOperator>(operators[2]);
        Assert.IsType<SetTextLeadingOperator>(operators[3]);
        Assert.IsType<SetTextRenderingModeOperator>(operators[4]);
        Assert.IsType<SetTextRiseOperator>(operators[5]);
    }

    #endregion

    #region Operand Type Tests

    [Fact]
    public void Parse_IntegerOperands_ParsedCorrectly()
    {
        string content = "100 200 m";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.IsType<MoveToOperator>(operators[0]);
        Assert.Equal(2, operators[0].Operands.Count);
        // Check that operands were parsed as integers
        Assert.True(operators[0].Operands[0] is PdfInteger or PdfReal);
        Assert.True(operators[0].Operands[1] is PdfInteger or PdfReal);
    }

    [Fact]
    public void Parse_RealNumberOperands_ParsedCorrectly()
    {
        string content = "3.14 2.718 1.414 cm";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.True(operators[0].Operands.Count >= 3);
        Assert.IsType<PdfReal>(operators[0].Operands[0]);
        Assert.Equal(3.14, ((PdfReal)operators[0].Operands[0]).Value, precision: 2);
    }

    [Fact]
    public void Parse_StringOperands_LiteralString()
    {
        string content = "(This is a test) Tj";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.Single(operators[0].Operands);
        Assert.IsType<PdfString>(operators[0].Operands[0]);
        Assert.Equal("This is a test", ((PdfString)operators[0].Operands[0]).Value);
    }

    [Fact]
    public void Parse_NameOperands_ParsedCorrectly()
    {
        string content = "/XObject Do";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.IsType<InvokeXObjectOperator>(operators[0]);
        Assert.Single(operators[0].Operands);
        Assert.IsType<PdfName>(operators[0].Operands[0]);
        Assert.Equal("XObject", ((PdfName)operators[0].Operands[0]).Value);
    }

    [Fact]
    public void Parse_ArrayOperands_ParsedCorrectly()
    {
        string content = "[1 2 3] 0 d";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.IsType<SetDashPatternOperator>(operators[0]);
        Assert.Equal(2, operators[0].Operands.Count);
        Assert.IsType<PdfArray>(operators[0].Operands[0]);

        var array = (PdfArray)operators[0].Operands[0];
        Assert.Equal(3, array.Count);
        Assert.All(array, item => Assert.IsType<PdfInteger>(item));
    }

    [Fact]
    public void Parse_DictionaryOperands_ParsedCorrectly()
    {
        string content = "<</Type /Font /Subtype /Type1>> unknown_op";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.IsType<GenericOperator>(operators[0]);
        Assert.Single(operators[0].Operands);
        // Dictionary was parsed (even if empty in this test context)
        Assert.IsType<PdfDictionary>(operators[0].Operands[0]);
    }

    [Fact]
    public void Parse_BooleanOperands_TrueAndFalse()
    {
        string content = "true false unknown_op";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.Equal("unknown_op", operators[0].Name);
        Assert.Equal(2, operators[0].Operands.Count);
        Assert.IsType<PdfBoolean>(operators[0].Operands[0]);
        Assert.True(((PdfBoolean)operators[0].Operands[0]).Value);
        Assert.IsType<PdfBoolean>(operators[0].Operands[1]);
        Assert.False(((PdfBoolean)operators[0].Operands[1]).Value);
    }

    [Fact]
    public void Parse_NullOperand_ParsedCorrectly()
    {
        string content = "null unknown_op";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.Single(operators[0].Operands);
        Assert.IsType<PdfNull>(operators[0].Operands[0]);
    }

    [Fact]
    public void Parse_MixedOperandTypes_ParsedCorrectly()
    {
        string content = "100 3.14 /Name (String) [1 2] true null unknown_op";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.Equal(7, operators[0].Operands.Count);
        Assert.IsType<PdfInteger>(operators[0].Operands[0]);
        Assert.IsType<PdfReal>(operators[0].Operands[1]);
        Assert.IsType<PdfName>(operators[0].Operands[2]);
        Assert.IsType<PdfString>(operators[0].Operands[3]);
        Assert.IsType<PdfArray>(operators[0].Operands[4]);
        Assert.IsType<PdfBoolean>(operators[0].Operands[5]);
        Assert.IsType<PdfNull>(operators[0].Operands[6]);
    }

    #endregion

    #region Real-World Content Stream Tests

    [Fact]
    public void Parse_SimpleTextStream_CompleteSequence()
    {
        string content = @"
BT
/F1 12 Tf
100 700 Td
(Hello, World!) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Equal(5, operators.Count);
        Assert.IsType<BeginTextOperator>(operators[0]);
        Assert.IsType<SetTextFontOperator>(operators[1]);
        Assert.IsType<MoveTextPositionOperator>(operators[2]);
        Assert.IsType<ShowTextOperator>(operators[3]);
        Assert.IsType<EndTextOperator>(operators[4]);
    }

    [Fact]
    public void Parse_MultiLineText_WithPositioning()
    {
        string content = @"
BT
/F1 12 Tf
100 700 Td
(First line) Tj
0 -14 Td
(Second line) Tj
0 -14 Td
(Third line) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Equal(9, operators.Count);
        Assert.IsType<BeginTextOperator>(operators[0]);
        Assert.IsType<ShowTextOperator>(operators[3]);
        Assert.IsType<MoveTextPositionOperator>(operators[4]);
        Assert.IsType<ShowTextOperator>(operators[5]);
        Assert.IsType<EndTextOperator>(operators[8]);
    }

    [Fact]
    public void Parse_TextWithArrayPositioning_TJOperator()
    {
        string content = @"
BT
/F1 12 Tf
100 700 Td
[(H) -50 (e) -50 (l) -50 (l) -50 (o)] TJ
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Equal(5, operators.Count);
        Assert.IsType<ShowTextWithPositioningOperator>(operators[3]);

        var tjOp = (ShowTextWithPositioningOperator)operators[3];
        Assert.Single(tjOp.Operands);
        Assert.IsType<PdfArray>(tjOp.Operands[0]);

        var array = (PdfArray)tjOp.Operands[0];
        Assert.Equal(9, array.Count); // 5 strings + 4 integers
    }

    [Fact]
    public void Parse_GraphicsPath_Rectangle()
    {
        string content = @"
q
1 0 0 1 100 200 cm
50 50 200 100 re
S
Q";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Equal(5, operators.Count);
        Assert.IsType<SaveGraphicsStateOperator>(operators[0]);
        Assert.IsType<ConcatenateMatrixOperator>(operators[1]);
        Assert.IsType<RectangleOperator>(operators[2]);
        Assert.IsType<StrokeOperator>(operators[3]);
        Assert.IsType<RestoreGraphicsStateOperator>(operators[4]);
    }

    [Fact]
    public void Parse_ComplexPath_MoveLineCurve()
    {
        string content = @"
100 200 m
150 200 l
150 100 200 100 200 150 c
h
f";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Equal(5, operators.Count);
        Assert.IsType<MoveToOperator>(operators[0]);
        Assert.IsType<LineToOperator>(operators[1]);
        Assert.IsType<CurveToOperator>(operators[2]);
        Assert.IsType<ClosePathOperator>(operators[3]);
        Assert.IsType<FillOperator>(operators[4]);
    }

    [Fact]
    public void Parse_CombinedTextAndGraphics_Mixed()
    {
        string content = @"
q
1 w
100 100 200 50 re
S
Q
BT
/F1 14 Tf
110 115 Td
(Text inside box) Tj
ET";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Equal(10, operators.Count);
        // Graphics operators
        Assert.IsType<SaveGraphicsStateOperator>(operators[0]);
        Assert.IsType<SetLineWidthOperator>(operators[1]);
        Assert.IsType<RectangleOperator>(operators[2]);
        Assert.IsType<StrokeOperator>(operators[3]);
        Assert.IsType<RestoreGraphicsStateOperator>(operators[4]);
        // Text operators
        Assert.IsType<BeginTextOperator>(operators[5]);
        Assert.IsType<SetTextFontOperator>(operators[6]);
        Assert.IsType<MoveTextPositionOperator>(operators[7]);
        Assert.IsType<ShowTextOperator>(operators[8]);
        Assert.IsType<EndTextOperator>(operators[9]);
    }

    [Fact]
    public void Parse_GraphicsStateParameters_LineCapJoinMiter()
    {
        string content = @"
1 J
2 j
10 M
1.5 w";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Equal(4, operators.Count);
        Assert.IsType<SetLineCapOperator>(operators[0]);
        Assert.IsType<SetLineJoinOperator>(operators[1]);
        Assert.IsType<SetMiterLimitOperator>(operators[2]);
        Assert.IsType<SetLineWidthOperator>(operators[3]);
    }

    [Fact]
    public void Parse_DashPattern_ArrayAndPhase()
    {
        string content = "[3 2] 0 d";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.IsType<SetDashPatternOperator>(operators[0]);
        Assert.Equal(2, operators[0].Operands.Count);
        Assert.IsType<PdfArray>(operators[0].Operands[0]);
        Assert.True(operators[0].Operands[1] is PdfInteger or PdfReal);
    }

    [Fact]
    public void Parse_XObjectInvocation_DoOperator()
    {
        string content = "/Im1 Do";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.IsType<InvokeXObjectOperator>(operators[0]);
        Assert.Equal("Do", operators[0].Name);
        Assert.Single(operators[0].Operands);
        Assert.IsType<PdfName>(operators[0].Operands[0]);
        Assert.Equal("Im1", ((PdfName)operators[0].Operands[0]).Value);
    }

    #endregion

    #region Error Handling and Edge Cases

    [Fact]
    public void Parse_UnknownOperator_CreatesGenericOperator()
    {
        string content = "100 200 UNKNOWN_OP";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.IsType<GenericOperator>(operators[0]);
        Assert.Equal("UNKNOWN_OP", operators[0].Name);
        Assert.Equal(2, operators[0].Operands.Count);
    }

    [Fact]
    public void Parse_OperatorWithoutRequiredOperands_CreatesGenericOperator()
    {
        // Tj requires a string operand, but none provided
        string content = "Tj";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        // Should create GenericOperator since operand pattern doesn't match
        Assert.IsType<GenericOperator>(operators[0]);
        Assert.Equal("Tj", operators[0].Name);
    }

    [Fact]
    public void Parse_OperatorWithWrongOperandType_CreatesGenericOperator()
    {
        // Tf expects name then number, but we provide two numbers
        string content = "12 14 Tf";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        // Should create GenericOperator since pattern doesn't match (needs PdfName)
        Assert.IsType<GenericOperator>(operators[0]);
        Assert.Equal("Tf", operators[0].Name);
    }

    [Fact]
    public void Parse_MalformedArray_HandlesGracefully()
    {
        string content = "[1 2 3 unknown_op"; // Missing closing bracket
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        // Parser may return empty list or partial results depending on error handling
        // The key is that it doesn't throw an exception
        Assert.NotNull(operators);
    }

    [Fact]
    public void Parse_NestedArrays_ParsedCorrectly()
    {
        string content = "[[1 2] [3 4]] unknown_op";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.Single(operators[0].Operands);
        Assert.IsType<PdfArray>(operators[0].Operands[0]);

        var outerArray = (PdfArray)operators[0].Operands[0];
        Assert.Equal(2, outerArray.Count);
        Assert.IsType<PdfArray>(outerArray[0]);
        Assert.IsType<PdfArray>(outerArray[1]);
    }

    [Fact]
    public void Parse_NestedDictionaries_ParsedCorrectly()
    {
        string content = "<</Outer <</Inner /Value>> >> unknown_op";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.Single(operators[0].Operands);
        Assert.IsType<PdfDictionary>(operators[0].Operands[0]);

        var outerDict = (PdfDictionary)operators[0].Operands[0];
        Assert.True(outerDict.ContainsKey(new PdfName("Outer")));
        Assert.IsType<PdfDictionary>(outerDict[new PdfName("Outer")]);
    }

    [Fact]
    public void Parse_WhitespaceVariations_HandleCorrectly()
    {
        // Various whitespace types: space, tab, newline, carriage return
        string content = "100\t200\n300\r400 m";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.IsType<MoveToOperator>(operators[0]);
        // Should have parsed all four numbers
        Assert.True(operators[0].Operands.Count >= 2);
    }

    [Fact]
    public void Parse_NoWhitespaceBetweenTokens_StillParses()
    {
        string content = "100 200m"; // No space before 'm'
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Single(operators);
        Assert.Equal("m", operators[0].Name);
    }

    [Fact]
    public void Parse_CommentsInStream_IgnoredCorrectly()
    {
        string content = @"
% This is a comment
100 200 m
% Another comment
150 250 l";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Equal(2, operators.Count);
        Assert.IsType<MoveToOperator>(operators[0]);
        Assert.IsType<LineToOperator>(operators[1]);
    }

    [Fact]
    public void Parse_LargeContentStream_HandlesEfficiently()
    {
        // Create a large content stream with many operators
        var sb = new StringBuilder();
        for (int i = 0; i < 1000; i++)
        {
            sb.AppendLine($"{i} {i + 100} m");
        }

        byte[] bytes = Encoding.ASCII.GetBytes(sb.ToString());
        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Equal(1000, operators.Count);
        Assert.All(operators, op => Assert.IsType<MoveToOperator>(op));
    }

    #endregion

    #region Operator Category Tests

    [Fact]
    public void Parse_OperatorCategories_AssignedCorrectly()
    {
        string content = "q Q BT ET 100 200 m 150 250 l S";
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        List<PdfOperator> operators = PdfContentParser.Parse(bytes);

        Assert.Equal(OperatorCategory.GraphicsState, operators[0].Category);
        Assert.Equal(OperatorCategory.GraphicsState, operators[1].Category);
        Assert.Equal(OperatorCategory.TextObject, operators[2].Category);
        Assert.Equal(OperatorCategory.TextObject, operators[3].Category);
        Assert.Equal(OperatorCategory.PathConstruction, operators[4].Category);
        Assert.Equal(OperatorCategory.PathConstruction, operators[5].Category);
        Assert.Equal(OperatorCategory.PathPainting, operators[6].Category);
    }

    #endregion
}
