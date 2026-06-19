using System.Text;
using Jp2Codec.Tier1;
using Jp2Codec.Tiles;
using Xunit.Abstractions;

namespace Jp2Codec.Tests.Integration;

/// <summary>
/// Diff probe: c1_mono.j2c (LAZY only) and f1_mono.j2c (default style, 4
/// layers) appear to be the same source image with different encoder
/// options — same dimensions, same component, same wavelet. The
/// post-Tier-1 / post-dequantization integer coefficients should therefore
/// be IDENTICAL between the two decodes (5/3 reversible, dequantization is
/// pass-through). Any difference points directly at a LAZY-specific bug
/// in Tier-1 / Tier-2.
///
/// Captures per-(component, resolution, orientation) grids via
/// <see cref="TileDecoder.OnSubbandInts53"/> and emits a diff report to
/// <c>bin/Debug/net10.0/visual/j2c/lazy_subband_diff.txt</c>.
/// </summary>
public class LazySubbandDiffProbe
{
    private static readonly bool Run = false;
    private readonly ITestOutputHelper _output;

    public LazySubbandDiffProbe(ITestOutputHelper output) => _output = output;

    private record struct SubbandKey(int Component, int Resolution, SubbandOrientation Orientation);

    [Fact]
    public void CompareSubbandsBetweenC1AndC2()
    {
        if (!Run) return;

        // c1 and c2 are the SAME image at the same dimensions / wavelet
        // (303x179 mono NL=5 5/3) encoded with different style flags. Their
        // post-Tier-1 / post-dequantization integer coefficients should be
        // identical — any difference flags a decoder bug.
        Dictionary<SubbandKey, int[,]> c1 = CaptureSubbands("c1_mono.j2c");
        Dictionary<SubbandKey, int[,]> f1 = CaptureSubbands("a1_mono.j2c");

        var report = new StringBuilder();
        report.AppendLine("c1 vs a1 per-subband diff (same image expected, c1=LAZY 10L, a1=default 1L).");
        report.AppendLine();

        var allKeys = new HashSet<SubbandKey>(c1.Keys);
        foreach (SubbandKey k in f1.Keys) allKeys.Add(k);

        var keyList = new List<SubbandKey>(allKeys);
        keyList.Sort((a, b) =>
        {
            int cmp = a.Component.CompareTo(b.Component);
            if (cmp != 0) return cmp;
            cmp = a.Resolution.CompareTo(b.Resolution);
            if (cmp != 0) return cmp;
            return a.Orientation.CompareTo(b.Orientation);
        });

        var totalDiffSubbands = 0;
        foreach (SubbandKey key in keyList)
        {
            bool hasC1 = c1.TryGetValue(key, out int[,]? g1);
            bool hasF1 = f1.TryGetValue(key, out int[,]? g2);
            if (!hasC1 || !hasF1 || g1 is null || g2 is null)
            {
                report.AppendLine($"comp={key.Component} res={key.Resolution} orient={key.Orientation}: " +
                    $"present in c1={hasC1}, f1={hasF1}");
                continue;
            }
            int h1 = g1.GetLength(0);
            int w1 = g1.GetLength(1);
            int h2 = g2.GetLength(0);
            int w2 = g2.GetLength(1);
            if (h1 != h2 || w1 != w2)
            {
                report.AppendLine($"comp={key.Component} res={key.Resolution} orient={key.Orientation}: " +
                    $"size differs c1={w1}x{h1} f1={w2}x{h2}");
                continue;
            }

            var diffCount = 0;
            var maxDelta = 0;
            var diffs = new List<(int X, int Y, int V1, int V2)>();
            for (var y = 0; y < h1; y++)
            for (var x = 0; x < w1; x++)
            {
                int v1 = g1[y, x];
                int v2 = g2[y, x];
                if (v1 != v2)
                {
                    diffCount++;
                    int delta = Math.Abs(v1 - v2);
                    if (delta > maxDelta) maxDelta = delta;
                    diffs.Add((x, y, v1, v2));
                }
            }
            var label = $"comp={key.Component} res={key.Resolution} orient={key.Orientation} ({w1}x{h1})";
            if (diffCount == 0)
            {
                report.AppendLine($"{label}: MATCH ({w1 * h1} samples).");
            }
            else
            {
                totalDiffSubbands++;
                report.AppendLine($"{label}: DIFF — {diffCount}/{w1 * h1} samples differ, max abs Δ={maxDelta}:");
                foreach ((int x, int y, int v1, int v2) in diffs)
                {
                    int dlt = v1 - v2;
                    report.AppendLine($"    ({x,3},{y,3}): c1={v1,5} c2={v2,5}  diff={dlt,+4}");
                }
            }
        }

        report.AppendLine();
        report.AppendLine($"Subbands with differences: {totalDiffSubbands} / {keyList.Count}.");

        var outDir = "C:/Users/jorda/RiderProjects/ImageLibraries/Jp2Codec.Tests/bin/Debug/net10.0/visual/j2c";
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "lazy_subband_diff.txt"), report.ToString());
        _output.WriteLine(report.ToString());
    }

    private static Dictionary<SubbandKey, int[,]> CaptureSubbands(string fileName)
    {
        var captured = new Dictionary<SubbandKey, int[,]>();
        try
        {
            TileDecoder.OnSubbandInts53 = (c, r, o, grid) =>
            {
                captured[new SubbandKey(c, r, o)] = (int[,])grid.Clone();
            };
            byte[] bytes = File.ReadAllBytes(Path.Combine("TestData", fileName));
            _ = new Jp2StreamDecoder().Decode(bytes);
        }
        finally
        {
            TileDecoder.OnSubbandInts53 = null;
        }
        return captured;
    }
}
