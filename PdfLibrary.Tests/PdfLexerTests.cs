using PdfLibrary.Parsing;
using System.Text;

namespace PdfLibrary.Tests;

/// <summary>
/// Comprehensive tests for PdfLexer token parsing
/// Tests cover all token types defined in ISO 32000-1:2008 section 7.2
/// </summary>
public class PdfLexerTests
{
    #region Helper Methods

    private static PdfLexer CreateLexer(string content)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(content);
        MemoryStream stream = new(bytes);
        return new PdfLexer(stream);
    }

    #endregion

    #region Integer Tests

    [Fact]
    public void Lexer_ParsesPositiveInteger()
    {
        PdfLexer lexer = CreateLexer("123");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.Integer, token.Type);
        Assert.Equal("123", token.Value);
    }

    [Fact]
    public void Lexer_ParsesNegativeInteger()
    {
        PdfLexer lexer = CreateLexer("-456");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.Integer, token.Type);
        Assert.Equal("-456", token.Value);
    }

    [Fact]
    public void Lexer_ParsesZero()
    {
        PdfLexer lexer = CreateLexer("0");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.Integer, token.Type);
        Assert.Equal("0", token.Value);
    }

    [Fact]
    public void Lexer_ParsesPositiveIntegerWithPlusSign()
    {
        PdfLexer lexer = CreateLexer("+789");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.Integer, token.Type);
        Assert.Equal("+789", token.Value);
    }

    #endregion

    #region Real Number Tests

    [Fact]
    public void Lexer_ParsesPositiveReal()
    {
        PdfLexer lexer = CreateLexer("123.456");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.Real, token.Type);
        Assert.Equal("123.456", token.Value);
    }

    [Fact]
    public void Lexer_ParsesNegativeReal()
    {
        PdfLexer lexer = CreateLexer("-78.9");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.Real, token.Type);
        Assert.Equal("-78.9", token.Value);
    }

    [Fact]
    public void Lexer_ParsesRealWithPlusSign()
    {
        PdfLexer lexer = CreateLexer("+0.25");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.Real, token.Type);
        Assert.Equal("+0.25", token.Value);
    }

    #endregion

    #region Boolean Tests

    [Fact]
    public void Lexer_ParsesTrue()
    {
        PdfLexer lexer = CreateLexer("true");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.Boolean, token.Type);
        Assert.Equal("true", token.Value);
    }

    [Fact]
    public void Lexer_ParsesFalse()
    {
        PdfLexer lexer = CreateLexer("false");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.Boolean, token.Type);
        Assert.Equal("false", token.Value);
    }

    #endregion

    #region Null Test

    [Fact]
    public void Lexer_ParsesNull()
    {
        PdfLexer lexer = CreateLexer("null");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.Null, token.Type);
        Assert.Equal("null", token.Value);
    }

    #endregion

    #region Name Tests

    [Fact]
    public void Lexer_ParsesSimpleName()
    {
        PdfLexer lexer = CreateLexer("/Name1");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.Name, token.Type);
        Assert.Equal("Name1", token.Value);
    }

    [Fact]
    public void Lexer_ParsesNameWithUnderscore()
    {
        PdfLexer lexer = CreateLexer("/Type_Name");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.Name, token.Type);
        Assert.Equal("Type_Name", token.Value);
    }

    [Fact]
    public void Lexer_ParsesEmptyName()
    {
        PdfLexer lexer = CreateLexer("/ ");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.Name, token.Type);
        Assert.Equal("", token.Value);
    }

    [Fact]
    public void Lexer_ParsesNameStopsAtWhitespace()
    {
        PdfLexer lexer = CreateLexer("/Font 123");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.Name, token.Type);
        Assert.Equal("Font", token.Value);
    }

    [Fact]
    public void Lexer_ParsesNameStopsAtDelimiter()
    {
        PdfLexer lexer = CreateLexer("/Type[");
        PdfToken token1 = lexer.NextToken();
        PdfToken token2 = lexer.NextToken();

        Assert.Equal(PdfTokenType.Name, token1.Type);
        Assert.Equal("Type", token1.Value);
        Assert.Equal(PdfTokenType.ArrayStart, token2.Type);
    }

    #endregion

    #region Literal String Tests

    [Fact]
    public void Lexer_ParsesSimpleLiteralString()
    {
        PdfLexer lexer = CreateLexer("(Hello World)");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.String, token.Type);
        Assert.Equal("Hello World", token.Value);
    }

    [Fact]
    public void Lexer_ParsesEmptyLiteralString()
    {
        PdfLexer lexer = CreateLexer("()");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.String, token.Type);
        Assert.Equal("", token.Value);
    }

    [Fact]
    public void Lexer_ParsesLiteralStringWithNestedParentheses()
    {
        PdfLexer lexer = CreateLexer("(Text (with nested) parens)");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.String, token.Type);
        Assert.Equal("Text (with nested) parens", token.Value);
    }

    [Fact]
    public void Lexer_ParsesLiteralStringWithEscapedParentheses()
    {
        PdfLexer lexer = CreateLexer(@"(Left \( Right \))");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.String, token.Type);
        Assert.Equal("Left ( Right )", token.Value);
    }

    [Fact]
    public void Lexer_ParsesLiteralStringWithNewlineEscape()
    {
        PdfLexer lexer = CreateLexer(@"(Line1\nLine2)");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.String, token.Type);
        Assert.Equal("Line1\nLine2", token.Value);
    }

    [Fact]
    public void Lexer_ParsesLiteralStringWithAllEscapes()
    {
        PdfLexer lexer = CreateLexer(@"(\n\r\t\b\f\(\)\\)");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.String, token.Type);
        Assert.Equal("\n\r\t\b\f()\\", token.Value);
    }

    [Fact]
    public void Lexer_ParsesLiteralStringWithOctalEscape()
    {
        PdfLexer lexer = CreateLexer(@"(\101\102\103)"); // ABC in octal
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.String, token.Type);
        Assert.Equal("ABC", token.Value);
    }

    [Fact]
    public void Lexer_ParsesLiteralStringWithTwoDigitOctal()
    {
        PdfLexer lexer = CreateLexer(@"(\50X)"); // '(' in octal + X
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.String, token.Type);
        Assert.Equal("(X", token.Value);
    }

    #endregion

    #region Hex String Tests

    [Fact]
    public void Lexer_ParsesHexString()
    {
        PdfLexer lexer = CreateLexer("<48656C6C6F>");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.String, token.Type);
        Assert.Equal("Hello", token.Value); // <48656C6C6F> = "Hello" in hex
    }

    [Fact]
    public void Lexer_ParsesHexStringWithWhitespace()
    {
        PdfLexer lexer = CreateLexer("<48 65 6C 6C 6F>");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.String, token.Type);
        Assert.Equal("Hello", token.Value); // Whitespace is ignored in hex strings
    }

    [Fact]
    public void Lexer_ParsesEmptyHexString()
    {
        PdfLexer lexer = CreateLexer("<>");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.String, token.Type);
        Assert.Equal("", token.Value);
    }

    [Fact]
    public void Lexer_ParsesHexStringWithMixedCase()
    {
        PdfLexer lexer = CreateLexer("<4A6b>");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.String, token.Type);
        Assert.Equal("Jk", token.Value); // <4A6b> = "Jk" (0x4A='J', 0x6b='k')
    }

    #endregion

    #region Array Delimiter Tests

    [Fact]
    public void Lexer_ParsesArrayStart()
    {
        PdfLexer lexer = CreateLexer("[");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.ArrayStart, token.Type);
        Assert.Equal("[", token.Value);
    }

    [Fact]
    public void Lexer_ParsesArrayEnd()
    {
        PdfLexer lexer = CreateLexer("]");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.ArrayEnd, token.Type);
        Assert.Equal("]", token.Value);
    }

    [Fact]
    public void Lexer_ParsesArrayWithContent()
    {
        PdfLexer lexer = CreateLexer("[1 2 3]");
        List<PdfToken> tokens = lexer.ReadAllTokens();

        Assert.Equal(5, tokens.Count);
        Assert.Equal(PdfTokenType.ArrayStart, tokens[0].Type);
        Assert.Equal(PdfTokenType.Integer, tokens[1].Type);
        Assert.Equal("1", tokens[1].Value);
        Assert.Equal(PdfTokenType.Integer, tokens[2].Type);
        Assert.Equal("2", tokens[2].Value);
        Assert.Equal(PdfTokenType.Integer, tokens[3].Type);
        Assert.Equal("3", tokens[3].Value);
        Assert.Equal(PdfTokenType.ArrayEnd, tokens[4].Type);
    }

    #endregion

    #region Dictionary Delimiter Tests

    [Fact]
    public void Lexer_ParsesDictionaryStart()
    {
        PdfLexer lexer = CreateLexer("<<");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.DictionaryStart, token.Type);
        Assert.Equal("<<", token.Value);
    }

    [Fact]
    public void Lexer_ParsesDictionaryEnd()
    {
        PdfLexer lexer = CreateLexer(">>");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.DictionaryEnd, token.Type);
        Assert.Equal(">>", token.Value);
    }

    [Fact]
    public void Lexer_ParsesDictionaryWithContent()
    {
        PdfLexer lexer = CreateLexer("<</Type/Font>>");
        List<PdfToken> tokens = lexer.ReadAllTokens();

        Assert.Equal(4, tokens.Count);
        Assert.Equal(PdfTokenType.DictionaryStart, tokens[0].Type);
        Assert.Equal(PdfTokenType.Name, tokens[1].Type);
        Assert.Equal("Type", tokens[1].Value);
        Assert.Equal(PdfTokenType.Name, tokens[2].Type);
        Assert.Equal("Font", tokens[2].Value);
        Assert.Equal(PdfTokenType.DictionaryEnd, tokens[3].Type);
    }

    #endregion

    #region Keyword Tests

    [Fact]
    public void Lexer_ParsesObjKeyword()
    {
        PdfLexer lexer = CreateLexer("obj");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.Obj, token.Type);
        Assert.Equal("obj", token.Value);
    }

    [Fact]
    public void Lexer_ParsesEndObjKeyword()
    {
        PdfLexer lexer = CreateLexer("endobj");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.EndObj, token.Type);
        Assert.Equal("endobj", token.Value);
    }

    [Fact]
    public void Lexer_ParsesStreamKeyword()
    {
        PdfLexer lexer = CreateLexer("stream");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.Stream, token.Type);
        Assert.Equal("stream", token.Value);
    }

    [Fact]
    public void Lexer_ParsesEndStreamKeyword()
    {
        PdfLexer lexer = CreateLexer("endstream");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.EndStream, token.Type);
        Assert.Equal("endstream", token.Value);
    }

    [Fact]
    public void Lexer_ParsesRKeyword()
    {
        PdfLexer lexer = CreateLexer("R");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.R, token.Type);
        Assert.Equal("R", token.Value);
    }

    [Fact]
    public void Lexer_ParsesXrefKeyword()
    {
        PdfLexer lexer = CreateLexer("xref");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.Xref, token.Type);
        Assert.Equal("xref", token.Value);
    }

    [Fact]
    public void Lexer_ParsesTrailerKeyword()
    {
        PdfLexer lexer = CreateLexer("trailer");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.Trailer, token.Type);
        Assert.Equal("trailer", token.Value);
    }

    [Fact]
    public void Lexer_ParsesStartXrefKeyword()
    {
        PdfLexer lexer = CreateLexer("startxref");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.StartXref, token.Type);
        Assert.Equal("startxref", token.Value);
    }

    #endregion

    #region Comment Tests

    [Fact]
    public void Lexer_SkipsComment()
    {
        PdfLexer lexer = CreateLexer("% This is a comment\n123");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.Integer, token.Type);
        Assert.Equal("123", token.Value);
    }

    [Fact]
    public void Lexer_SkipsMultipleComments()
    {
        PdfLexer lexer = CreateLexer("% Comment 1\n% Comment 2\ntrue");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.Boolean, token.Type);
        Assert.Equal("true", token.Value);
    }

    [Fact]
    public void Lexer_SkipsCommentWithCRLF()
    {
        PdfLexer lexer = CreateLexer("% Comment\r\n456");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.Integer, token.Type);
        Assert.Equal("456", token.Value);
    }

    #endregion

    #region Whitespace Tests

    [Fact]
    public void Lexer_SkipsLeadingWhitespace()
    {
        PdfLexer lexer = CreateLexer("  \t\n\r  123");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.Integer, token.Type);
        Assert.Equal("123", token.Value);
    }

    [Fact]
    public void Lexer_HandlesWhitespaceBetweenTokens()
    {
        PdfLexer lexer = CreateLexer("1  2  3");
        List<PdfToken> tokens = lexer.ReadAllTokens();

        Assert.Equal(3, tokens.Count);
        Assert.All(tokens, t => Assert.Equal(PdfTokenType.Integer, t.Type));
    }

    #endregion

    #region End of File Tests

    [Fact]
    public void Lexer_ReturnsEOFWhenEmpty()
    {
        PdfLexer lexer = CreateLexer("");
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.EndOfFile, token.Type);
    }

    [Fact]
    public void Lexer_ReturnsEOFAfterLastToken()
    {
        PdfLexer lexer = CreateLexer("123");
        lexer.NextToken(); // Consume 123
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.EndOfFile, token.Type);
    }

    #endregion

    #region Position Tracking Tests

    [Fact]
    public void Lexer_TracksPosition()
    {
        PdfLexer lexer = CreateLexer("123 456");

        PdfToken token1 = lexer.NextToken();
        Assert.Equal(0, token1.Position);

        PdfToken token2 = lexer.NextToken();
        Assert.Equal(4, token2.Position);
    }

    #endregion

    #region Complex Integration Tests

    [Fact]
    public void Lexer_ParsesIndirectObjectDefinition()
    {
        PdfLexer lexer = CreateLexer("1 0 obj\n<</Type/Font>>\nendobj");
        List<PdfToken> tokens = lexer.ReadAllTokens();

        Assert.Equal(8, tokens.Count);
        Assert.Equal(PdfTokenType.Integer, tokens[0].Type);
        Assert.Equal("1", tokens[0].Value);
        Assert.Equal(PdfTokenType.Integer, tokens[1].Type);
        Assert.Equal("0", tokens[1].Value);
        Assert.Equal(PdfTokenType.Obj, tokens[2].Type);
        Assert.Equal(PdfTokenType.DictionaryStart, tokens[3].Type);
        Assert.Equal(PdfTokenType.Name, tokens[4].Type);
        Assert.Equal(PdfTokenType.Name, tokens[5].Type);
        Assert.Equal(PdfTokenType.DictionaryEnd, tokens[6].Type);
        Assert.Equal(PdfTokenType.EndObj, tokens[7].Type);
    }

    [Fact]
    public void Lexer_ParsesIndirectReference()
    {
        PdfLexer lexer = CreateLexer("5 0 R");
        List<PdfToken> tokens = lexer.ReadAllTokens();

        Assert.Equal(3, tokens.Count);
        Assert.Equal(PdfTokenType.Integer, tokens[0].Type);
        Assert.Equal("5", tokens[0].Value);
        Assert.Equal(PdfTokenType.Integer, tokens[1].Type);
        Assert.Equal("0", tokens[1].Value);
        Assert.Equal(PdfTokenType.R, tokens[2].Type);
    }

    [Fact]
    public void Lexer_ParsesNestedArray()
    {
        PdfLexer lexer = CreateLexer("[1 [2 3] 4]");
        List<PdfToken> tokens = lexer.ReadAllTokens();

        Assert.Equal(8, tokens.Count);
        Assert.Equal(PdfTokenType.ArrayStart, tokens[0].Type);
        Assert.Equal(PdfTokenType.Integer, tokens[1].Type);
        Assert.Equal(PdfTokenType.ArrayStart, tokens[2].Type);
        Assert.Equal(PdfTokenType.Integer, tokens[3].Type);
        Assert.Equal(PdfTokenType.Integer, tokens[4].Type);
        Assert.Equal(PdfTokenType.ArrayEnd, tokens[5].Type);
        Assert.Equal(PdfTokenType.Integer, tokens[6].Type);
        Assert.Equal(PdfTokenType.ArrayEnd, tokens[7].Type);
    }

    #endregion
}
