using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

/// <summary>B-1: a Type0 font with a registered Adobe ordering and no /ToUnicode decodes through
/// code→CID (embedded CMap / Identity / UCS2 shortcut) → CID→Unicode (bundled tables). ToUnicode,
/// when present, still wins; Adobe-Identity stays on the old fallback.</summary>
public class Type0FontRegistryDecodeTests
{
    private static PdfDictionary CidSystemInfo(string registry, string ordering) => new()
    {
        [new PdfName("Registry")] = PdfString.FromByteLiteral(registry),
        [new PdfName("Ordering")] = PdfString.FromByteLiteral(ordering),
        [new PdfName("Supplement")] = new PdfInteger(4),
    };

    private static PdfDictionary Descendant(string ordering) => new()
    {
        [new PdfName("Type")] = new PdfName("Font"),
        [new PdfName("Subtype")] = new PdfName("CIDFontType0"),
        [new PdfName("BaseFont")] = new PdfName("Test-" + ordering),
        [new PdfName("CIDSystemInfo")] = CidSystemInfo("Adobe", ordering),
    };

    private static Type0Font Build(string ordering, PdfObject encoding, PdfStream? toUnicode = null)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("Type0"),
            [new PdfName("BaseFont")] = new PdfName("Test-" + ordering),
            [new PdfName("Encoding")] = encoding,
            [new PdfName("DescendantFonts")] = new PdfArray { Descendant(ordering) },
        };
        if (toUnicode is not null) dict[new PdfName("ToUnicode")] = toUnicode;
        return (Type0Font)PdfFont.Create(dict)!;
    }

    // A CID with a real mapping in the bundled table, found dynamically — no hand-remembered CIDs.
    private static int FirstMappedCid(string ordering)
    {
        for (var cid = 1; cid < 1000; cid++)
            if (AdobeCidToUnicode.Lookup(ordering, cid) is not null) return cid;
        throw new InvalidOperationException($"no mapped CID under 1000 for {ordering}?");
    }

    [Fact]
    public void EmbeddedCMapEncoding_DecodesThroughCodeToCidToUnicode()
    {
        int cid = FirstMappedCid("Japan1");
        var encStream = new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes($"1 begincidchar\n<0042> {cid}\nendcidchar\n"));
        Type0Font font = Build("Japan1", encStream);

        Assert.Equal(AdobeCidToUnicode.Lookup("Japan1", cid), font.DecodeCharacter(0x0042));
    }

    [Fact]
    public void IdentityH_UsesCodeAsCid()
    {
        int cid = FirstMappedCid("Korea1");
        Type0Font font = Build("Korea1", new PdfName("Identity-H"));

        Assert.Equal(AdobeCidToUnicode.Lookup("Korea1", cid), font.DecodeCharacter(cid));
    }

    [Fact]
    public void Ucs2Encoding_ReturnsTheCodeDirectly()
    {
        Type0Font font = Build("Japan1", new PdfName("UniJIS-UCS2-H"));
        Assert.Equal("あ", font.DecodeCharacter(0x3042));   // the code IS UCS-2
    }

    [Fact]
    public void ToUnicode_StillWins_OverTheRegistryPath()
    {
        int cid = FirstMappedCid("Japan1");
        var toUnicode = new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes($"1 beginbfchar\n<{cid:X4}> <005A>\nendbfchar\n"));   // → "Z"
        Type0Font font = Build("Japan1", new PdfName("Identity-H"), toUnicode);

        Assert.Equal("Z", font.DecodeCharacter(cid));
    }

    [Fact]
    public void AdobeIdentityOrdering_KeepsTheOldFallback()
    {
        Type0Font font = Build("Identity", new PdfName("Identity-H"));
        Assert.Equal(char.ConvertFromUtf32(0x0041), font.DecodeCharacter(0x0041));
    }

    [Fact]
    public void UnmappableCode_FallsThroughToTheOldFallback()
    {
        var encStream = new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes("1 begincidchar\n<0042> 5\nendcidchar\n"));
        Type0Font font = Build("Japan1", encStream);
        // 0x0999 has no entry in the embedded CMap → no CID → old fallback.
        Assert.Equal(char.ConvertFromUtf32(0x0999), font.DecodeCharacter(0x0999));
    }
}
