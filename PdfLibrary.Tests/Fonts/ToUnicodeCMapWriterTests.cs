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

        ToUnicodeCMap parsed = ToUnicodeCMap.Parse(ToUnicodeCMapWriter.Write(map));

        Assert.Equal("A", parsed.Lookup(0x41));
        Assert.Equal("B", parsed.Lookup(0x42));
        Assert.Equal("ü", parsed.Lookup(0xFF));
    }

    [Fact]
    public void Write_RoundTripsAMultiCharacterMapping()
    {
        // A ligature code maps to more than one character — the bfchar destination is a UTF-16BE
        // string, not a single code unit.
        var map = new Dictionary<int, string> { [0x01] = "fi" };

        Assert.Equal("fi", ToUnicodeCMap.Parse(ToUnicodeCMapWriter.Write(map)).Lookup(0x01));
    }

    [Fact]
    public void Write_RoundTripsANonBmpMapping()
    {
        // U+1D400 MATHEMATICAL BOLD CAPITAL A — a surrogate pair in UTF-16BE.
        var map = new Dictionary<int, string> { [0x02] = "\U0001D400" };

        Assert.Equal("\U0001D400", ToUnicodeCMap.Parse(ToUnicodeCMapWriter.Write(map)).Lookup(0x02));
    }

    [Fact]
    public void Write_EmitsTheRequiredCMapStructure()
    {
        string text = Encoding.ASCII.GetString(
            ToUnicodeCMapWriter.Write(new Dictionary<int, string> { [0x41] = "A" }));

        Assert.Contains("/CMapType 2", text);
        Assert.Contains("begincmap", text);
        Assert.Contains("endcmap", text);
        Assert.Contains("beginbfchar", text);
        Assert.Contains("begincodespacerange", text);
    }

    [Fact]
    public void Write_ChunksBeyondTheHundredEntryLimit()
    {
        // ISO 32000-1 9.10.3: at most 100 entries per bfchar section.
        var map = new Dictionary<int, string>();
        for (var i = 1; i <= 250; i++) map[i] = ((char)(0x2000 + i)).ToString();

        string text = Encoding.ASCII.GetString(ToUnicodeCMapWriter.Write(map));

        Assert.Equal(3, text.Split("beginbfchar").Length - 1);

        ToUnicodeCMap parsed = ToUnicodeCMap.Parse(ToUnicodeCMapWriter.Write(map));
        Assert.Equal(((char)(0x2000 + 137)).ToString(), parsed.Lookup(137));
    }
}
