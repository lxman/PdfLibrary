using System.Text;
using PdfLibrary.Fonts;
using Xunit;

namespace PdfLibrary.Tests.Fonts;

public class ToUnicodeCMapWriterTests
{
    // The writer's output must be readable by the reader that already ships. If these two ever
    // disagree, a fix will report success and preflight will still fail, with nothing to point at.
    [Fact]
    public void Write_RoundTripsThroughToUnicodeCMap()
    {
        var map = new Dictionary<int, string>
        {
            [0x41] = "A",
            [0x42] = "B",
            [0xFF] = "ü",
        };

        ToUnicodeCMap parsed = ToUnicodeCMap.Parse(ToUnicodeCMapWriter.Write(map, ToUnicodeCodespace.TwoByte));

        Assert.Equal("A", parsed.Lookup(0x41));
        Assert.Equal("B", parsed.Lookup(0x42));
        Assert.Equal("ü", parsed.Lookup(0xFF));
    }

    [Theory]
    [InlineData(ToUnicodeCodespace.OneByte)]
    [InlineData(ToUnicodeCodespace.TwoByte)]
    public void Write_RoundTripsAMultiCharacterMapping(ToUnicodeCodespace codespace)
    {
        // A ligature code maps to more than one character — the bfchar destination is a UTF-16BE
        // string, not a single code unit. The destination encoding is independent of the source
        // code width, so this must hold under both codespaces.
        var map = new Dictionary<int, string> { [0x01] = "fi" };

        Assert.Equal("fi", ToUnicodeCMap.Parse(ToUnicodeCMapWriter.Write(map, codespace)).Lookup(0x01));
    }

    [Theory]
    [InlineData(ToUnicodeCodespace.OneByte)]
    [InlineData(ToUnicodeCodespace.TwoByte)]
    public void Write_RoundTripsANonBmpMapping(ToUnicodeCodespace codespace)
    {
        // U+1D400 MATHEMATICAL BOLD CAPITAL A — a surrogate pair in UTF-16BE. Same independence
        // from source-code width as the multi-character case above.
        var map = new Dictionary<int, string> { [0x02] = "\U0001D400" };

        Assert.Equal("\U0001D400", ToUnicodeCMap.Parse(ToUnicodeCMapWriter.Write(map, codespace)).Lookup(0x02));
    }

    [Fact]
    public void Write_EmitsTheRequiredCMapStructure()
    {
        string text = Encoding.ASCII.GetString(
            ToUnicodeCMapWriter.Write(new Dictionary<int, string> { [0x41] = "A" }, ToUnicodeCodespace.TwoByte));

        Assert.Contains("/CMapType 2", text);
        Assert.Contains("begincmap", text);
        Assert.Contains("endcmap", text);
        Assert.Contains("beginbfchar", text);
        Assert.Contains("begincodespacerange", text);
    }

    [Fact]
    public void Write_OneByteCodespace_EmitsOneByteRangeAndTwoHexDigitCodes()
    {
        string text = Encoding.ASCII.GetString(
            ToUnicodeCMapWriter.Write(new Dictionary<int, string> { [0x41] = "A" }, ToUnicodeCodespace.OneByte));

        Assert.Contains("<00> <FF>", text);
        Assert.Contains("<41> <0041>", text);
        Assert.DoesNotContain("<0041> <0041>", text);

        ToUnicodeCMap parsed = ToUnicodeCMap.Parse(
            ToUnicodeCMapWriter.Write(new Dictionary<int, string> { [0x41] = "A" }, ToUnicodeCodespace.OneByte));
        Assert.Equal("A", parsed.Lookup(0x41));
    }

    [Fact]
    public void Write_TwoByteCodespace_EmitsTwoByteRangeAndFourHexDigitCodes()
    {
        string text = Encoding.ASCII.GetString(
            ToUnicodeCMapWriter.Write(new Dictionary<int, string> { [0x41] = "A" }, ToUnicodeCodespace.TwoByte));

        Assert.Contains("<0000> <FFFF>", text);
        Assert.Contains("<0041> <0041>", text);

        ToUnicodeCMap parsed = ToUnicodeCMap.Parse(
            ToUnicodeCMapWriter.Write(new Dictionary<int, string> { [0x41] = "A" }, ToUnicodeCodespace.TwoByte));
        Assert.Equal("A", parsed.Lookup(0x41));
    }

    [Fact]
    public void Write_ChunksBeyondTheHundredEntryLimit()
    {
        // ISO 32000-1 9.10.3: at most 100 entries per bfchar section.
        var map = new Dictionary<int, string>();
        for (var i = 1; i <= 250; i++) map[i] = ((char)(0x2000 + i)).ToString();

        string text = Encoding.ASCII.GetString(ToUnicodeCMapWriter.Write(map, ToUnicodeCodespace.TwoByte));

        Assert.Equal(3, text.Split("beginbfchar").Length - 1);

        ToUnicodeCMap parsed = ToUnicodeCMap.Parse(ToUnicodeCMapWriter.Write(map, ToUnicodeCodespace.TwoByte));
        Assert.Equal(((char)(0x2000 + 137)).ToString(), parsed.Lookup(137));
    }

    [Fact]
    public void Write_ChunksBeyondTheHundredEntryLimit_UnderOneByteCodespace()
    {
        // A one-byte codespace caps codes at 0..255, so this fixture is kept to 200 entries (under
        // 256) rather than reusing the 250-entry TwoByte fixture, which is not representable here.
        var map = new Dictionary<int, string>();
        for (var i = 1; i <= 200; i++) map[i] = ((char)(0x2000 + i)).ToString();

        string text = Encoding.ASCII.GetString(ToUnicodeCMapWriter.Write(map, ToUnicodeCodespace.OneByte));

        Assert.Equal(2, text.Split("beginbfchar").Length - 1);

        ToUnicodeCMap parsed = ToUnicodeCMap.Parse(ToUnicodeCMapWriter.Write(map, ToUnicodeCodespace.OneByte));
        Assert.Equal(((char)(0x2000 + 137)).ToString(), parsed.Lookup(137));
    }

    [Fact]
    public void Write_ThrowsOnEmptyDestination()
    {
        // A dropped code becomes an unmapped code that still fails preflight, while the fix reports
        // success — the exact failure shape this increment exists to avoid. Must throw, not drop.
        var map = new Dictionary<int, string> { [0x41] = "A", [0x42] = "" };

        Assert.Throws<ArgumentException>(() => ToUnicodeCMapWriter.Write(map, ToUnicodeCodespace.TwoByte));
    }
}
