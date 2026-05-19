using Jp2Codec.Codestream.Segments;
using Jp2Codec.TileAssembly;

namespace Jp2Codec.Tests.TileAssembly;

/// <summary>
/// Unit-level checks for the PPM (main-header packed headers) slicer and
/// PPT (per-tile-part packed headers) builder. The conformance corpus
/// exercises both at the integration level via <c>g1..g4_colr.j2c</c>,
/// but CSJ2K can't reference-decode those files (it throws inside
/// <c>HeaderDecoder.readPPM</c>), so we rely on these targeted tests to
/// pin the byte-level behaviour.
/// </summary>
public class PackedHeaderStreamTests
{
    private static PpmSegment Ppm(byte zppm, params byte[] payload) =>
        new PpmSegment(zppm, payload);

    private static PptSegment Ppt(byte zppt, params byte[] payload) =>
        new PptSegment(zppt, payload);

    [Fact]
    public void PpmSlicer_SingleSegmentTwoTileParts_ReturnsExpectedChunks()
    {
        // Nppm0 = 3 → bytes 0xAA 0xBB 0xCC; Nppm1 = 2 → bytes 0xDD 0xEE.
        var seg = Ppm(0,
            0, 0, 0, 3, 0xAA, 0xBB, 0xCC,
            0, 0, 0, 2, 0xDD, 0xEE);
        var slicer = new PpmStreamSlicer(new[] { seg });

        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, slicer.NextTilePartChunk());
        Assert.Equal(new byte[] { 0xDD, 0xEE }, slicer.NextTilePartChunk());
        Assert.True(slicer.IsExhausted);
    }

    [Fact]
    public void PpmSlicer_MultipleSegmentsConcatenateInZppmOrder()
    {
        // First marker in codestream is Zppm=1, second is Zppm=0 — the slicer
        // must sort by Zppm before concatenation.
        var segLater = Ppm(1, 0xCC, 0, 0, 0, 1, 0xDD);     // tail of tuple 0 + tuple 1 (Nppm=1, byte 0xDD)
        var segFirst = Ppm(0, 0, 0, 0, 3, 0xAA, 0xBB);     // start of tuple 0 (Nppm=3, two of three bytes)
        var slicer = new PpmStreamSlicer(new[] { segLater, segFirst });

        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, slicer.NextTilePartChunk());
        Assert.Equal(new byte[] { 0xDD }, slicer.NextTilePartChunk());
        Assert.True(slicer.IsExhausted);
    }

    [Fact]
    public void PpmSlicer_NppmStraddlesSegmentBoundary_StillRecoversChunk()
    {
        // Even the 4-byte Nppm length field may be split between two PPM
        // markers — the concatenation has to handle byte-level continuity.
        var seg0 = Ppm(0, 0, 0, 0);              // first 3 bytes of Nppm
        var seg1 = Ppm(1, 4, 0xAA, 0xBB, 0xCC, 0xDD); // last Nppm byte (=4) + 4 payload bytes
        var slicer = new PpmStreamSlicer(new[] { seg0, seg1 });

        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }, slicer.NextTilePartChunk());
        Assert.True(slicer.IsExhausted);
    }

    [Fact]
    public void PpmSlicer_ZeroLengthChunk_IsLegal()
    {
        // Nppm = 0 → tile-part contributes no packet-header bytes. Legal per
        // A.7.4 (e.g. a degenerate tile-part with no packets).
        var seg = Ppm(0,
            0, 0, 0, 0,
            0, 0, 0, 2, 0xFF, 0xFE);
        var slicer = new PpmStreamSlicer(new[] { seg });

        Assert.Equal(Array.Empty<byte>(), slicer.NextTilePartChunk());
        Assert.Equal(new byte[] { 0xFF, 0xFE }, slicer.NextTilePartChunk());
        Assert.True(slicer.IsExhausted);
    }

    [Fact]
    public void PpmSlicer_ExhaustedStream_ThrowsOnNextChunk()
    {
        var seg = Ppm(0, 0, 0, 0, 1, 0xAA);
        var slicer = new PpmStreamSlicer(new[] { seg });
        _ = slicer.NextTilePartChunk();
        Assert.True(slicer.IsExhausted);
        Assert.Throws<System.IO.InvalidDataException>(() => slicer.NextTilePartChunk());
    }

    [Fact]
    public void PpmSlicer_TruncatedNppmPayload_Throws()
    {
        // Nppm = 4 but only 2 payload bytes follow.
        var seg = Ppm(0, 0, 0, 0, 4, 0xAA, 0xBB);
        var slicer = new PpmStreamSlicer(new[] { seg });
        Assert.Throws<System.IO.InvalidDataException>(() => slicer.NextTilePartChunk());
    }

    [Fact]
    public void PptStreamBuilder_NoSegments_ReturnsEmpty()
    {
        Assert.Equal(Array.Empty<byte>(), PptStreamBuilder.Concatenate(Array.Empty<PptSegment>()));
    }

    [Fact]
    public void PptStreamBuilder_ConcatenatesPayloadsInZpptOrder()
    {
        var s1 = Ppt(1, 0xCC, 0xDD);
        var s0 = Ppt(0, 0xAA, 0xBB);
        // Input order is intentionally reversed to confirm the builder sorts.
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD },
            PptStreamBuilder.Concatenate(new[] { s1, s0 }));
    }
}
