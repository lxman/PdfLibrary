using System;
using System.IO;
using CoreJ2K;
using CoreJ2K.j2k.util;
using CoreJ2K.Util;
using Jp2Codec;

namespace Jp2Codec.Tests.Integration;

/// <summary>
/// Writes BMP renderings of the raw J2K conformance images to
/// <c>visual/j2c/&lt;file&gt;/</c>, mirroring <see cref="File8VisualDump"/> for
/// the JP2-wrapped corpus. Three BMPs per file:
///   ours.bmp      — Jp2Codec output.
///   reference.bmp — CSJ2K with nocolorspace=on (per-component samples).
///   diff.bmp      — pixel-wise absolute delta between the two, amplified
///                   ×8 (clamped) so single-LSB differences are visible.
///
/// Useful for inspecting the c1/c2 LAZY-style anomalies recorded in
/// the project memory — diff.bmp shows whether a divergence is one
/// localised pixel, a whole region, or scattered noise.
/// </summary>
public class J2cVisualDump
{
    private static readonly bool Run = true;

    private static readonly string[] Files =
    {
        "a1_mono.j2c", "a3_mono.j2c", "b1_mono.j2c", "b3_mono.j2c",
        "c1_mono.j2c", "c2_mono.j2c",
        "d1_colr.j2c", "d2_colr.j2c",
        "f1_mono.j2c",
    };

    [Fact]
    public void DumpJ2cImagesAsBmp()
    {
        if (!Run) return;

        string visualRoot = "C:/Users/jorda/RiderProjects/ImageLibraries/Jp2Codec.Tests/bin/Debug/net10.0/visual/j2c";
        Directory.CreateDirectory(visualRoot);

        foreach (string fileName in Files)
        {
            string folderName = Path.GetFileNameWithoutExtension(fileName);
            string dir = Path.Combine(visualRoot, folderName);
            Directory.CreateDirectory(dir);

            string testDataPath = Path.Combine("TestData", fileName);
            if (!File.Exists(testDataPath)) continue;

            byte[] bytes = File.ReadAllBytes(testDataPath);

            Jp2DecodeResult ours = new Jp2StreamDecoder().Decode(bytes);

            int width = ours.Width;
            int height = ours.Height;
            int numComps = ours.NumberOfComponents;
            int[][] oursData = ours.ComponentArrays;
            int[] precisions = new int[numComps];
            for (var c = 0; c < numComps; c++) precisions[c] = ours.ComponentPrecision[c];

            BmpWriter.Save(Path.Combine(dir, "ours.bmp"),
                width, height, numComps, oursData, precisions);

            int[][] refData;
            using (var ms = new MemoryStream(bytes))
            {
                var pl = new ParameterList(J2kImage.GetDefaultDecoderParameterList());
                pl["nocolorspace"] = "on";
                PortableImage img;
                try
                {
                    img = J2kImage.FromStream(ms, pl);
                }
                catch
                {
                    // CSJ2K can't decode some files (e.g. g-series PPM bug).
                    refData = null!;
                    continue;
                }
                refData = new int[img.NumberOfComponents][];
                for (var c = 0; c < img.NumberOfComponents; c++)
                    refData[c] = img.GetComponent(c);
            }

            BmpWriter.Save(Path.Combine(dir, "reference.bmp"),
                width, height, numComps, refData, precisions);

            // Per-pixel absolute difference, amplified ×8 (clamped) so
            // single-LSB deltas surface visually.
            DumpDiff(Path.Combine(dir, "diff.bmp"),
                width, height, numComps, oursData, refData, precisions);

            // Numeric stats for the project memory / triage.
            DumpStats(Path.Combine(dir, "stats.txt"),
                fileName, width, height, numComps, oursData, refData);
        }
    }

    private static void DumpStats(
        string path, string fileName, int width, int height, int numComps,
        int[][] oursData, int[][] refData)
    {
        using var w = new StreamWriter(path);
        w.WriteLine($"file:        {fileName}");
        w.WriteLine($"dimensions:  {width}x{height} ({numComps} component(s))");
        for (var c = 0; c < numComps; c++)
        {
            int n = Math.Min(oursData[c].Length, refData[c].Length);
            int diffCount = 0;
            int maxDelta = 0;
            long sumDelta = 0;
            int firstDiff = -1;
            int minRow = int.MaxValue, maxRow = int.MinValue;
            int minCol = int.MaxValue, maxCol = int.MinValue;
            // Per-row diff histogram — reveals whether errors cluster in
            // specific stripes (which would point to specific code-blocks
            // or wavelet bands).
            var rowDiffs = new int[height];
            var colDiffs = new int[width];
            for (var i = 0; i < n; i++)
            {
                int delta = Math.Abs(oursData[c][i] - refData[c][i]);
                if (delta > 0)
                {
                    if (firstDiff < 0) firstDiff = i;
                    diffCount++;
                    if (delta > maxDelta) maxDelta = delta;
                    sumDelta += delta;
                    int row = i / width;
                    int col = i % width;
                    if (row < minRow) minRow = row;
                    if (row > maxRow) maxRow = row;
                    if (col < minCol) minCol = col;
                    if (col > maxCol) maxCol = col;
                    rowDiffs[row]++;
                    colDiffs[col]++;
                }
            }
            double mean = diffCount > 0 ? (double)sumDelta / diffCount : 0;
            double matchPct = (1.0 - (double)diffCount / n) * 100;
            w.WriteLine();
            w.WriteLine($"component {c}: {n} samples");
            w.WriteLine($"  matching:     {n - diffCount} / {n} ({matchPct:F4}%)");
            w.WriteLine($"  differing:    {diffCount}");
            w.WriteLine($"  max abs Δ:    {maxDelta}");
            w.WriteLine($"  mean abs Δ:   {mean:F3} (over differing samples)");
            if (firstDiff >= 0)
            {
                int row = firstDiff / width;
                int col = firstDiff % width;
                w.WriteLine($"  first diff at index {firstDiff} (row {row}, col {col}): " +
                    $"ours={oursData[c][firstDiff]}, ref={refData[c][firstDiff]}");
                w.WriteLine($"  diff bbox:    rows [{minRow}..{maxRow}], cols [{minCol}..{maxCol}]");
                w.WriteLine();
                w.WriteLine("  rows with diffs (row: count):");
                for (var r = 0; r < height; r++)
                    if (rowDiffs[r] > 0) w.WriteLine($"    {r}: {rowDiffs[r]}");
                w.WriteLine();
                w.WriteLine("  cols with diffs (col: count):");
                for (var col2 = 0; col2 < width; col2++)
                    if (colDiffs[col2] > 0) w.WriteLine($"    {col2}: {colDiffs[col2]}");
            }
        }
    }

    private static void DumpDiff(
        string path, int width, int height, int numComps,
        int[][] oursData, int[][] refData, int[] precisions)
    {
        var diffData = new int[numComps][];
        for (var c = 0; c < numComps; c++)
        {
            int n = Math.Min(oursData[c].Length, refData[c].Length);
            var diff = new int[n];
            for (var i = 0; i < n; i++)
            {
                int delta = Math.Abs(oursData[c][i] - refData[c][i]);
                int amplified = delta * 8;
                int max = (1 << precisions[c]) - 1;
                if (amplified > max) amplified = max;
                diff[i] = amplified;
            }
            diffData[c] = diff;
        }
        BmpWriter.Save(path, width, height, numComps, diffData, precisions);
    }
}
