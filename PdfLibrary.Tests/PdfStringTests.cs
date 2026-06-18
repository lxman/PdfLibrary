using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Parsing;
using Xunit;

namespace PdfLibrary.Tests;

public class PdfStringTests
{
    [Fact]
    public void ToPdfString_HighBytes_RoundTripThroughLexer_PreservesBytes()
    {
        // Non-printable / high bytes are escaped as OCTAL \ddd, so the lexer reads them back
        // identically. Regression: they were emitted in DECIMAL and read as octal, corrupting any
        // byte >= 64 (e.g. UTF-16 BOM FE FF -> AC AD) on a save/optimize round-trip.
        byte[] original = [0x00, 0x07, 0x1F, 0x41, 0x7F, 0x80, 0xAC, 0xFE, 0xFF, (byte)'(', (byte)')', (byte)'\\'];
        var s = new PdfString(original);

        string serialized = s.ToPdfString();
        var lexer = new PdfLexer(new MemoryStream(Encoding.Latin1.GetBytes(serialized)));
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.String, token.Type);
        Assert.Equal(original, Encoding.Latin1.GetBytes(token.Value));
    }

    [Fact]
    public void ToPdfString_EscapesHighBytesInOctal()
    {
        var s = new PdfString([0xFE, 0xFF]);
        Assert.Equal("(\\376\\377)", s.ToPdfString()); // 0xFE = octal 376, 0xFF = octal 377
    }
}
