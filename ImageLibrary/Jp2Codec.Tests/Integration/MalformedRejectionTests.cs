using System.Runtime.CompilerServices;

namespace Jp2Codec.Tests.Integration;

/// <summary>
/// The OpenJPEG / GDAL non-regression corpus bundles deliberately-malformed and
/// fuzzer / crash-test inputs — truncated packet headers, a missing COD segment,
/// degenerate SIZ extents, out-of-range bit depths, oversized component grids,
/// duplicate tile-parts, and codestreams that drive a subband shift negative.
/// A hardened decoder must reject every one of them <em>cleanly</em>: with an
/// <see cref="InvalidDataException"/>, the single contract the public
/// <see cref="Jpeg2000.Decompress"/> facade documents for bad input — not by
/// letting a raw framework exception (ArgumentOutOfRangeException, EndOfStream,
/// IndexOutOfRange, …) leak through to callers that do not wrap the decode, such
/// as the ImageUtility <c>CustomJpeg2000Codec</c> / <c>CodecRegistry</c> path.
/// </summary>
public class MalformedRejectionTests
{
    [Theory]
    [InlineData("issue420.jp2")]                 // drives CoordMath.CeilDivPow2 to a negative exponent — previously leaked a raw ArgumentOutOfRangeException
    [InlineData("issue427-null-image-size.jp2")] // degenerate SIZ image extent
    [InlineData("issue418.jp2")]                 // SIZ component bit depth 128, outside [1, 38]
    [InlineData("issue408.jp2")]                 // main header missing the required COD segment
    [InlineData("issue390.jp2")]                 // Lblock increment past the sanity cap
    [InlineData("issue391.jp2")]                 // duplicate tile-part TPsot=0
    [InlineData("issue393.jp2")]                 // expected a J2K marker, got 0x80FF
    [InlineData("issue395.jp2")]                 // expected SOT/EOC, got 0x221A
    [InlineData("issue362-2894.jp2")]            // ftyp box length overruns the buffer
    public void Malformed_corpus_file_rejects_cleanly(string fileName)
    {
        string path = NonRegressionPath(fileName);

        // The OpenJPEG/GDAL non-regression corpus is local-only (untracked) — skip (not fail)
        // where it isn't present, e.g. Mac/Linux/CI checkouts.
        Assert.SkipUnless(File.Exists(path), $"Non-regression corpus not present: {path}");

        byte[] bytes = File.ReadAllBytes(path);

        // Must throw, and the throw must be the clean domain exception. xUnit's
        // Assert.Throws<T> demands the *exact* type, so a future raw-exception
        // regression (e.g. the issue420 ArgumentOutOfRangeException leak this test
        // was written to lock) fails here instead of silently reaching callers.
        Assert.Throws<InvalidDataException>(
            () => Jpeg2000.Decompress(bytes, out _, out _, out _));
    }

    private static string NonRegressionPath(string fileName, [CallerFilePath] string thisFile = "")
    {
        // thisFile = <repo>/ImageLibrary/Jp2Codec.Tests/Integration/MalformedRejectionTests.cs
        string integrationDir = Path.GetDirectoryName(thisFile)!;
        string imageLibrary = Path.GetFullPath(Path.Combine(integrationDir, "..", ".."));
        return Path.Combine(imageLibrary, "TestImages", "jp2_test", "nonregression", fileName);
    }
}
