using System;
using System.IO;
using FontParser.Tables.Cff.Type1;
using Xunit;

namespace FontParser.Tests;

/// <summary>
/// Regression tests for CID-keyed CFF (FDSelect) parsing. Fixtures are untracked (commercial Kazuraki
/// font data) so each test is File.Exists-guarded.
/// </summary>
public class Type1TableCidTests
{
    private static string Fix(string name) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "Cff", name));

    [Theory]
    [InlineData("Kazuraki-CID-fail.cff")] // single-range FDSelect format 3 — used to throw at Ranges[1]
    [InlineData("Kazuraki-CID-96g.cff")]  // larger CID font — must keep parsing
    public void Type1Table_ParsesCidFdSelect_AndResolvesEveryGlyph(string fixture)
    {
        string path = Fix(fixture);
        if (!File.Exists(path)) return; // untracked fixture; guarded

        var t = new Type1Table(File.ReadAllBytes(path)); // must NOT throw

        Assert.True(t.IsCid);
        Assert.True(t.RawCharStrings.Count > 0);
        // Every glyph must resolve to a font dict via FDSelect (no out-of-range), so every outline parses.
        for (var g = 0; g < t.RawCharStrings.Count; g++)
            Assert.NotNull(t.GetGlyphOutline(g));
    }
}
