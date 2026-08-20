using System.Linq;
using System.Text;
using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using Xunit;

namespace PdfLibrary.Tests.Content;

/// <summary>
/// ISO 19005-2 clause 6.1.13 test 1 forbids an integer outside ±2147483647. The content parser
/// clamps such an operand to 0, which erases the evidence — so the FACT is recorded while the VALUE
/// is left exactly as it was. Leaving the value alone is what keeps the cross-platform render
/// baselines still.
///
/// <para>The top-level, primary assertions go through <see cref="PdfContentParser.Parse(byte[],out bool)"/>'s
/// out-parameter, not through the returned operators' <c>Operands</c>: a typed operator such as
/// <c>Td</c> reconstructs its <c>Operands</c> as brand-new <see cref="PdfReal"/> instances from the
/// parsed doubles, so a marked <see cref="PdfInteger"/> pushed for such an operator is unobservable
/// through <c>op.Operands</c> even though it was correctly recorded when parsed. The out-param
/// captures the fact before that reconstruction can discard it — see the overload's doc comment.</para>
/// </summary>
public class IntegerRangeFactTests
{
    private static bool Saw(string content)
    {
        PdfContentParser.Parse(Encoding.ASCII.GetBytes(content), out bool sawOutOfRangeInteger);
        return sawOutOfRangeInteger;
    }

    [Theory]
    [InlineData("0 2157483648 Td", true)]        // corpus 6-1-13-t01-fail-b, above Int32
    [InlineData("0 -2157483648 Td", true)]       // below Int32
    [InlineData("0 +2157483648 Td", true)]       // signed, above Int32
    [InlineData("0 99999999999999999999 Td", true)]  // beyond even long, still an integer literal
    public void An_oversized_integer_literal_is_reported(string content, bool expected)
    {
        Assert.Equal(expected, Saw(content));
    }

    [Theory]
    [InlineData("0 2147483647 Td")]              // exactly int.MaxValue — in range
    [InlineData("0 -2147483648 Td")]             // exactly int.MinValue — in range
    [InlineData("0 42 Td")]
    public void An_in_range_integer_is_not_reported(string content)
    {
        Assert.False(Saw(content));
    }

    [Theory]
    [InlineData("- 5 Td")]
    [InlineData("-- 5 Td")]
    [InlineData("+- 5 Td")]
    public void Malformed_text_that_is_not_an_integer_is_never_reported(string content)
    {
        // This is the false-positive guard, and the most important assertion in this task. ParseInt
        // returned 0 for these too, and reporting them would invent a 6.1.13 violation the reference
        // validator does not report.
        Assert.False(Saw(content));
    }

    [Fact]
    public void The_clamped_value_is_unchanged()
    {
        // The whole point: rendering must not move. An out-of-range operand is still 0 — checked on
        // the actual value the renderer consumes (Tx/Ty), since Td's Operands are reconstructed from
        // doubles rather than preserving the original PdfInteger.
        var op = Assert.IsType<MoveTextPositionOperator>(
            PdfContentParser.Parse(Encoding.ASCII.GetBytes("0 2157483648 Td")).Last());

        Assert.Equal(0.0, op.Tx);
        Assert.Equal(0.0, op.Ty);
    }

    [Fact]
    public void The_mark_stays_out_of_equality()
    {
        Assert.Equal(new PdfInteger(0), new PdfInteger(0, sourceOutOfInt32Range: true));
        Assert.Equal(new PdfInteger(0).GetHashCode(), new PdfInteger(0, true).GetHashCode());
    }

    // Array and dictionary operands survive into op.Operands unchanged (no reconstruction), so the
    // mark stays observable per-object through the normal Parse(byte[]) overload for these two sites.

    [Fact]
    public void Site_2_array_operand()
    {
        var array = Assert.IsType<PdfArray>(
            PdfContentParser.Parse(Encoding.ASCII.GetBytes("BT [(x) 2157483648 (y)] TJ ET"))
                .SelectMany(op => op.Operands).First(o => o is PdfArray));

        Assert.True(array.OfType<PdfInteger>().Single().SourceOutOfInt32Range);
    }

    [Fact]
    public void Site_3_dictionary_operand()
    {
        var dict = Assert.IsType<PdfDictionary>(
            PdfContentParser.Parse(Encoding.ASCII.GetBytes("/OC <</N 2157483648>> BDC EMC"))
                .SelectMany(op => op.Operands).First(o => o is PdfDictionary));

        Assert.True(Assert.IsType<PdfInteger>(dict[new PdfName("N")]).SourceOutOfInt32Range);
    }

    [Fact]
    public void Site_4_inline_image_dictionary()
    {
        // Inline-image parameters are also observable per-object (ParseInlineImage does not
        // reconstruct), but this fact never reaches the out-param above: the top-level parse loop
        // only pushes onto the operand stack for tokens outside BI...EI, and ParseInlineImage never
        // ORs into sawOutOfRangeInteger. This is a deliberate, already-documented under-report — the
        // out-param covers the top-level operand-stack site, not every site MakeInteger touches.
        PdfOperator inline = PdfContentParser
            .Parse(Encoding.ASCII.GetBytes("BI /W 1 /H 1 /BPC 8 /CS /G /Foo 2157483648 ID \u0000 EI"))
            .First(op => op is InlineImageOperator);

        var image = Assert.IsType<InlineImageOperator>(inline);
        Assert.True(Assert.IsType<PdfInteger>(image.Parameters[new PdfName("Foo")]).SourceOutOfInt32Range);
    }
}
