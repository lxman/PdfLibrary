using BenchmarkDotNet.Attributes;
using JpegCodec;

namespace ImageLibrary.Benchmarks.Jpeg;

[MemoryDiagnoser]
public class JpegDecodeBenchmarks
{
    private byte[] _backhoe = null!;
    private byte[] _color420 = null!;
    private byte[] _gray64 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _backhoe  = BenchmarkAssets.Load(@"jpeg_test\backhoe-006.jpg");
        _color420 = BenchmarkAssets.Load(@"jpeg_test\level5_color_420\color420_32x32.jpg");
        _gray64   = BenchmarkAssets.Load(@"jpeg_test\level3_multiple_blocks\gray_64x64_gradient.jpg");
    }

    [Benchmark]
    public JpegDecodeResult Backhoe_Color() => new JpegStreamDecoder().Decode(_backhoe);

    [Benchmark]
    public JpegDecodeResult Color420_32x32() => new JpegStreamDecoder().Decode(_color420);

    [Benchmark]
    public JpegDecodeResult Gray64x64() => new JpegStreamDecoder().Decode(_gray64);
}
