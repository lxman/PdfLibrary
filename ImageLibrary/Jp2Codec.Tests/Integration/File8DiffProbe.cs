using CoreJ2K;
using CoreJ2K.Util;

namespace Jp2Codec.Tests.Integration;

/// <summary>
/// Triage probe for <c>file8.jp2</c> (700x400 greyscale 5/3 NL=5). Reports
/// the distribution of |our - reference| differences so we can tell whether
/// the divergence is broad (Tier-1 / MQ issue) or sparse along edges
/// (canvas-rect / parity issue). Flip <see cref="Run"/> to regenerate.
/// </summary>
public class File8DiffProbe
{
    private static readonly bool Run = false;

    [Fact]
    public void ProbeFile8()
    {
        if (!Run) return;

        byte[] bytes = File.ReadAllBytes(Path.Combine("TestData", "file8.jp2"));
        using var ms = new MemoryStream(bytes);
        PortableImage img = J2kImage.FromStream(ms);
        int[] reference = img.GetComponent(0);
        Jp2DecodeResult ours = new Jp2StreamDecoder().Decode(bytes);
        int[] mine = ours.ComponentData[0];

        int w = ours.Width, h = ours.Height;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"file8.jp2: {w}x{h} = {reference.Length} samples");

        int matches = 0;
        int maxAbs = 0;
        var hist = new Dictionary<int, int>();
        for (var i = 0; i < reference.Length; i++)
        {
            int d = mine[i] - reference[i];
            if (d == 0) matches++;
            int abs = Math.Abs(d);
            if (abs > maxAbs) maxAbs = abs;
            hist[d] = hist.GetValueOrDefault(d, 0) + 1;
        }
        sb.AppendLine($"matches: {matches} ({100.0 * matches / reference.Length:F2}%), max|diff|={maxAbs}");
        sb.AppendLine($"diff histogram (diff -> count, top 15):");
        foreach (KeyValuePair<int, int> kvp in hist.OrderByDescending(kv => kv.Value).Take(15))
            sb.AppendLine($"  diff={kvp.Key,5}  count={kvp.Value}");

        // Spatial map: where are the diffs? Bin by row.
        sb.AppendLine($"diffs per row (first/last few):");
        var rowDiffs = new int[h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                if (mine[y * w + x] != reference[y * w + x]) rowDiffs[y]++;
        for (var y = 0; y < Math.Min(8, h); y++)
            sb.AppendLine($"  row {y,3}: {rowDiffs[y]} diffs / {w}");
        for (int y = Math.Max(0, h - 8); y < h; y++)
            sb.AppendLine($"  row {y,3}: {rowDiffs[y]} diffs / {w}");

        sb.AppendLine($"diffs per column (first 8, last 8):");
        var colDiffs = new int[w];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                if (mine[y * w + x] != reference[y * w + x]) colDiffs[x]++;
        for (var x = 0; x < Math.Min(8, w); x++)
            sb.AppendLine($"  col {x,3}: {colDiffs[x]} diffs / {h}");
        for (int x = Math.Max(0, w - 8); x < w; x++)
            sb.AppendLine($"  col {x,3}: {colDiffs[x]} diffs / {h}");

        // First/last 8 mismatched samples with context.
        sb.AppendLine($"first 12 mismatches:");
        var shown = 0;
        for (var i = 0; i < reference.Length && shown < 12; i++)
        {
            if (mine[i] != reference[i])
            {
                int y = i / w, x = i % w;
                sb.AppendLine($"  [{x},{y}] (idx {i}): ref={reference[i],3} ours={mine[i],3} diff={mine[i] - reference[i]:+#;-#;0}");
                shown++;
            }
        }

        string outPath = "C:/Users/jorda/RiderProjects/ImageLibraries/Jp2Codec.Tests/bin/Debug/net10.0/file8_diff_probe.txt";
        File.WriteAllText(outPath, sb.ToString());
    }
}
