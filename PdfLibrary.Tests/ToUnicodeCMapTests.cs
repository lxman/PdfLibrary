using System.Text;
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests;

public class ToUnicodeCMapTests
{
    [Fact]
    public void Parse_BfChar_SingleMappings()
    {
        string cmapContent = @"
/CIDInit /ProcSet findresource begin
12 dict begin
begincmap
/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def
/CMapName /Adobe-Identity-UCS def
/CMapType 2 def
1 begincodespacerange
<00> <FF>
endcodespacerange
3 beginbfchar
<41> <0041>
<42> <0042>
<43> <0043>
endbfchar
endcmap
CMapName currentdict /CMap defineresource pop
end
end";

        byte[] data = Encoding.ASCII.GetBytes(cmapContent);
        ToUnicodeCMap cmap = ToUnicodeCMap.Parse(data);

        Assert.Equal("A", cmap.Lookup(0x41));
        Assert.Equal("B", cmap.Lookup(0x42));
        Assert.Equal("C", cmap.Lookup(0x43));
    }

    [Fact]
    public void Parse_BfRange_SequentialMappings()
    {
        string cmapContent = @"
beginbfrange
<0020> <007E> <0020>
endbfrange
";

        byte[] data = Encoding.ASCII.GetBytes(cmapContent);
        ToUnicodeCMap cmap = ToUnicodeCMap.Parse(data);

        // Test space through tilde (basic ASCII printable)
        Assert.Equal(" ", cmap.Lookup(0x20));
        Assert.Equal("A", cmap.Lookup(0x41));
        Assert.Equal("Z", cmap.Lookup(0x5A));
        Assert.Equal("a", cmap.Lookup(0x61));
        Assert.Equal("z", cmap.Lookup(0x7A));
        Assert.Equal("~", cmap.Lookup(0x7E));
    }

    [Fact]
    public void Parse_BfRange_ArrayMappings()
    {
        string cmapContent = @"
beginbfrange
<0041> <0043> [<0391> <0392> <0393>]
endbfrange
";

        byte[] data = Encoding.ASCII.GetBytes(cmapContent);
        ToUnicodeCMap cmap = ToUnicodeCMap.Parse(data);

        // Map A, B, C to Greek Alpha, Beta, Gamma
        Assert.Equal("Α", cmap.Lookup(0x41));
        Assert.Equal("Β", cmap.Lookup(0x42));
        Assert.Equal("Γ", cmap.Lookup(0x43));
    }

    [Fact]
    public void Parse_MultipleBfCharBlocks()
    {
        string cmapContent = @"
2 beginbfchar
<41> <0041>
<42> <0042>
endbfchar
2 beginbfchar
<43> <0043>
<44> <0044>
endbfchar
";

        byte[] data = Encoding.ASCII.GetBytes(cmapContent);
        ToUnicodeCMap cmap = ToUnicodeCMap.Parse(data);

        Assert.Equal("A", cmap.Lookup(0x41));
        Assert.Equal("B", cmap.Lookup(0x42));
        Assert.Equal("C", cmap.Lookup(0x43));
        Assert.Equal("D", cmap.Lookup(0x44));
    }

    [Fact]
    public void Parse_MixedBfCharAndBfRange()
    {
        string cmapContent = @"
2 beginbfchar
<01> <0041>
<02> <0042>
endbfchar
1 beginbfrange
<0010> <0012> <0061>
endbfrange
";

        byte[] data = Encoding.ASCII.GetBytes(cmapContent);
        ToUnicodeCMap cmap = ToUnicodeCMap.Parse(data);

        // bfchar mappings
        Assert.Equal("A", cmap.Lookup(0x01));
        Assert.Equal("B", cmap.Lookup(0x02));

        // bfrange mappings
        Assert.Equal("a", cmap.Lookup(0x10));
        Assert.Equal("b", cmap.Lookup(0x11));
        Assert.Equal("c", cmap.Lookup(0x12));
    }

    [Fact]
    public void Parse_MultiByteCharacterCodes()
    {
        string cmapContent = @"
beginbfchar
<0102> <4E00>
<0103> <4E01>
endbfchar
";

        byte[] data = Encoding.ASCII.GetBytes(cmapContent);
        ToUnicodeCMap cmap = ToUnicodeCMap.Parse(data);

        // CJK characters with 2-byte codes
        Assert.NotNull(cmap.Lookup(0x0102));
        Assert.NotNull(cmap.Lookup(0x0103));
    }

    [Fact]
    public void Parse_EmptyCMap_ReturnsValidObject()
    {
        string cmapContent = @"
begincmap
endcmap
";

        byte[] data = Encoding.ASCII.GetBytes(cmapContent);
        ToUnicodeCMap cmap = ToUnicodeCMap.Parse(data);

        Assert.NotNull(cmap);
        Assert.Null(cmap.Lookup(0x41)); // No mappings
    }

    [Fact]
    public void Lookup_UnmappedCharacter_ReturnsNull()
    {
        string cmapContent = @"
beginbfchar
<41> <0041>
endbfchar
";

        byte[] data = Encoding.ASCII.GetBytes(cmapContent);
        ToUnicodeCMap cmap = ToUnicodeCMap.Parse(data);

        Assert.Equal("A", cmap.Lookup(0x41));
        Assert.Null(cmap.Lookup(0x42)); // Not mapped
    }

    [Fact]
    public void Parse_LongHexValues_8Digits()
    {
        string cmapContent = @"
beginbfchar
<01> <0001F600>
endbfchar
";

        byte[] data = Encoding.ASCII.GetBytes(cmapContent);
        ToUnicodeCMap cmap = ToUnicodeCMap.Parse(data);

        // Emoji or other characters beyond BMP
        string result = cmap.Lookup(0x01);
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }
}
