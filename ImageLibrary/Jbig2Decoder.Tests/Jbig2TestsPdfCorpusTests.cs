
namespace Jbig2Decoder.Tests;

/// <summary>
/// Walks the Nico Weber jbig2-tests-pdf corpus
/// (https://nico.github.io/jbig2-bench/, used by Chromium / SerenityOS LibGfx)
/// and asserts the decoder produces the expected reference bitmap for each
/// stream. The corpus exercises a different decoder feature per file but
/// every file decodes to one of two reference bitmaps:
///   golden-A: 103 streams (default OR/XOR/REPLACE compositors)
///   golden-B:   4 streams (the four *-and-xnor-* variants)
/// The mapping is captured in manifest.txt next to the streams.
/// </summary>
public class Jbig2TestsPdfCorpusTests
{
    private readonly ITestOutputHelper _output;

    public Jbig2TestsPdfCorpusTests(ITestOutputHelper output) => _output = output;

    private static string CorpusPath =>
        Path.Combine(AppContext.BaseDirectory, "Corpus", "jbig2-tests-pdf");

    private record ManifestEntry(string Name, string Golden, int Width, int Height, bool HasGlobals);

    private static List<ManifestEntry> LoadManifest()
    {
        string manifestPath = Path.Combine(CorpusPath, "manifest.txt");
        if (!File.Exists(manifestPath)) return [];

        var entries = new List<ManifestEntry>();
        foreach (string raw in File.ReadAllLines(manifestPath))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            string[] parts = line.Split('|');
            if (parts.Length != 5) continue;
            entries.Add(new ManifestEntry(
                parts[0],
                parts[1],
                int.Parse(parts[2]),
                int.Parse(parts[3]),
                parts[4] == "1"));
        }
        return entries;
    }

    public static IEnumerable<object[]> AllStreams()
    {
        foreach (ManifestEntry entry in LoadManifest())
            yield return [entry.Name];
    }

    [Theory]
    [MemberData(nameof(AllStreams))]
    public void Decode_MatchesGoldenBitmap(string streamName)
    {
        ManifestEntry entry = LoadManifest().Single(e => e.Name == streamName);

        byte[] streamData = File.ReadAllBytes(Path.Combine(CorpusPath, streamName + ".jb2"));
        byte[] expected = File.ReadAllBytes(Path.Combine(CorpusPath, entry.Golden + ".bin"));

        var decoder = new JBIG2StreamDecoder { TolerateMissingSegments = true };
        if (entry.HasGlobals)
        {
            byte[] globals = File.ReadAllBytes(Path.Combine(CorpusPath, streamName + ".globals.jb2"));
            decoder.SetGlobalData(globals);
        }

        byte[] actual = decoder.DecodeJBIG2ToPacked(streamData, out int w, out int h);

        Assert.Equal(entry.Width, w);
        Assert.Equal(entry.Height, h);
        Assert.Equal(expected.Length, actual.Length);

        // First-divergence reporting — attribution-friendly.
        int rowBytes = (w + 7) / 8;
        for (var i = 0; i < expected.Length; i++)
        {
            if (expected[i] == actual[i]) continue;
            int row = i / rowBytes;
            int colByte = i % rowBytes;
            _output.WriteLine(
                $"First divergence at row={row}, byte={colByte} (x={colByte * 8}-{colByte * 8 + 7}): " +
                $"expected=0x{expected[i]:X2}, actual=0x{actual[i]:X2}");
            Assert.Fail($"{streamName}: bitmap mismatch against {entry.Golden} (see test output for first divergence)");
        }
    }
}
