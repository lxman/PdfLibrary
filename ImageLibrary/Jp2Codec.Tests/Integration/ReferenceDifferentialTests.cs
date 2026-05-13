using System.IO;
using CoreJ2K;
using CoreJ2K.j2k.util;
using CoreJ2K.Util;
using Jp2Codec;

namespace Jp2Codec.Tests.Integration;

/// <summary>
/// Compares Jp2Codec's per-component samples to the same samples decoded by
/// Melville.CSJ2K (CoreJ2K). For default-style, no-subsampling, single-tile
/// images we expect bit-exact agreement on the reversible (5/3) path.
///
/// Reference decode goes through CSJ2K with <c>nocolorspace=on</c> so that
/// optional ICC profile transforms (CoreJ2K.Icc.ICCProfiler applying a
/// monochrome sRGB tone curve) don't fire on JP2 files like file8.jp2
/// — see <see cref="DecodeReference"/>. Without that flag, ICC-bearing JP2s
/// silently diverged by 1–20 per pixel and looked like Tier-1 bugs.
/// </summary>
public class ReferenceDifferentialTests
{
    private static byte[] LoadTestFile(string name)
    {
        string path = Path.Combine("TestData", name);
        return File.ReadAllBytes(path);
    }

    private static int[][] DecodeReference(byte[] bytes, bool noColorSpace = false)
    {
        using var ms = new MemoryStream(bytes);
        PortableImage img;
        if (noColorSpace)
        {
            // Disable CSJ2K's optional ICC colorspace mapping. The ParameterList
            // is layered the same way J2kImage.FromStream does internally — the
            // outer list holds overrides and chains a defaults list so that
            // FileBitstreamReaderAgent can still resolve base values via
            // DefaultParameterList.
            ParameterList pl = new ParameterList(J2kImage.GetDefaultDecoderParameterList());
            pl["nocolorspace"] = "on";
            img = J2kImage.FromStream(ms, pl);
        }
        else
        {
            img = J2kImage.FromStream(ms);
        }
        var per = new int[img.NumberOfComponents][];
        for (var i = 0; i < img.NumberOfComponents; i++)
            per[i] = img.GetComponent(i);
        return per;
    }

    [Fact]
    public void Decode_Test8x8_MatchesReferenceExactly()
    {
        byte[] bytes = LoadTestFile("test_8x8.jp2");
        int[][] reference = DecodeReference(bytes);
        Jp2DecodeResult ours = new Jp2StreamDecoder().Decode(bytes);

        Assert.Equal(reference.Length, ours.NumberOfComponents);
        for (var c = 0; c < reference.Length; c++)
            Assert.Equal(reference[c], ours.ComponentData[c]);
    }

    [Fact]
    public void Decode_Test16x16_MatchesReferenceExactly()
    {
        byte[] bytes = LoadTestFile("test_16x16.jp2");
        int[][] reference = DecodeReference(bytes);
        Jp2DecodeResult ours = new Jp2StreamDecoder().Decode(bytes);

        Assert.Equal(reference.Length, ours.NumberOfComponents);
        for (var c = 0; c < reference.Length; c++)
            Assert.Equal(reference[c], ours.ComponentData[c]);
    }

    // ---- conformance/file1.jp2: 768x512, 3 components, 8-bit, 5/3, NL=5, MCT=Y ----
    // RGB image with the reversible multi-component transform — first test that
    // exercises InverseRct.Apply on real conformance data.
    [Fact]
    public void Decode_ConformanceFile1_MatchesReferenceExactly()
    {
        byte[] bytes = LoadTestFile("file1.jp2");
        int[][] reference = DecodeReference(bytes);
        Jp2DecodeResult ours = new Jp2StreamDecoder().Decode(bytes);

        Assert.Equal(reference.Length, ours.NumberOfComponents);
        for (var c = 0; c < reference.Length; c++)
            Assert.Equal(reference[c], ours.ComponentData[c]);
    }

