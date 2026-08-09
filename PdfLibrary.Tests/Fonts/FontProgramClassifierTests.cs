using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

public class FontProgramClassifierTests
{
    private static byte[] WithMagic(params byte[] magic)
    {
        var data = new byte[512];
        magic.CopyTo(data, 0);
        return data;
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x01, 0x00, 0x00 }, FontProgramFormat.TrueType)]
    [InlineData(new byte[] { 0x74, 0x72, 0x75, 0x65 }, FontProgramFormat.TrueType)]
    [InlineData(new byte[] { 0x4F, 0x54, 0x54, 0x4F }, FontProgramFormat.OpenType)]
    [InlineData(new byte[] { 0x01, 0x00, 0x04, 0x01 }, FontProgramFormat.Type1C)]
    [InlineData(new byte[] { 0x80, 0x01 }, FontProgramFormat.Type1)]
    public void Classifies_by_magic(byte[] magic, FontProgramFormat expected)
    {
        ClassifiedProgram? result = FontProgramClassifier.Classify(WithMagic(magic), faceIndex: 0);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Format);
    }

    [Fact]
    public void Classifies_a_PFA_Type1_program()
    {
        byte[] pfa = "%!PS-AdobeFont-1.0: Something 001.001\n"u8.ToArray();

        ClassifiedProgram? result = FontProgramClassifier.Classify(pfa, faceIndex: 0);

        Assert.NotNull(result);
        Assert.Equal(FontProgramFormat.Type1, result.Format);
    }

    [Fact]
    public void Declines_bytes_that_are_not_a_font_program()
    {
        Assert.Null(FontProgramClassifier.Classify("not a font at all"u8.ToArray(), 0));
        Assert.Null(FontProgramClassifier.Classify([], 0));
    }

    [Fact]
    public void A_real_system_font_classifies_and_round_trips_through_the_metrics_reader()
    {
        // The point of this test: a synthetic magic-number fixture proves the switch statement,
        // not that the output is loadable. Classify a real font and hand the result to the parser
        // that will actually consume it.
        FontMatch? match = SystemFontLocator.Default.Resolve(
            new FontRequest("Arial", Bold: false, Italic: false));
        Assert.SkipWhen(match is null, "No Arial on this machine.");

        ClassifiedProgram? result = FontProgramClassifier.Classify(match!.Data, match.FaceIndex);

        Assert.NotNull(result);
        Assert.Contains(result.Format,
            new[] { FontProgramFormat.TrueType, FontProgramFormat.OpenType });
        Assert.True(FontProgramClassifier.IsLoadable(result.Program),
            "The classified bytes must parse as a font program, not merely start with the right magic.");
    }

    [Fact]
    public void A_collection_face_is_extracted_to_a_standalone_sfnt()
    {
        // Find any .ttc the machine has; skip honestly when there is none.
        FontMatch? collection = SystemFontLocatorTestHelpers.FirstCollectionFace();
        Assert.SkipWhen(collection is null, "No font collection available on this machine.");

        ClassifiedProgram? result = FontProgramClassifier.Classify(
            collection!.Data, collection.FaceIndex);

        Assert.NotNull(result);
        Assert.NotEqual<byte>(0x74, result.Program[0]);          // no longer 'ttcf'
        Assert.Equal(FontProgramFormat.TrueType, result.Format);
        Assert.True(FontProgramClassifier.IsLoadable(result.Program));
    }
}
