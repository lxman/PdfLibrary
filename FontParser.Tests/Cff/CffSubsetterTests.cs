using System;
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
}
