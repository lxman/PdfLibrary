using System.Reflection;
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

/// <summary>B-1: the four Adobe *-UCS2 CMaps (CID→Unicode source data) ship as embedded resources.
/// The direction inversion and lookup behavior are pinned in the tests added with AdobeCidToUnicode
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
}
