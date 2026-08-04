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
        string dir = TempDir();
        try
        {
            WriteFamily(dir);
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
        string dir = TempDir();
        try
        {
            WriteFamily(dir);
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

    [Fact]
    public void Equally_scoring_siblings_break_ties_by_ordinal_PostScriptName_not_enumeration_order()
    {
        // Review finding: BetterStyledSibling originally kept the first tied candidate it encountered,
        // which meant Directory.EnumerateFiles order — not stable across machines — decided the
        // winner among equally-scored siblings. Filenames here are chosen so alphabetical enumeration
        // meets a HIGHER-ordinal PostScript name before a LOWER-ordinal one: "1-a.ttf" (PostScript
        // "Fam2-Zeta") enumerates before "2-b.ttf" (PostScript "Fam2-Alpha"). The old first-seen code
        // would return Zeta; the fix must return Alpha regardless of file order.
        string dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "0-upright.ttf"), SfntFixtures.Sfnt(0,
                (3, 0x409, 1, "Fam2"), (3, 0x409, 2, "Regular"), (3, 0x409, 6, "Fam2-Regular")));
            File.WriteAllBytes(Path.Combine(dir, "1-a.ttf"), SfntFixtures.Sfnt(0x2,
                (3, 0x409, 1, "Fam2"), (3, 0x409, 2, "Italic"), (3, 0x409, 6, "Fam2-Zeta")));
            File.WriteAllBytes(Path.Combine(dir, "2-b.ttf"), SfntFixtures.Sfnt(0x2,
                (3, 0x409, 1, "Fam2"), (3, 0x409, 2, "Italic"), (3, 0x409, 6, "Fam2-Alpha")));
            var locator = new SystemFontLocator([dir]);

            FontMatch? m = locator.Resolve(
                new FontRequest("Fam2-Regular", false, true, ExplicitItalic: true));

            Assert.NotNull(m);
            Assert.Equal(File.ReadAllBytes(Path.Combine(dir, "2-b.ttf")), m!.Data);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_sibling_that_improves_the_explicit_score_but_regresses_the_merged_score_is_rejected()
    {
        // Review finding: explicit implies merged pointwise, but not the reverse — a descriptor can
        // set StemV (feeding the merged pair) without an explicit bold flag. Here the exact hit is
        // bold+italic; the request's explicit pair is italic-only (bold false), so a non-bold italic
        // sibling scores higher explicitly (2 vs the hit's 1) — but under the MERGED pair (bold true
        // via StemV-style inference, italic true) the hit scores 2 and that same sibling scores only
        // 1. Swapping to the sibling would make step 1 return a lower-scoring face than today under
        // the metric steps 2/3 use, which the ladder's own contract forbids. The hit must survive.
        string dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "boldoblique.ttf"), SfntFixtures.Sfnt(0x3,
                (3, 0x409, 1, "Foo"), (3, 0x409, 2, "BoldOblique"), (3, 0x409, 6, "Foo-Oblique")));
            File.WriteAllBytes(Path.Combine(dir, "italic.ttf"), SfntFixtures.Sfnt(0x2,
                (3, 0x409, 1, "Foo"), (3, 0x409, 2, "Italic"), (3, 0x409, 6, "Foo-Italic")));
            var locator = new SystemFontLocator([dir]);

            FontMatch? m = locator.Resolve(new FontRequest(
                "Foo-Oblique", Bold: true, Italic: true, ExplicitBold: false, ExplicitItalic: true));

            Assert.NotNull(m);
            Assert.Equal(File.ReadAllBytes(Path.Combine(dir, "boldoblique.ttf")), m!.Data);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_request_stating_no_style_at_all_keeps_the_exact_hit_even_when_its_own_bits_disagree()
    {
        // Pins the `explicitBold || explicitItalic` gate, which nothing else in the suite held in
        // place: delete it and the other 2857 tests still pass. It IS load-bearing, and on the
        // overwhelmingly common path — a /BaseFont with no style token and no descriptor, which the
        // design spec promises is byte-identical to before this branch.
        //
        // The fixture is the case that makes the gate bite: "Foo" is an exact PostScript hit whose
        // OWN head macStyle says italic, and nothing in the request says otherwise (no token in the
        // name for Base35Aliases.Split to find, no descriptor flags), so the explicit pair is empty.
        // Ungated, the hit would score 1 against the upright request and be swapped for the upright
        // "Foo-Regular" sibling scoring 2 — a silent substitution the document never asked for.
        string dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "named.ttf"), SfntFixtures.Sfnt(0x2,
                (3, 0x409, 1, "Foo"), (3, 0x409, 2, "Italic"), (3, 0x409, 6, "Foo")));
            File.WriteAllBytes(Path.Combine(dir, "upright.ttf"), SfntFixtures.Sfnt(0,
                (3, 0x409, 1, "Foo"), (3, 0x409, 2, "Regular"), (3, 0x409, 6, "Foo-Regular")));
            var locator = new SystemFontLocator([dir]);

            FontMatch? m = locator.Resolve(new FontRequest("Foo", false, false));

            Assert.NotNull(m);
            Assert.Equal(File.ReadAllBytes(Path.Combine(dir, "named.ttf")), m!.Data);
        }
        finally { Directory.Delete(dir, true); }
    }
}
