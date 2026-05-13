using System.Diagnostics;
using System.IO;
using Jp2Codec;
using Jp2Codec.Color;
using Xunit.Abstractions;

namespace Jp2Codec.Tests.Integration;

/// <summary>
/// Wall-clock measurement of <see cref="SrgbRenderer.RenderToSrgb"/> on each
/// reference image. Not a microbenchmark (Debug binaries, allocator noise, etc.)
/// — used as a sanity check before and after optimization work.
/// </summary>
public class SrgbRendererBenchmark
{
    private const bool Run = true;
    private readonly ITestOutputHelper _output;

    public SrgbRendererBenchmark(ITestOutputHelper output) => _output = output;

    [Fact]
    public void TimeAllImages()
    {
        if (!Run) return;

        string[] names = { "test_8x8.jp2", "test_16x16.jp2", "file1.jp2", "file2.jp2", "file4.jp2", "file5.jp2", "file8.jp2", "file9.jp2" };
        foreach (string name in names)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine("TestData", name));
            Jp2DecodeResult result = new Jp2StreamDecoder().Decode(bytes);

            // Warm-up.
            _ = SrgbRenderer.RenderToSrgb(result);

            var sw = Stopwatch.StartNew();
            const int iterations = 1;
            for (var i = 0; i < iterations; i++)
                _ = SrgbRenderer.RenderToSrgb(result);
            sw.Stop();

            int pixels = result.Width * result.Height;
            double msPerCall = sw.Elapsed.TotalMilliseconds / iterations;
            double mpps = pixels / msPerCall / 1000.0;
            _output.WriteLine(
                $"{name,-16} {result.Width}x{result.Height} ColorSpace={result.ColorSpace,-12} " +
                $"{msPerCall,8:F2} ms ({mpps,5:F2} MP/s)");
        }
    }
}
