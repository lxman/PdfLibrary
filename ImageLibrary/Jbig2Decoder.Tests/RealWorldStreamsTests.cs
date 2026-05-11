namespace Jbig2Decoder.Tests;

/// <summary>
/// Smoke tests on JBIG2 streams extracted from real-world PDFs. These were
/// the regression cases that drove fixes during the in-house decoder work
/// (most notably object 2006, which previously crashed with
/// IndexOutOfRangeException in tolerance mode due to malformed text-region
/// symbol-code-length encoding).
/// </summary>
public class RealWorldStreamsTests
{
    private static string TestDataPath =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "RealWorld");

    [Theory]
    [InlineData("jbig2_1964.jb2", null,                       603, 696)]
    [InlineData("jbig2_2005.jb2", null,                       583, 707)]
    [InlineData("jbig2_2006.jb2", "jbig2_2007_globals.jb2",   532, 622)]
    [InlineData("jbig2_2042.jb2", null,                       513, 722)]
    [InlineData("jbig2_2043.jb2", "jbig2_2044_globals.jb2",   473, 472)]
    public void Decode_RealWorldStream_ProducesExpectedDimensions(
        string streamFile, string? globalsFile, int expectedWidth, int expectedHeight)
    {
        byte[] data = File.ReadAllBytes(Path.Combine(TestDataPath, streamFile));

        var decoder = new JBIG2StreamDecoder { TolerateMissingSegments = true };
        if (globalsFile is not null)
        {
            byte[] globals = File.ReadAllBytes(Path.Combine(TestDataPath, globalsFile));
            decoder.SetGlobalData(globals);
        }

        byte[] packed = decoder.DecodeJBIG2ToPacked(data, out int width, out int height);

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
        Assert.Equal((expectedWidth + 7) / 8 * expectedHeight, packed.Length);
    }
}
