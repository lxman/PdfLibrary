using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

public class FontMetadataIndexTests
{
    private static FontFaceRecord Face(string ps, string family, bool italic, bool bold, int index = 0) =>
        new("f.ttf", index, ps, [family], family, italic ? "Italic" : "Regular", italic, bold);

    [Fact]
    public void PickBest_prefers_the_face_matching_both_style_bits()
    {
        FontFaceRecord[] faces =
        [
            Face("F-Regular", "F", italic: false, bold: false, index: 0),
            Face("F-Bold", "F", italic: false, bold: true, index: 1),
            Face("F-Italic", "F", italic: true, bold: false, index: 2),
            Face("F-BoldItalic", "F", italic: true, bold: true, index: 3),
        ];

        Assert.Equal("F-Italic", FontMetadataIndex.PickBest(faces, bold: false, italic: true)!.PostScriptName);
        Assert.Equal("F-BoldItalic", FontMetadataIndex.PickBest(faces, bold: true, italic: true)!.PostScriptName);
        Assert.Equal("F-Regular", FontMetadataIndex.PickBest(faces, bold: false, italic: false)!.PostScriptName);
    }

    [Fact]
    public void PickBest_degrades_rather_than_failing_when_the_style_is_absent()
    {
        // Italic requested, none available: keep the regular rather than returning nothing.
        FontFaceRecord[] faces =
        [
            Face("F-Regular", "F", italic: false, bold: false, index: 0),
            Face("F-Bold", "F", italic: false, bold: true, index: 1),
        ];

        Assert.Equal("F-Regular", FontMetadataIndex.PickBest(faces, bold: false, italic: true)!.PostScriptName);
    }

    [Fact]
    public void PickBest_breaks_ties_on_lowest_face_index()
    {
        // Indistinguishable faces must resolve the way they did before this index existed.
        FontFaceRecord[] faces = [Face("B", "F", false, false, index: 3), Face("A", "F", false, false, index: 1)];

        Assert.Equal("A", FontMetadataIndex.PickBest(faces, bold: false, italic: false)!.PostScriptName);
    }

    [Fact]
    public void PickBest_of_nothing_is_null()
    {
        Assert.Null(FontMetadataIndex.PickBest([], bold: false, italic: false));
    }

    [Fact]
    public void Indexes_the_real_system_fonts_by_postscript_name()
    {
        var index = new FontMetadataIndex(SystemFontLocator.DefaultFontDirectories());
        Assert.SkipWhen(index.Faces.Count == 0, "no system fonts on this machine");

        // Measured on all three CI machines: 100% of faces carry a PostScript name.
        Assert.All(index.Faces, f => Assert.NotEmpty(f.PostScriptName));

        FontFaceRecord first = index.Faces[0];
        Assert.Same(first, index.ByPostScriptName(first.PostScriptName));
    }

    [Fact]
    public void Missing_directories_are_skipped_not_thrown()
    {
        var index = new FontMetadataIndex(["/definitely/not/a/real/path", ""]);
        Assert.Empty(index.Faces);
    }

    [Fact]
    public void PickFaceIndex_of_a_single_face_font_is_zero()
    {
        byte[] bare = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

        Assert.Equal(0, FontMetadataIndex.PickFaceIndex(bare, bold: false, italic: true));
    }

    [Fact]
    public void PickFaceIndex_of_malformed_bytes_is_zero_not_an_exception()
    {
        Assert.Equal(0, FontMetadataIndex.PickFaceIndex([0x00, 0x01], bold: false, italic: false));
    }
}
