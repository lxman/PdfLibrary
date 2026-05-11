using System.IO;
using JpegCodec.Stream;

namespace JpegCodec.Tests;

// Sanity test for progress.jpg — Identify succeeds, Decode does not throw.
// Byte-identical comparison against JpegLibrary lives in
// ProgressiveDecodeDifferentialTests; this is the smoke-level gate.
public class ProgressJpgSmokeTest
{
    [Fact]
    public void Decode_ProgressJpg_ProducesOutputWithoutThrowing()
    {
        string path = Path.Combine(Corpus.CorpusFiles.CorpusRoot, "progressive", "progress.jpg");
        if (!File.Exists(path)) return;

        byte[] data = File.ReadAllBytes(path);

        var info = new JpegStreamDecoder().Identify(data);
        Assert.Equal(JpegMarker.Sof2, info.StartOfFrame);
        Assert.Equal(341, info.Width);
        Assert.Equal(486, info.Height);

        var result = new JpegStreamDecoder().Decode(data);
        Assert.Equal(341, result.Width);
        Assert.Equal(486, result.Height);
        Assert.Equal(3, result.NumberOfComponents);
        Assert.Equal(341 * 486 * 3, result.ComponentData.Length);
    }
}
