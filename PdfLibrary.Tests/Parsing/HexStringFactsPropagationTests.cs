using System.Linq;
using System.Text;
using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Parsing;
using Xunit;

namespace PdfLibrary.Tests.Parsing;

/// <summary>
/// Every path that turns a hexadecimal token into a <see cref="PdfString"/> must carry the clause
/// 6.1.6 facts with it. There are SIX such sites — four in <see cref="PdfContentParser"/> (operand
/// stack, array, dictionary, inline image) and two in <see cref="PdfParser"/> (initial parse and
/// re-construction after decryption). The corpus exercises only the operand stack, so these tests
/// are the only thing standing between the other five and a silent under-report.
/// </summary>
public class HexStringFactsPropagationTests
{
    private static PdfObject[] ContentOperands(string content) =>
        PdfContentParser.Parse(Encoding.ASCII.GetBytes(content))
            .SelectMany(op => op.Operands)
            .ToArray();

    private static void AssertOdd(PdfObject? o)
    {
        PdfString s = Assert.IsType<PdfString>(o);
        Assert.NotNull(s.HexFacts);
        Assert.Equal(5, s.HexFacts!.Value.NonWhitespaceCount);
        Assert.False(s.HexFacts!.Value.HasNonHexDigit);
    }

    [Fact]
    public void Site_1_content_stream_operand_stack()
    {
        // Exactly corpus fixture 6-1-6-t01-fail-a.pdf: "<48455> Tj".
        AssertOdd(ContentOperands("BT <48455> Tj ET").FirstOrDefault(o => o is PdfString));
    }

    [Fact]
    public void Site_2_content_stream_array()
    {
        var array = Assert.IsType<PdfArray>(
            ContentOperands("BT [<48455> -100 (x)] TJ ET").First(o => o is PdfArray));
        AssertOdd(array.First(o => o is PdfString));
    }

    [Fact]
    public void Site_3_content_stream_dictionary()
    {
        var dict = Assert.IsType<PdfDictionary>(
            ContentOperands("/OC <</Name <48455>>> BDC EMC").First(o => o is PdfDictionary));
        AssertOdd(dict[new PdfName("Name")]);
    }

    [Fact]
    public void Site_4_content_stream_inline_image()
    {
        // Brief specifies "BI /W 1 /H 1 /BPC 8 /CS /G /Foo <48455> ID [NUL] EI" — a single zero byte
        // as the (1x1, 8bpc) image data between ID and EI. Built via byte concatenation instead of a
        // C# string escape for that byte: this repo has a documented tool-pipeline hazard where a
        // backslash-u four-hex-digit escape typed into a tool-call string parameter can decode into a
        // raw zero byte landing directly in the .cs file (making the source file binary to git and
        // silently vanishing from diffs), instead of surviving as literal source characters for the
        // C# compiler to interpret later. Concatenating a one-byte array sidesteps that entirely while
        // producing the identical runtime bytes.
        byte[] contentBytes = Encoding.ASCII.GetBytes("BI /W 1 /H 1 /BPC 8 /CS /G /Foo <48455> ID ")
            .Concat(new byte[] { 0 })
            .Concat(Encoding.ASCII.GetBytes(" EI"))
            .ToArray();

        PdfOperator inline = PdfContentParser.Parse(contentBytes)
            .First(op => op is InlineImageOperator);

        var image = Assert.IsType<InlineImageOperator>(inline);
        AssertOdd(image.Parameters[new PdfName("Foo")]);
    }

    [Fact]
    public void Site_5_object_parser()
    {
        var parser = new PdfParser(new System.IO.MemoryStream(Encoding.ASCII.GetBytes("<48455>")));
        AssertOdd(parser.ReadObject());
    }

    [Fact]
    public void A_literal_string_carries_no_facts()
    {
        var parser = new PdfParser(new System.IO.MemoryStream(Encoding.ASCII.GetBytes("(HEP)")));
        Assert.Null(Assert.IsType<PdfString>(parser.ReadObject()).HexFacts);
    }

    [Fact]
    public void An_in_memory_string_carries_no_facts()
    {
        // Nothing synthesised in memory has a written form to be wrong about, so 6.1.6 cannot
        // constrain it and the rule in Task 3 must stay silent.
        Assert.Null(PdfString.FromText("hello").HexFacts);
        Assert.Null(PdfString.FromByteLiteral("hello").HexFacts);
        Assert.Null(new PdfString([0x41, 0x42]).HexFacts);
    }

    [Fact]
    public void Facts_stay_out_of_equality()
    {
        // Two strings with the same bytes must remain equal however they were written, or dictionary
        // keys and de-duplication shift underneath unrelated code.
        var withFacts = new PdfString([0x48], PdfStringFormat.Hexadecimal, new HexStringFacts(5, true));
        var without = new PdfString([0x48]);

        Assert.Equal(withFacts, without);
        Assert.Equal(withFacts.GetHashCode(), without.GetHashCode());
    }
}
