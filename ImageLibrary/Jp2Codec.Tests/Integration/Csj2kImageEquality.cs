using CoreJ2K;
using CoreJ2K.j2k.util;
using CoreJ2K.Util;
using Xunit.Abstractions;

namespace Jp2Codec.Tests.Integration;

/// <summary>
/// Sanity probe: are c1, c2, a1, f1, f2 (all 303x179 mono NL=5 5/3) the
/// SAME source image just encoded differently? CSJ2K decodes default and
/// layer-truncated streams correctly, so if its output for two files
/// matches, the encoder source was identical.
/// </summary>
public class Csj2kImageEquality
{
    private static readonly bool Run = false;
    private readonly ITestOutputHelper _output;

    public Csj2kImageEquality(ITestOutputHelper output) => _output = output;

    [Fact]
    public void CompareImages()
    {
        if (!Run) return;

        string[] files = { "a1_mono.j2c", "f1_mono.j2c", "f2_mono.j2c", "c1_mono.j2c", "c2_mono.j2c" };
        int[][] arrays = new int[files.Length][];
        for (var i = 0; i < files.Length; i++)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine("TestData", files[i]));
            using var ms = new MemoryStream(bytes);
            try
            {
                var pl = new ParameterList(J2kImage.GetDefaultDecoderParameterList());
                pl["nocolorspace"] = "on";
                PortableImage img = J2kImage.FromStream(ms, pl);
                arrays[i] = img.GetComponent(0);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"{files[i]}: CSJ2K failed — {ex.Message.Split('\n')[0]}");
                arrays[i] = null!;
            }
        }

        for (var i = 0; i < files.Length; i++)
        {
            if (arrays[i] == null) continue;
            for (int j = i + 1; j < files.Length; j++)
            {
                if (arrays[j] == null) continue;
                int n = Math.Min(arrays[i].Length, arrays[j].Length);
                int diff = 0;
                int maxD = 0;
                for (var k = 0; k < n; k++)
                {
                    int d = Math.Abs(arrays[i][k] - arrays[j][k]);
                    if (d > 0) { diff++; if (d > maxD) maxD = d; }
                }
                _output.WriteLine($"{files[i]} vs {files[j]}: {diff}/{n} diffs, max Δ {maxD}");
            }
        }
    }
}