    // ---- conformance/file2.jp2: 480x640, 3 components, 8-bit, 5/3, NL=5, MCT=N ----
    // RGB stored as three independent grayscale channels (no MCT). Like file8,
    // this carries an embedded ICC profile (sYCC-style colour space) — CSJ2K
    // applies SYccColorSpaceMapper by default, which transforms YCbCr → sRGB.
    // We bypass colorspace mapping (noColorSpace=true) so the comparison is
    // apples-to-apples raw codestream samples.
    [Fact]
    public void Decode_ConformanceFile2_MatchesReferenceExactly()
    {
        byte[] bytes = LoadTestFile("file2.jp2");
        int[][] reference = DecodeReference(bytes, noColorSpace: true);
        Jp2DecodeResult ours = new Jp2StreamDecoder().Decode(bytes);

        Assert.Equal(reference.Length, ours.NumberOfComponents);
        for (var c = 0; c < reference.Length; c++)
            Assert.Equal(reference[c], ours.ComponentData[c]);
    }

    // ---- conformance/file3.jp2: 480x640, 3 components, 8-bit, 5/3, NL=5, MCT=N ----
    // Component subsampling: comp 0 at 1×1 (full res 480×640), comp 1 and 2
    // at 2×2 (subsampled to 240×320 — classic 4:2:0 chroma). Exercises the
    // multi-component-grid pipeline where each component owns its own tile-
    // component canvas.
    //
    // Bit-exact differential against CSJ2K is impractical for subsampled
    // images: CSJ2K's J2kImage.FromStream with nocolorspace=on throws
    // IndexOutOfRangeException because it assumes uniform component dims;
    // with default colorspace it Resampler-upsamples chroma and applies
    // SYccColorSpaceMapper, producing sRGB values cross-mixed across our
    // raw per-component samples. We assert the structural decode here
    // (correct widths, heights, ranges, no exceptions) so a future
    // regression in subsampled handling fails loudly; a bit-exact test
    // can be added once chroma upsampling lands in our pipeline (and
    // SrgbRenderer learns sYCC on subsampled inputs).
    [Fact]
    public void Decode_ConformanceFile3_StructureMatchesSpec()
    {
        byte[] bytes = LoadTestFile("file3.jp2");
        Jp2DecodeResult r = new Jp2StreamDecoder().Decode(bytes);

        Assert.Equal(480, r.Width);
        Assert.Equal(640, r.Height);
        Assert.Equal(3, r.NumberOfComponents);
        Assert.Equal(Jp2ColorSpace.SrgbYcc, r.ColorSpace);

        // Luma: 480×640, chroma: 240×320 (2:1 horizontal and vertical subsample).
        Assert.Equal(480, r.ComponentWidth[0]);
        Assert.Equal(640, r.ComponentHeight[0]);
        Assert.Equal(240, r.ComponentWidth[1]);
        Assert.Equal(320, r.ComponentHeight[1]);
        Assert.Equal(240, r.ComponentWidth[2]);
        Assert.Equal(320, r.ComponentHeight[2]);

        for (var c = 0; c < 3; c++)
        {
            Assert.Equal(8, r.ComponentPrecision[c]);
            Assert.False(r.ComponentSigned[c]);
            Assert.Equal(r.ComponentWidth[c] * r.ComponentHeight[c], r.ComponentData[c].Length);
            int min = int.MaxValue, max = int.MinValue;
            foreach (int v in r.ComponentData[c])
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }
            Assert.InRange(min, 0, 255);
            Assert.InRange(max, 0, 255);
        }
    }

    // ---- conformance/file4.jp2: 768x512, 1 component, 8-bit, 5/3, NL=5 ----
    // Greyscale single-tile single-layer reversible — exercises a real
    // multi-resolution 5/3 IDWT (NL=5) on production-sized data.
    [Fact]
    public void Decode_ConformanceFile4_MatchesReferenceExactly()
    {
        byte[] bytes = LoadTestFile("file4.jp2");
        int[][] reference = DecodeReference(bytes);
        Jp2DecodeResult ours = new Jp2StreamDecoder().Decode(bytes);

        Assert.Equal(reference.Length, ours.NumberOfComponents);
        for (var c = 0; c < reference.Length; c++)
            Assert.Equal(reference[c], ours.ComponentData[c]);
    }

    // ---- conformance/file6.jp2: 768x512, 1 component, 12-bit, 5/3, NL=5 ----
    // Greyscale with 12-bit precision — first conformance image past 8-bit
    // depth. Tests that Tier-1 magnitude bits, dequant integer arithmetic,
    // IDWT integer arithmetic, DC level shift (range 1<<11 = 2048), and
    // ClipInt's max-value (4095) all extend cleanly past byte width.
    [Fact]
    public void Decode_ConformanceFile6_MatchesReferenceExactly()
    {
        byte[] bytes = LoadTestFile("file6.jp2");
        int[][] reference = DecodeReference(bytes);
        Jp2DecodeResult ours = new Jp2StreamDecoder().Decode(bytes);

        Assert.Equal(reference.Length, ours.NumberOfComponents);
        for (var c = 0; c < reference.Length; c++)
            Assert.Equal(reference[c], ours.ComponentData[c]);
    }

    // ---- conformance/file5.jp2: 768x512, 3 components, 8-bit, 5/3, NL=5, MCT=Y ----
    // Second RGB+MCT conformance image — different content than file1, same
    // pipeline. Catches MCT bugs that happen to round-trip on file1 alone.
    [Fact]
    public void Decode_ConformanceFile5_MatchesReferenceExactly()
    {
        byte[] bytes = LoadTestFile("file5.jp2");
        int[][] reference = DecodeReference(bytes);
        Jp2DecodeResult ours = new Jp2StreamDecoder().Decode(bytes);

        Assert.Equal(reference.Length, ours.NumberOfComponents);
        for (var c = 0; c < reference.Length; c++)
            Assert.Equal(reference[c], ours.ComponentData[c]);
    }

    // ---- conformance/file7.jp2: 480x640, 3 components, 16-bit, 5/3, NL=5, MCT=Y ----
    // 16-bit RGB plus inverse RCT — file1's structure but at 16-bit precision.
    // Stresses the same paths file6 exercises (range/clip/level shift past
    // byte width) and the inverse RCT path together. RCT is integer-exact so
    // the only added risk vs 8-bit is overflow / signedness in the larger
    // arithmetic range.
    //
    // file7 also carries an embedded colorspace transform (CSJ2K default
    // decode produces values offset by a few thousand per component — typical
    // sRGB gamma / ICC application). We compare against CSJ2K's
    // nocolorspace=on path, which is what our raw codestream output should
    // match.
    [Fact]
    public void Decode_ConformanceFile7_MatchesReferenceExactly()
    {
        byte[] bytes = LoadTestFile("file7.jp2");
        int[][] reference = DecodeReference(bytes, noColorSpace: true);
        Jp2DecodeResult ours = new Jp2StreamDecoder().Decode(bytes);

        Assert.Equal(reference.Length, ours.NumberOfComponents);
        for (var c = 0; c < reference.Length; c++)
            Assert.Equal(reference[c], ours.ComponentData[c]);
    }

    // ---- conformance/file8.jp2: 700x400, 1 component, 8-bit, 5/3, NL=5 ----
    // Non-power-of-two dimensions in both axes (700 = 2^2·175, 400 = 2^4·25).
    // The earlier "89% diff" symptom was not a codec bug — it was CSJ2K's
    // CoreJ2K.Icc.ICCProfiler applying a monochrome sRGB tone curve from the
    // ICC profile embedded in file8's JP2 wrapper. file4 / test_8x8 /
    // test_16x16 don't carry that profile and decode through
    // EnumeratedColorSpaceMapper, which is pass-through; file9 has PCLR but
    // no ICC, so palette expansion still runs through ColorSpaceMapper
    // without value modification.
    //
    // For this comparison we set noColorSpace=true so CSJ2K skips the entire
    // colorspace mapping chain (including ICC), returning raw decoded
    // grayscale samples that match what Jp2Codec produces. The actual
    // EBCOT / dequant / IDWT path was always bit-exact for file8 (verified
    // via per-codeblock and per-level dumps against CSJ2K's StdDequantizer /
    // InvWTFull).
    [Fact]
    public void Decode_ConformanceFile8_MatchesReferenceExactly()
    {
        byte[] bytes = LoadTestFile("file8.jp2");
        int[][] reference = DecodeReference(bytes, noColorSpace: true);
        Jp2DecodeResult ours = new Jp2StreamDecoder().Decode(bytes);

        Assert.Equal(reference.Length, ours.NumberOfComponents);
        for (var c = 0; c < reference.Length; c++)
            Assert.Equal(reference[c], ours.ComponentData[c]);
    }

    // ---- conformance/file9.jp2: 768x512, 1 codestream component, 8-bit, 5/3, NL=5 ----
    // The codestream has 1 indexed component, but the JP2 wrapper carries a
    // PCLR palette box that expands the 1 indexed channel to 3 RGB channels.
    [Fact]
    public void Decode_ConformanceFile9_MatchesReferenceExactly()
    {
        byte[] bytes = LoadTestFile("file9.jp2");
        int[][] reference = DecodeReference(bytes);
        Jp2DecodeResult ours = new Jp2StreamDecoder().Decode(bytes);

        Assert.Equal(reference.Length, ours.NumberOfComponents);
        for (var c = 0; c < reference.Length; c++)
            Assert.Equal(reference[c], ours.ComponentData[c]);
    }

    // ---- conformance/subsampling_1.jp2: 1280x1024, 3 RGB, 9/7, NL=5, MCT=N, 6 layers ----
    // First conformance image to exercise the 9/7 irreversible path
    // (InverseLifting97, InverseDwt2D float overload). No MCT — chroma
    // subsampled 4:2:0. Like file3, structural test only — bit-exact
    // differential blocked by the same CSJ2K-J2kImage-vs-subsampled crash.
    [Fact]
    public void Decode_ConformanceSubsampling1_StructureMatchesSpec()
    {
        byte[] bytes = LoadTestFile("subsampling_1.jp2");
        Jp2DecodeResult r = new Jp2StreamDecoder().Decode(bytes);

        Assert.Equal(1280, r.Width);
        Assert.Equal(1024, r.Height);
        Assert.Equal(3, r.NumberOfComponents);

        Assert.Equal(1280, r.ComponentWidth[0]);
        Assert.Equal(1024, r.ComponentHeight[0]);
        Assert.Equal(640,  r.ComponentWidth[1]);
        Assert.Equal(512,  r.ComponentHeight[1]);
        Assert.Equal(640,  r.ComponentWidth[2]);
        Assert.Equal(512,  r.ComponentHeight[2]);

        for (var c = 0; c < 3; c++)
        {
            Assert.Equal(8, r.ComponentPrecision[c]);
            Assert.False(r.ComponentSigned[c]);
            int len = r.ComponentWidth[c] * r.ComponentHeight[c];
            Assert.Equal(len, r.ComponentData[c].Length);
            int min = int.MaxValue, max = int.MinValue;
            foreach (int v in r.ComponentData[c])
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }
            Assert.InRange(min, 0, 255);
            Assert.InRange(max, 0, 255);
        }
    }

    // ---- conformance/subsampling_2.jp2: 1280x1024, 3 RGB at 2x2, 9/7, MCT=Y, 5 layers ----
    // 9/7 + inverse irreversible component transform (ICT). All three
    // components are subsampled 2×2 from the reference grid (codestream
    // dimensions 640×512). This is the only conformance image that
    // exercises InverseIct against real data.
    [Fact]
    public void Decode_ConformanceSubsampling2_StructureMatchesSpec()
    {
        byte[] bytes = LoadTestFile("subsampling_2.jp2");
        Jp2DecodeResult r = new Jp2StreamDecoder().Decode(bytes);

        Assert.Equal(1280, r.Width);
        Assert.Equal(1024, r.Height);
        Assert.Equal(3, r.NumberOfComponents);

        // All three components are subsampled 2x2 from the reference grid.
        for (var c = 0; c < 3; c++)
        {
            Assert.Equal(640, r.ComponentWidth[c]);
            Assert.Equal(512, r.ComponentHeight[c]);
            Assert.Equal(8, r.ComponentPrecision[c]);
            Assert.False(r.ComponentSigned[c]);
            Assert.Equal(640 * 512, r.ComponentData[c].Length);
            int min = int.MaxValue, max = int.MinValue;
            foreach (int v in r.ComponentData[c])
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }
            Assert.InRange(min, 0, 255);
            Assert.InRange(max, 0, 255);
        }
    }
}
