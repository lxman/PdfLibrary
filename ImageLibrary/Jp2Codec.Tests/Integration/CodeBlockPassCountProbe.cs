using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Jp2Codec;
using Jp2Codec.Tier1;
using Jp2Codec.Tiles;
using Xunit.Abstractions;

namespace Jp2Codec.Tests.Integration;

/// <summary>
/// Compare per-(component, resolution, orientation, blockX, blockY) pass
/// counts between c1 (LAZY 10-layer) and a1 (default single-layer) for the
/// same source image. They should agree at full lossless decode — if c1
/// has fewer passes for some blocks, Tier-2 is dropping bytes somewhere
/// in the LAZY contribution path.
/// </summary>
public class CodeBlockPassCountProbe
{
    private static readonly bool Run = false;
    private readonly ITestOutputHelper _output;

    public CodeBlockPassCountProbe(ITestOutputHelper output) => _output = output;

    private record struct BlockKey(int Component, int Resolution, SubbandOrientation Orientation, int BlockX, int BlockY);

    private class BlockInfo
    {
        public int PassCount;
        public int FirstBitPlane;
        public int ZeroBitPlanes;
        public int SegmentCount;
        public int TotalBytes;
        public string SegmentSig = "";
    }

    [Fact]
    public void DiffPassCounts()
    {
        if (!Run) return;

        Dictionary<BlockKey, BlockInfo> c1 = Capture("c1_mono.j2c");
        Dictionary<BlockKey, BlockInfo> a1 = Capture("a1_mono.j2c");

        var report = new StringBuilder();
        report.AppendLine("Per-block pass count comparison: c1 (LAZY 10-layer) vs a1 (default 1-layer).");
        report.AppendLine();

        var allKeys = new HashSet<BlockKey>(c1.Keys);
        foreach (var k in a1.Keys) allKeys.Add(k);
        var keyList = allKeys.OrderBy(k => k.Resolution).ThenBy(k => k.Orientation)
            .ThenBy(k => k.BlockY).ThenBy(k => k.BlockX).ToList();

        int matchCount = 0;
        int diffCount = 0;
        foreach (BlockKey key in keyList)
        {
            bool has1 = c1.TryGetValue(key, out BlockInfo? i1);
            bool has2 = a1.TryGetValue(key, out BlockInfo? i2);
            if (!has1 || !has2 || i1 == null || i2 == null) continue;

            // Always dump pass counts + segment signatures, even when they
            // match — gives the trace context for byte-level diff.
            report.AppendLine($"r{key.Resolution} {key.Orientation} b({key.BlockX},{key.BlockY}):");
            report.AppendLine($"   passes c1={i1.PassCount} a1={i2.PassCount}; " +
                $"firstBp c1={i1.FirstBitPlane} a1={i2.FirstBitPlane}; " +
                $"zbp c1={i1.ZeroBitPlanes} a1={i2.ZeroBitPlanes}");
            report.AppendLine($"   c1 segs ({i1.TotalBytes}B): {i1.SegmentSig}");
            report.AppendLine($"   a1 segs ({i2.TotalBytes}B): {i2.SegmentSig}");
            if (i1.PassCount == i2.PassCount) matchCount++; else diffCount++;
        }

        report.AppendLine();
        report.AppendLine($"Matching: {matchCount}.  Differing: {diffCount}.");

        string outDir = "C:/Users/jorda/RiderProjects/ImageLibraries/Jp2Codec.Tests/bin/Debug/net10.0/visual/j2c";
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "passcount_diff.txt"), report.ToString());
        _output.WriteLine(report.ToString());
    }

    private static Dictionary<BlockKey, BlockInfo> Capture(string fileName)
    {
        var captured = new Dictionary<BlockKey, BlockInfo>();
        try
        {
            TileDecoder.OnCodeBlockTrace = (c, r, o, bx, by, passCount, segments, firstBp, zbp) =>
            {
                int totalBytes = 0;
                var sb = new StringBuilder();
                for (var i = 0; i < segments.Count; i++)
                {
                    CodeBlockSegment seg = segments[i];
                    totalBytes += seg.Bytes.Length;
                    if (i > 0) sb.Append(',');
                    sb.Append($"({seg.PassCount}{(seg.IsRaw ? "R" : "M")},{seg.Bytes.Length}B)");
                }
                captured[new BlockKey(c, r, o, bx, by)] = new BlockInfo
                {
                    PassCount = passCount,
                    FirstBitPlane = firstBp,
                    ZeroBitPlanes = zbp,
                    SegmentCount = segments.Count,
                    TotalBytes = totalBytes,
                    SegmentSig = sb.ToString(),
                };
            };
            byte[] bytes = File.ReadAllBytes(Path.Combine("TestData", fileName));
            _ = new Jp2StreamDecoder().Decode(bytes);
        }
        finally
        {
            TileDecoder.OnCodeBlockTrace = null;
        }
        return captured;
    }
}
