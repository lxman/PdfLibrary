using System.Text;
using Jp2Codec.Tier1;
using Jp2Codec.Tiles;

namespace Jp2Codec.Tests.Integration;

/// <summary>
/// Dumps the post-Tier-1 / post-dequantization integer coefficient grids
/// of c1_mono.j2c (LAZY only) to text files for manual inspection. One
/// file per (resolution, orientation). Used to spot Tier-1 anomalies in
/// the LAZY decode that aren't obvious from spatial diffs alone.
/// </summary>
public class LazySubbandDump
{
    private static readonly bool Run = false;
    private readonly ITestOutputHelper _output;

    public LazySubbandDump(ITestOutputHelper output) => _output = output;

    private record struct SubbandKey(int Component, int Resolution, SubbandOrientation Orientation);

    [Fact]
    public void DumpC1Subbands()
    {
        if (!Run) return;

        var outDir = "C:/Users/jorda/RiderProjects/ImageLibraries/Jp2Codec.Tests/bin/Debug/net10.0/visual/j2c/c1_subbands";
        Directory.CreateDirectory(outDir);

        // Capture (and keep per-tile by appending tile index). c1 is
        // single-tile per the survey, so first capture per key is sufficient.
        var captured = new List<(SubbandKey Key, int[,] Grid)>();
        try
        {
            TileDecoder.OnSubbandInts53 = (c, r, o, grid) =>
            {
                captured.Add((new SubbandKey(c, r, o), (int[,])grid.Clone()));
            };
            byte[] bytes = File.ReadAllBytes(Path.Combine("TestData", "c1_mono.j2c"));
            _ = new Jp2StreamDecoder().Decode(bytes);
        }
        finally
        {
            TileDecoder.OnSubbandInts53 = null;
        }

        var summary = new StringBuilder();
        summary.AppendLine($"c1 subband capture: {captured.Count} subband(s)");
        summary.AppendLine();

        foreach ((SubbandKey key, int[,] grid) in captured)
        {
            int h = grid.GetLength(0);
            int w = grid.GetLength(1);
            var nonZero = 0;
            int minV = int.MaxValue, maxV = int.MinValue;
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    int v = grid[y, x];
                    if (v != 0) nonZero++;
                    if (v < minV) minV = v;
                    if (v > maxV) maxV = v;
                }
            var fileName = $"c{key.Component}_r{key.Resolution}_{key.Orientation}.txt";
            summary.AppendLine($"comp={key.Component} res={key.Resolution} orient={key.Orientation} " +
                $"size={w}x{h} nonZero={nonZero}/{w * h} min={minV} max={maxV} → {fileName}");

            using var sw = new StreamWriter(Path.Combine(outDir, fileName));
            sw.WriteLine($"# {key.Orientation} subband at resolution {key.Resolution}, component {key.Component}");
            sw.WriteLine($"# size: {w} cols × {h} rows");
            sw.WriteLine();
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    if (x > 0) sw.Write(' ');
                    sw.Write(grid[y, x].ToString().PadLeft(5));
                }
                sw.WriteLine();
            }
        }

        File.WriteAllText(Path.Combine(outDir, "_summary.txt"), summary.ToString());
        _output.WriteLine(summary.ToString());
    }
}
