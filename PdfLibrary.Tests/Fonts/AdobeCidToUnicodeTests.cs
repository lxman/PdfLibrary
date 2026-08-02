using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

/// <summary>B-1: the four Adobe *-UCS2 CMaps (CID→Unicode source data) ship as embedded resources.
/// The lookup behavior is pinned in the tests added with AdobeCidToUnicode
/// itself (Task 3); this class starts with the packaging pin.</summary>
public class AdobeCidToUnicodeTests
{
    [Theory]
    [InlineData("PdfLibrary.Resources.CMaps.Adobe-Japan1-UCS2.gz")]
    [InlineData("PdfLibrary.Resources.CMaps.Adobe-Korea1-UCS2.gz")]
    [InlineData("PdfLibrary.Resources.CMaps.Adobe-GB1-UCS2.gz")]
    [InlineData("PdfLibrary.Resources.CMaps.Adobe-CNS1-UCS2.gz")]
    public void Ucs2CMapResource_IsEmbedded(string logicalName)
    {
        using var s = typeof(ToUnicodeCMap).Assembly.GetManifestResourceStream(logicalName);
        Assert.NotNull(s);
        Assert.True(s!.Length > 1000, $"{logicalName} is implausibly small ({s.Length} bytes)");
    }

    // ---- direct CID→Unicode lookup (Task 3, amended: bf* dialect, no inversion) -------------

    [Theory]
    [InlineData("Japan1")]
    [InlineData("Korea1")]
    [InlineData("GB1")]
    [InlineData("CNS1")]
    public void BundledTable_AgreesWithItsOwnFirstBfRangeEntry(string ordering)
    {
        // Ground truth from the shipped resource ITSELF: independently decompress + regex the
        // first bfrange entry (<cidLo> <cidHi> <unicodeStart>), and assert Lookup serves the
        // CID→Unicode direction for both endpoints. No hand-remembered CIDs.
        string text = DecompressResource($"PdfLibrary.Resources.CMaps.Adobe-{ordering}-UCS2.gz");
        Match m = Regex.Match(text,
            @"beginbfrange\s*<([0-9A-Fa-f]{4})>\s*<([0-9A-Fa-f]{4})>\s*<([0-9A-Fa-f]{4})>");
        Assert.True(m.Success, "no bfrange entry found in bundled resource");
        int cidLo = int.Parse(m.Groups[1].Value, NumberStyles.HexNumber);
        int cidHi = int.Parse(m.Groups[2].Value, NumberStyles.HexNumber);
        int uniStart = int.Parse(m.Groups[3].Value, NumberStyles.HexNumber);

        Assert.Equal(((char)uniStart).ToString(), AdobeCidToUnicode.Lookup(ordering, cidLo));
        Assert.Equal(((char)(uniStart + (cidHi - cidLo))).ToString(),
            AdobeCidToUnicode.Lookup(ordering, cidHi));
    }

    [Theory]
    [InlineData("Japan1")]
    [InlineData("Korea1")]
    [InlineData("GB1")]
    [InlineData("CNS1")]
    public void BundledTable_ServesACjkCodePoint(string ordering)
    {
        // Sanity that the table is genuinely CJK-bearing, not just the ASCII-range head: some CID
        // under 65536 must map into the CJK blocks (Han/Kana/Hangul, U+2E80..U+D7FF or U+F900+).
        var found = false;
        for (var cid = 1; cid < 65536 && !found; cid++)
        {
            string? u = AdobeCidToUnicode.Lookup(ordering, cid);
            if (string.IsNullOrEmpty(u)) continue;
            char c = u[0];
            if (c is >= '⺀' and <= '퟿' or >= '豈') found = true;
        }
        Assert.True(found, $"{ordering}: no CJK mapping found in the whole CID range");
    }

    [Fact]
    public void UnknownOrdering_And_UnknownCid_ReturnNull()
    {
        Assert.Null(AdobeCidToUnicode.Lookup("Identity", 34));
        Assert.Null(AdobeCidToUnicode.Lookup(null, 34));
        Assert.Null(AdobeCidToUnicode.Lookup("Japan1", int.MaxValue));
        Assert.True(AdobeCidToUnicode.IsSupportedOrdering("Japan1"));
        Assert.False(AdobeCidToUnicode.IsSupportedOrdering("Identity"));
    }

    private static string DecompressResource(string logicalName)
    {
        using Stream s = typeof(ToUnicodeCMap).Assembly.GetManifestResourceStream(logicalName)!;
        using var gz = new System.IO.Compression.GZipStream(s, System.IO.Compression.CompressionMode.Decompress);
        using var ms = new MemoryStream();
        gz.CopyTo(ms);
        return Encoding.ASCII.GetString(ms.ToArray());
    }
}
