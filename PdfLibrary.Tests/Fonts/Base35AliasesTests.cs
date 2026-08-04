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
        // ORDER IS BEHAVIOUR: it encodes substitution preference, so this asserts the exact
        // sequence. Assert.Contains would let a reordered table pass silently and change which
        // font a document actually renders with.
        Assert.Equal(
            ["Nimbus Roman", "Liberation Serif", "Times New Roman", "Times", "Tinos"],
            Base35Aliases.FamiliesFor("Times"));

        Assert.Equal(
            ["C059", "Century Schoolbook L", "New Century Schoolbook", "Century Schoolbook"],
            Base35Aliases.FamiliesFor("NewCenturySchlbk"));
    }

    [Fact]
    public void An_unknown_family_aliases_to_itself()
    {
        Assert.Equal(["Garamond"], Base35Aliases.FamiliesFor("Garamond"));
    }

    [Fact]
    public void FamiliesFor_lookup_ignores_spaces_and_case()
    {
        // A /BaseFont may spell the family with spaces or in any case; all must reach the same row.
        Assert.Equal(Base35Aliases.FamiliesFor("NewCenturySchlbk"), Base35Aliases.FamiliesFor("New Century Schlbk"));
        Assert.Equal(Base35Aliases.FamiliesFor("NewCenturySchlbk"), Base35Aliases.FamiliesFor("newcenturyschlbk"));
    }

    [Fact]
    public void Null_and_empty_input_do_not_throw()
    {
        Assert.Equal("", Base35Aliases.Split(null!).Family);
        Assert.Equal("", Base35Aliases.Split("").Family);
        Assert.NotNull(Base35Aliases.FamiliesFor(null!));
        Assert.NotNull(Base35Aliases.FamiliesFor(""));
    }
}
