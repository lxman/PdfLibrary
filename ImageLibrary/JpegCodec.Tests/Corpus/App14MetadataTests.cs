namespace JpegCodec.Tests.Corpus;

// Pins the contract that Phase 8's DctDecodeFilter rewrite depends on:
// HasAdobeMarker and AdobeColorTransform must flow through Identify and
// Decode for each APP14 variant. The pre-Phase-8 JpegLibraryAdapter
// hardcoded HasAdobeMarker=false, leaving the CMYK/YCCK/inverted-CMYK
// branches at DctDecodeFilter.cs:63-117 unreachable. These tests confirm
// the new decoder surfaces the flag so those branches activate.
public class App14MetadataTests
{
    [Theory]
    [InlineData("cmyk_real/cmyk-sample.jpg", 4, true, (byte)0)]
    [InlineData("cmyk_real/cmyk-sample-no-icc.jpg", 4, true, (byte)0)]
    [InlineData("cmyk_real/cmyk_invalid_icc.jpg", 4, true, (byte)0)]
    [InlineData("cmyk_real/cmyk_ycck_transform2.jpg", 4, true, (byte)2)]
    [InlineData("cmyk_real/edge_app14_ycck_3channel.jpg", 3, true, (byte)2)]
    [InlineData("cmyk_real/edge_app14_ycbcr_grayscale.jpg", 1, true, (byte)1)]
    [InlineData("pdf_extracted/rgb_app14_t1_0_PDF_2_0_image_with_BPC_72x72.jpg", 3, true, (byte)1)]
    [InlineData("pdf_extracted/sequential_dri_0_BLUEBOOK_612x10.jpg", 1, true, (byte)0)]
    [InlineData("turbo/testorig.jpg", 3, false, (byte)0)]
    public void Identify_ReportsAdobeApp14Correctly(string corpusFile, int expectedComponents, bool expectAdobe, byte expectedTransform)
    {
        string path = Path.Combine(CorpusFiles.CorpusRoot, corpusFile);
        if (!File.Exists(path)) return;
        byte[] data = File.ReadAllBytes(path);

        var info = new JpegStreamDecoder().Identify(data);

        Assert.Equal(expectedComponents, info.NumberOfComponents);
        Assert.Equal(expectAdobe, info.HasAdobeMarker);
        if (expectAdobe)
            Assert.Equal(expectedTransform, info.AdobeColorTransform);
    }

    [Fact]
    public void Decode_FourChannelCmyk_ProducesInterleavedComponentData()
    {
        string path = Path.Combine(CorpusFiles.CorpusRoot, "cmyk_real", "cmyk-sample.jpg");
        if (!File.Exists(path)) return;
        byte[] data = File.ReadAllBytes(path);

        var result = new JpegStreamDecoder().Decode(data);

        Assert.Equal(4, result.NumberOfComponents);
        Assert.Equal(result.Width * result.Height * 4, result.ComponentData.Length);
        Assert.True(result.HasAdobeMarker);
        Assert.Equal(0, result.AdobeColorTransform);
    }

    [Fact]
    public void Decode_FourChannelYcck_ProducesInterleavedComponentData()
    {
        string path = Path.Combine(CorpusFiles.CorpusRoot, "cmyk_real", "cmyk_ycck_transform2.jpg");
        if (!File.Exists(path)) return;
        byte[] data = File.ReadAllBytes(path);

        var result = new JpegStreamDecoder().Decode(data);

        Assert.Equal(4, result.NumberOfComponents);
        Assert.Equal(result.Width * result.Height * 4, result.ComponentData.Length);
        Assert.True(result.HasAdobeMarker);
        Assert.Equal(2, result.AdobeColorTransform);
    }
}
