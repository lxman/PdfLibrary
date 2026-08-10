using System.IO;
using System.IO.Compression;
using System.Linq;
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// The nine vendored Adobe resources must be present IN THE ASSEMBLY, not merely on disk. A missing
/// LogicalName in the csproj produces a null stream at runtime and a silent fallback — exactly the
/// failure this pins.
/// </summary>
public class AfmResourceTests
{
    public static TheoryData<string> Faces =>
    [
        "Helvetica", "Helvetica-Bold",
        "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic",
        "Symbol", "ZapfDingbats",
    ];

    [Theory]
    [MemberData(nameof(Faces))]
    public void EachVendoredAfmIsEmbeddedAndDecompresses(string face)
    {
        Stream? raw = typeof(Standard14Metrics).Assembly
            .GetManifestResourceStream($"PdfLibrary.Resources.Afm.{face}.afm.gz");
        Assert.NotNull(raw);

        using var gz = new GZipStream(raw!, CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        string text = reader.ReadToEnd();

        Assert.StartsWith("StartFontMetrics", text);
        Assert.Contains("Copyright (c)", text);
        Assert.Contains("Adobe Systems", text);
        Assert.Contains("StartCharMetrics", text);
    }

    [Fact]
    public void TheLicenceShipsInsideTheAssembly()
    {
        // APAFML: "the AFM files are not distributed without this file". Shipping it in the repo is
        // not enough — the assembly is what gets distributed.
        using Stream? s = typeof(Standard14Metrics).Assembly
            .GetManifestResourceStream("PdfLibrary.Resources.Afm.LICENSE-Adobe-Core14-AFM.txt");
        Assert.NotNull(s);
        using var reader = new StreamReader(s!);
        string text = reader.ReadToEnd();
        Assert.Contains("Adobe Systems Incorporated", text);
        Assert.Contains("may be used, copied, and distributed", text);
    }
}
