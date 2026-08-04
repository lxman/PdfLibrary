using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

public class LadderStep1StyleTests
{
    /// <summary>Two faces of one family in a temp directory: an upright and an italic, with distinct
    /// PostScript names. Synthesised rather than taken from the system on purpose — the slice-1
    /// ladder tests used DefaultFontDirectories() and so could never reach step 1's short-circuit on
    /// a CI box, which is exactly why this defect survived them.</summary>
    private static string WriteFamily(string dir)
    {
        Directory.CreateDirectory(dir);
        // macStyle bit 1 = italic. Name IDs: 1 = family, 2 = subfamily, 6 = PostScript name.
        File.WriteAllBytes(Path.Combine(dir, "upright.ttf"), SfntFixtures.Sfnt(0,
            (3, 0x409, 1, "Arial"), (3, 0x409, 2, "Regular"), (3, 0x409, 6, "ArialMT")));
        File.WriteAllBytes(Path.Combine(dir, "italic.ttf"), SfntFixtures.Sfnt(0x2,
            (3, 0x409, 1, "Arial"), (3, 0x409, 2, "Italic"), (3, 0x409, 6, "Arial-ItalicMT")));
        return dir;
    }

    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "ladder-step1-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void An_explicit_italic_request_gets_the_italic_sibling_of_an_exact_upright_hit()
    {
        string dir = WriteFamily(TempDir());
        try
        {
            var locator = new SystemFontLocator([dir]);
            FontMatch? m = locator.Resolve(
                new FontRequest("ArialMT", false, true, ExplicitItalic: true));

            Assert.NotNull(m);
            Assert.Equal(File.ReadAllBytes(Path.Combine(dir, "italic.ttf")), m!.Data);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void An_exact_hit_that_already_agrees_is_returned_unchanged()
    {
        string dir = WriteFamily(TempDir());
        try
        {
            var locator = new SystemFontLocator([dir]);
            FontMatch? m = locator.Resolve(new FontRequest("ArialMT", false, false));

            Assert.NotNull(m);
            Assert.Equal(File.ReadAllBytes(Path.Combine(dir, "upright.ttf")), m!.Data);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void An_exact_hit_whose_family_has_no_better_face_is_kept_not_dropped()
    {
        string dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "upright.ttf"), SfntFixtures.Sfnt(0,
                (3, 0x409, 1, "Arial"), (3, 0x409, 2, "Regular"), (3, 0x409, 6, "ArialMT")));
            var locator = new SystemFontLocator([dir]);

            FontMatch? m = locator.Resolve(
                new FontRequest("ArialMT", false, true, ExplicitItalic: true));

            Assert.NotNull(m);
            Assert.Equal(File.ReadAllBytes(Path.Combine(dir, "upright.ttf")), m!.Data);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_StemV_style_inference_cannot_displace_an_exact_hit()
    {
        // The whole point of the two-pair design. Bold is set in the MERGED pair (as a StemV >= 120
        // inference would set it) but not in the explicit pair; the named face must survive.
        string dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "upright.ttf"), SfntFixtures.Sfnt(0,
                (3, 0x409, 1, "Arial"), (3, 0x409, 2, "Regular"), (3, 0x409, 6, "ArialMT")));
            File.WriteAllBytes(Path.Combine(dir, "bold.ttf"), SfntFixtures.Sfnt(0x1,
                (3, 0x409, 1, "Arial"), (3, 0x409, 2, "Bold"), (3, 0x409, 6, "Arial-BoldMT")));
            var locator = new SystemFontLocator([dir]);

            FontMatch? m = locator.Resolve(new FontRequest("ArialMT", true, false));

            Assert.NotNull(m);
            Assert.Equal(File.ReadAllBytes(Path.Combine(dir, "upright.ttf")), m!.Data);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void An_explicit_style_token_in_the_name_alone_counts_as_explicit()
    {
        // No descriptor at all — the request's explicit flags are both false. The signal comes only
        // from the "-Italic" token in the /BaseFont, which SystemFontLocator re-derives via
        // Base35Aliases.Split. The fixture is a MISLABELLED font, which is what makes this test
        // bite: "Fam-Italic" is an exact PostScript hit, but that face's own head macStyle says
        // upright, so name-derived explicit style is the only thing that can reject it. Mislabelled
        // style bits are common enough in the wild to be worth honouring the name over.
        string dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            // PostScript name claims Italic; macStyle 0 and subfamily "Regular" say otherwise.
            File.WriteAllBytes(Path.Combine(dir, "mislabelled.ttf"), SfntFixtures.Sfnt(0,
                (3, 0x409, 1, "Fam"), (3, 0x409, 2, "Regular"), (3, 0x409, 6, "Fam-Italic")));
            // A genuinely italic sibling in the same family.
            File.WriteAllBytes(Path.Combine(dir, "trueitalic.ttf"), SfntFixtures.Sfnt(0x2,
                (3, 0x409, 1, "Fam"), (3, 0x409, 2, "Italic"), (3, 0x409, 6, "Fam-Oblique")));
            var locator = new SystemFontLocator([dir]);

            FontMatch? m = locator.Resolve(new FontRequest("Fam-Italic", false, true));

            Assert.NotNull(m);
            Assert.Equal(File.ReadAllBytes(Path.Combine(dir, "trueitalic.ttf")), m!.Data);
        }
        finally { Directory.Delete(dir, true); }
    }
}
