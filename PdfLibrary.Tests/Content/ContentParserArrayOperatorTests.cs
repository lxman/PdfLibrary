using System.Text;
using PdfLibrary.Content;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Tests.Content;

// The parser must bind a TJ operator to its `[…]` array operand regardless of the whitespace between them
// (space, newline, CRLF are all legal per ISO 32000-1 §7.2). Adjacent coverage to MultiStreamContentTests,
// which covers the GWG2015 spec case where the array and its TJ were split across two content streams.
public class ContentParserArrayOperatorTests
{
    private static PdfOperator? Tj(string content)
    {
        var ops = PdfContentParser.Parse(Encoding.ASCII.GetBytes(content));
        return ops.FirstOrDefault(o => o.Name == "TJ");
    }

    [Theory]
    [InlineData("[(Hello)]TJ")]          // adjacent — the working case
    [InlineData("[(Hello)] TJ")]         // space before TJ
    [InlineData("[(Hello)] \nTJ")]       // space + newline before TJ (the GWG2015 case)
    [InlineData("[(Hello)]\r\nTJ")]      // CRLF before TJ
    public void TJ_receives_its_array_regardless_of_whitespace(string content)
    {
        PdfOperator? tj = Tj(content);
        Assert.NotNull(tj);
        Assert.Single(tj!.Operands);
        Assert.IsType<PdfArray>(tj.Operands[0]);
        Assert.Single((PdfArray)tj.Operands[0]);
    }
}
