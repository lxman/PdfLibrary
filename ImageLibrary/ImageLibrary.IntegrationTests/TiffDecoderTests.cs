using ImageLibrary.Container.Tiff;
using Xunit;

namespace ImageLibrary.IntegrationTests;

/// <summary>
/// Tests pinning TiffDecoder behavior contracts that span multiple
/// internal subsystems (compression dispatchers, etc.).
/// </summary>
public class TiffDecoderTests
{
    [Fact]
    public void Decode_JpegCompression_ThrowsTiffException()
    {
        // The JPEG-compression path is intentionally stubbed during the
        // JPEG decoder rewrite. When the rewire lands, this assertion's
        // expected exception should change rather than disappear silently.
        byte[] tiff = BuildMinimalTiff(compression: 7);

        var ex = Assert.Throws<TiffException>(() => TiffDecoder.Decode(tiff));
        Assert.Contains("JPEG", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a minimal valid little-endian TIFF (1×1 pixel, 1 strip, 1 byte of garbage strip data).
    /// </summary>
    private static byte[] BuildMinimalTiff(ushort compression)
    {
        using var ms = new System.IO.MemoryStream();
        using var w = new System.IO.BinaryWriter(ms);

        // Header
        w.Write((byte)'I');
        w.Write((byte)'I');
        w.Write((ushort)42);
        w.Write((uint)8);

        // IFD: 5 entries
        w.Write((ushort)5);

        // 256 ImageWidth  (LONG, count=1, value=1)
        w.Write((ushort)256); w.Write((ushort)4); w.Write((uint)1); w.Write((uint)1);
        // 257 ImageHeight (LONG, count=1, value=1)
        w.Write((ushort)257); w.Write((ushort)4); w.Write((uint)1); w.Write((uint)1);
        // 259 Compression (SHORT, count=1, value=<compression>)
        w.Write((ushort)259); w.Write((ushort)3); w.Write((uint)1);
        w.Write((ushort)compression); w.Write((ushort)0);
        // 273 StripOffsets (LONG, count=1, value=74 — start of strip data, just past next-IFD offset)
        w.Write((ushort)273); w.Write((ushort)4); w.Write((uint)1); w.Write((uint)74);
        // 279 StripByteCounts (LONG, count=1, value=1)
        w.Write((ushort)279); w.Write((ushort)4); w.Write((uint)1); w.Write((uint)1);

        // Next IFD offset = 0
        w.Write((uint)0);

        // Strip data — never read because DecompressJpeg throws first.
        w.Write((byte)0);

        return ms.ToArray();
    }
}
