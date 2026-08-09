using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

public class FontDescriptorMetricsTests
{
    private static byte[] RealFont(string family = "Arial")
    {
        FontMatch? match = SystemFontLocator.Default.Resolve(
            new FontRequest(family, Bold: false, Italic: false));
        Assert.SkipWhen(match is null, $"No {family} on this machine.");
        ClassifiedProgram? classified = FontProgramClassifier.Classify(match!.Data, match.FaceIndex);
        Assert.SkipWhen(classified is null, $"{family} did not classify.");
        return classified!.Program;
    }

    [Fact]
    public void Computes_a_plausible_descriptor_from_a_real_font()
    {
        byte[] program = RealFont();

        FontDescriptorValues? values = FontDescriptorMetrics.Compute(program, FontProgramFormat.TrueType);

        Assert.NotNull(values);
        Assert.Equal(4, values.FontBBox.Length);
        Assert.True(values.FontBBox[0] < values.FontBBox[2], "BBox llx must be left of urx");
        Assert.True(values.FontBBox[1] < values.FontBBox[3], "BBox lly must be below ury");
        Assert.True(values.Ascent > 0, "Ascent must be positive");
        Assert.True(values.Descent < 0, $"Descent must be negative, was {values.Descent}");
        Assert.True(values.CapHeight > 0, "CapHeight must be positive");
        Assert.InRange(values.StemV, 1, 400);
    }

    [Fact]
    public void The_values_are_in_1000_unit_glyph_space_not_raw_font_units()
    {
        // Arial's unitsPerEm is 2048. A reader that forgot to scale would report an Ascent near
        // 1854 rather than near 905, so a generous upper bound still catches the mistake.
        byte[] program = RealFont();

        FontDescriptorValues? values = FontDescriptorMetrics.Compute(program, FontProgramFormat.TrueType);

        Assert.NotNull(values);
        Assert.InRange(values.Ascent, 400, 1200);
        Assert.InRange(values.CapHeight, 400, 1100);
        Assert.InRange(values.FontBBox[3], 400, 1400);
    }

    [Fact]
    public void CapHeight_comes_from_OS2_when_the_table_provides_it()
    {
        // Not "is a number" — WHICH source answered. Arial ships a version-4 OS/2 with a real
        // sCapHeight, so a fallback here means the OS/2 read silently failed.
        byte[] program = RealFont();

        FontDescriptorValues? values = FontDescriptorMetrics.Compute(program, FontProgramFormat.TrueType);

        Assert.NotNull(values);
        Assert.InRange(values.CapHeight, 650, 750);   // Arial's cap height is ~716/1000
    }

    [Fact]
    public void StemV_reports_which_source_produced_it()
    {
        byte[] program = RealFont();

        FontDescriptorValues? values = FontDescriptorMetrics.Compute(program, FontProgramFormat.TrueType);

        Assert.NotNull(values);
        Assert.Contains(values.StemVSource, new[] { "cff-stdvw", "measured-I", "weight-class" });
    }

    [Fact]
    public void Garbage_bytes_yield_null_rather_than_a_descriptor_of_zeroes()
    {
        // A descriptor full of zeroes would be written into the file and would be worse than the
        // wrong-but-plausible one it replaced.
        Assert.Null(FontDescriptorMetrics.Compute("not a font"u8.ToArray(), FontProgramFormat.TrueType));
    }

    [Fact]
    public void StemV_source_is_measured_I_for_a_TrueType_font_not_the_weight_class_guess()
    {
        // Arial is TrueType, not CFF — there is no StdVW to read, so a real measurement of the 'I'
        // glyph's stem must answer. If this comes back "weight-class" the measured-I path silently
        // failed to find/measure the glyph.
        byte[] program = RealFont();

        FontDescriptorValues? values = FontDescriptorMetrics.Compute(program, FontProgramFormat.TrueType);

        Assert.NotNull(values);
        Assert.Equal("measured-I", values.StemVSource);
    }
}
