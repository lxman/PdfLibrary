namespace Jp2Codec.Tests.Integration;

/// <summary>
/// Smoke tests for <see cref="Jp2StreamDecoder.Decode"/>: confirm the public
/// API runs end-to-end on real conformance images without throwing, and
/// returns a result whose shape matches the SIZ / JP2 header. Sample-level
/// correctness against a reference decoder is exercised by
/// <see cref="ReferenceDifferentialTests"/> (currently skipped pending
/// Tier-1 cross-codec investigation).
/// </summary>
public class BasicDecodeTests
{
    private static byte[] LoadTestFile(string name)
    {
        string path = Path.Combine("TestData", name);
        return File.ReadAllBytes(path);
    }

    [Fact]
    public void Decode_Test8x8_ProducesGreyscaleSampleGrid()
    {
        byte[] bytes = LoadTestFile("test_8x8.jp2");
        var decoder = new Jp2StreamDecoder();
        Jp2DecodeResult result = decoder.Decode(bytes);

        Assert.Equal(8, result.Width);
        Assert.Equal(8, result.Height);
        Assert.Equal(1, result.NumberOfComponents);
        Assert.Equal(8, result.ComponentPrecision[0]);
        Assert.False(result.ComponentSigned[0]);
        Assert.Equal(64, result.ComponentData[0].Length);
        // Samples should be in [0, 255] (8-bit unsigned).
        foreach (int v in result.ComponentData[0])
        {
            Assert.InRange(v, 0, 255);
        }
    }

    [Fact]
    public void Decode_Test16x16_ProducesGreyscaleSampleGrid()
    {
        byte[] bytes = LoadTestFile("test_16x16.jp2");
        var decoder = new Jp2StreamDecoder();
        Jp2DecodeResult result = decoder.Decode(bytes);

        Assert.Equal(16, result.Width);
        Assert.Equal(16, result.Height);
        Assert.Equal(1, result.NumberOfComponents);
        Assert.Equal(256, result.ComponentData[0].Length);
        foreach (int v in result.ComponentData[0])
        {
            Assert.InRange(v, 0, 255);
        }
    }
}
