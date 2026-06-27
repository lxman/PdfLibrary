
namespace Jbig2Decoder.Tests;

/// <summary>
/// Walks the Power JBIG-2 test corpus (https://nico.github.io/power-jbig2-tests/)
/// and asserts the decoder produces the source bitmap for each variant.
///
/// All 042_*.jb2 streams encode the same source bitmap (042.bmp); all amb_*.jb2
/// streams encode amb.bmp. So the expected output for any test file is the
/// source bitmap of the same family.
/// </summary>
public class CorpusTests
{
    private readonly ITestOutputHelper _output;

    public CorpusTests(ITestOutputHelper output) => _output = output;

    private static string TestDataPath =>
        Path.Combine(AppContext.BaseDirectory, "TestData");

    // Refagg stress cases that the reference jbig2dec also fails to decode —
    // not our bugs, see memory note project_jbig2_corpus_known_broken.md.
    private static readonly HashSet<string> KnownBrokenInJbig2dec =
        new(StringComparer.OrdinalIgnoreCase) { "042_13.jb2", "042_14.jb2" };

    public static IEnumerable<object[]> AllStreams()
    {
        if (!Directory.Exists(TestDataPath)) yield break;
        foreach (string file in Directory.EnumerateFiles(TestDataPath, "*.jb2").OrderBy(f => f))
        {
            string name = Path.GetFileName(file);
            if (KnownBrokenInJbig2dec.Contains(name)) continue;
            yield return [name];
        }
    }

    [Theory]
    [MemberData(nameof(AllStreams))]
    public void Decode_ProducesSourceBitmap(string fileName)
    {
        string streamPath = Path.Combine(TestDataPath, fileName);
        string referenceName = fileName.StartsWith("amb", StringComparison.OrdinalIgnoreCase)
            ? "amb.bmp"
            : "042.bmp";
        string referencePath = Path.Combine(TestDataPath, referenceName);

        BmpReference.Bitmap expected = BmpReference.Load(referencePath);
        byte[] streamData = File.ReadAllBytes(streamPath);

        var decoder = new JBIG2StreamDecoder();
        byte[] rgb = decoder.DecodeJBIG2(streamData, out int width, out int height);

        Assert.Equal(expected.Width, width);
        Assert.Equal(expected.Height, height);
        Assert.Equal(expected.Width * expected.Height * 3, rgb.Length);

        // Pack RGB output to 1-bit MSB-first rows for diff against expected.
        int rowBytes = (width + 7) / 8;
        var actual = new byte[rowBytes * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int rgbIdx = (y * width + x) * 3;
                bool isBlack = rgb[rgbIdx] == 0 && rgb[rgbIdx + 1] == 0 && rgb[rgbIdx + 2] == 0;
                if (isBlack)
                    actual[y * rowBytes + x / 8] |= (byte)(0x80 >> (x % 8));
            }
        }

        // First-divergence reporting — attribution-friendly.
        for (var i = 0; i < expected.PackedRows.Length; i++)
        {
            if (expected.PackedRows[i] == actual[i]) continue;
            int row = i / rowBytes;
            int colByte = i % rowBytes;
            _output.WriteLine(
                $"First divergence at row={row}, byte={colByte} (x≈{colByte * 8}-{colByte * 8 + 7}): " +
                $"expected=0x{expected.PackedRows[i]:X2}, actual=0x{actual[i]:X2}");
            Assert.Fail($"{fileName}: bitmap mismatch (see test output for first divergence)");
        }
    }
}
