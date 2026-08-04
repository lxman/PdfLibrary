using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

public class Base35AliasesTests
{
    [Fact]
    public void Split_strips_the_subset_tag()
    {
        Assert.Equal("MyriadPro", Base35Aliases.Split("BOXDGO+MyriadPro-Regular").Family);
    }

    [Fact]
    public void Split_reads_style_from_the_name()
    {
        Assert.True(Base35Aliases.Split("NewCenturySchlbk-Italic").Italic);
        Assert.False(Base35Aliases.Split("NewCenturySchlbk-Italic").Bold);
        Assert.True(Base35Aliases.Split("Helvetica-BoldOblique").Bold);
        Assert.True(Base35Aliases.Split("Helvetica-BoldOblique").Italic);
        Assert.False(Base35Aliases.Split("Times-Roman").Italic);
    }

    [Fact]
    public void Split_treats_a_comma_as_a_style_separator()
    {
        // Windows-authored PDFs use "Arial,Bold" rather than "Arial-Bold".
        Assert.True(Base35Aliases.Split("Arial,BoldItalic").Bold);
        Assert.True(Base35Aliases.Split("Arial,BoldItalic").Italic);
        Assert.Equal("Arial", Base35Aliases.Split("Arial,BoldItalic").Family);
    }

    [Fact]
    public void NewCenturySchlbk_aliases_to_C059()
    {
        // Ghostscript Fontmap.GS: /NewCenturySchlbk-Italic /C059-Italic ;
        Assert.Contains("C059", Base35Aliases.FamiliesFor("NewCenturySchlbk"));
    }

    [Fact]
    public void Standard14_families_alias_to_their_clones_in_preference_order()
    {
        IReadOnlyList<string> times = Base35Aliases.FamiliesFor("Times");
        Assert.Contains("Nimbus Roman", times);
        Assert.Contains("Liberation Serif", times);
        Assert.Contains("Times New Roman", times);
    }

    [Fact]
    public void An_unknown_family_aliases_to_itself()
    {
        Assert.Equal(["Garamond"], Base35Aliases.FamiliesFor("Garamond"));
    }
}
