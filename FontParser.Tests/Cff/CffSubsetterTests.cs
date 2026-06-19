using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FontParser.Subsetting.Cff;
using FontParser.Tables.Cff.Type1;
using Xunit;

namespace FontParser.Tests;

/// <summary>
/// CFF subsetter round-trip tests. Fixtures are untracked (commercial AbadiMT font data) so each
/// test is File.Exists-guarded; run locally after extracting the .cff fixtures.
/// </summary>
public class CffSubsetterTests
{
    private static string Fix(string name) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "Cff", name));

    [Fact]
    public void Type1Table_ExposesSubsetInputs_ForAbadiMT()
    {
        string path = Fix("AbadiMT-CondensedLight.cff");
        if (!File.Exists(path)) return; // untracked fixture; guarded

        var t = new Type1Table(File.ReadAllBytes(path));

        Assert.False(t.IsCid);
        Assert.True(t.RawCharStrings.Count > 1);
        Assert.NotEmpty(t.RawTopDict);
        Assert.NotEmpty(t.RawPrivateDict);
        Assert.NotNull(t.CharSet);
    }

    [Fact]
    public void Subset_Type1C_KeepsRequestedGlyphs_BlanksRest_Smaller()
    {
        string path = Fix("AbadiMT-CondensedLight.cff");
        if (!File.Exists(path)) return; // untracked fixture; guarded

        byte[] src = File.ReadAllBytes(path);
        var srcTable = new Type1Table(src);
        int numGlyphs = srcTable.RawCharStrings.Count;
        var keep = new HashSet<int> { 0, 1, 2, 3, 4, 5 };

        byte[] sub = CffSubsetter.Subset(srcTable, keep);
        var subTable = new Type1Table(sub); // must re-parse cleanly

        Assert.Equal(numGlyphs, subTable.RawCharStrings.Count); // GID numbering preserved

        // Every retained glyph's outline is byte-identical -> same command count and bounding box.
        foreach (int g in keep)
        {
            var a = srcTable.GetGlyphOutline(g);
            var b = subTable.GetGlyphOutline(g);
            Assert.Equal(a?.Commands.Count ?? 0, b?.Commands.Count ?? 0);
            Assert.Equal(a?.MinX, b?.MinX);
            Assert.Equal(a?.MaxX, b?.MaxX);
        }

        // A glyph that originally had a real outline but was NOT kept is now endchar-only, so it draws
        // nothing — its command count drops to far fewer than the original.
        int unused = Enumerable.Range(0, numGlyphs)
            .First(g => !keep.Contains(g) && (srcTable.GetGlyphOutline(g)?.Commands.Count ?? 0) > 2);
        int origCommands = srcTable.GetGlyphOutline(unused)!.Commands.Count;
        int blankedCommands = subTable.GetGlyphOutline(unused)?.Commands.Count ?? 0;
        Assert.True(blankedCommands < origCommands,
            $"blanked glyph {unused}: {blankedCommands} commands not fewer than original {origCommands}");

        Assert.True(sub.Length < src.Length, $"subset {sub.Length} not smaller than source {src.Length}");
    }
}
