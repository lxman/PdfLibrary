using System.IO;
using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Parsing;
using Xunit;

namespace PdfLibrary.Tests.Parsing;

/// <summary>
/// ISO 19005-2 clause 6.1.6 needs two facts about how a hexadecimal string was WRITTEN that
/// <see cref="PdfLexer.ReadHexStringOrDictionaryStart"/> normalises away: the count of
/// non-white-space characters between the angle brackets (test 1 wants it even) and whether any of
/// them was outside [0-9A-Fa-f] (test 2 forbids it). These pin the capture at the point of the read.
/// </summary>
public class PdfLexerHexFactsTests
{
    private static (PdfToken Token, HexStringFacts? Facts) Lex(string raw)
    {
        var lexer = new PdfLexer(new MemoryStream(Encoding.ASCII.GetBytes(raw)));
        PdfToken token = lexer.NextToken();
        return (token, lexer.TryTakeHexFacts(token.Position, out HexStringFacts f) ? f : null);
    }

    [Theory]
    [InlineData("<48455>", 5)]              // corpus 6-1-6-t01-fail-a: odd
    [InlineData("<484550>", 6)]             // even
    [InlineData("<48 45 50>", 6)]           // whitespace is NOT counted
    [InlineData("<>", 0)]                   // empty is even, and legal
    public void The_non_whitespace_count_excludes_whitespace(string raw, int expected)
    {
        Assert.Equal(expected, Lex(raw).Facts!.Value.NonWhitespaceCount);
    }

    [Theory]
    [InlineData("<484!>", true)]            // corpus 6-1-6-t02-fail-a
    [InlineData("<48zz>", true)]
    [InlineData("<484550>", false)]
    [InlineData("<abcDEF09>", false)]       // both cases plus digits are all legal hex
    public void A_non_hex_character_is_detected(string raw, bool expected)
    {
        Assert.Equal(expected, Lex(raw).Facts!.Value.HasNonHexDigit);
    }

    [Fact]
    public void A_non_hex_character_in_the_second_half_of_a_pair_is_detected()
    {
        // The flag must come from testing each CHARACTER. Deriving it from byte.TryParse failing
        // would depend on pair alignment: "4!" fails as a unit, but "!4" is a different pair.
        Assert.True(Lex("<!4>").Facts!.Value.HasNonHexDigit);
    }

    [Fact]
    public void A_dictionary_start_records_no_facts()
    {
        var lexer = new PdfLexer(new MemoryStream(Encoding.ASCII.GetBytes("<</Type /Catalog>>")));
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.DictionaryStart, token.Type);
        Assert.False(lexer.TryTakeHexFacts(token.Position, out _));
    }

    [Fact]
    public void Facts_are_taken_only_once()
    {
        var lexer = new PdfLexer(new MemoryStream(Encoding.ASCII.GetBytes("<48455>")));
        PdfToken token = lexer.NextToken();

        Assert.True(lexer.TryTakeHexFacts(token.Position, out _));
        Assert.False(lexer.TryTakeHexFacts(token.Position, out _));
    }

    [Fact]
    public void Each_hex_string_keeps_its_own_facts_across_an_interleaved_read()
    {
        // The reason the table is keyed by POSITION rather than "the last hex string read":
        // PdfParser lexes ahead (PeekToken) before turning a buffered token into a PdfString, so a
        // "last one wins" channel would hand the second string's facts to the first.
        var lexer = new PdfLexer(new MemoryStream(Encoding.ASCII.GetBytes("<48455> <484550>")));
        PdfToken first = lexer.NextToken();
        PdfToken second = lexer.NextToken();

        Assert.True(lexer.TryTakeHexFacts(second.Position, out HexStringFacts secondFacts));
        Assert.True(lexer.TryTakeHexFacts(first.Position, out HexStringFacts firstFacts));
        Assert.Equal(5, firstFacts.NonWhitespaceCount);
        Assert.Equal(6, secondFacts.NonWhitespaceCount);
    }
}
