namespace JpegCodec.Tests;

public class SmokeTests
{
    [Fact]
    public void JpegStreamDecoder_CanBeInstantiated()
    {
        var decoder = new JpegStreamDecoder();
        Assert.NotNull(decoder);
    }

    [Fact]
    public void JpegStreamEncoder_CanBeInstantiated()
    {
        var encoder = new JpegStreamEncoder();
        Assert.NotNull(encoder);
    }
}
