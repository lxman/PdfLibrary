using ImageLibrary.Compression.Ccitt;
using Xunit.Abstractions;

namespace Jbig2Decoder.Tests.Mmr;

/// <summary>
/// Differential tests against jbig2dec for JBIG2 MMR collective bitmaps fed
/// to <see cref="CcittDecoder"/>. The fixture format (MMR1):
///   magic        4 bytes  "MMR1"
///   width        4 bytes  (le u32)
///   height       4 bytes
///   stride       4 bytes  (= (width+7)/8)
///   bmsize       4 bytes  (compressed length)
///   mmr_data     bmsize bytes
///   expected     stride*height bytes (decoded by jbig2dec)
/// </summary>
public class CcittDecoderJbig2MmrTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "Mmr", name);

    private readonly ITestOutputHelper _out;
    public CcittDecoderJbig2MmrTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void MultiRow_1706x9_042_11_Class8() => RunFixture("042_11_class8_1706x9.bin");

    private void RunFixture(string fixtureName)
    {
        var (mmrInput, width, height, stride, expected) = LoadFixture(FixturePath(fixtureName));

        _out.WriteLine($"Fixture: {width}x{height} stride={stride} mmr_input={mmrInput.Length} bytes");

        var dec = new CcittDecoder(new CcittOptions
        {
            Group = CcittGroup.Group4,
            K = -1,
            Width = width,
            Height = height,
            BlackIs1 = true,
            EndOfBlock = true,
        });
        byte[] actual = dec.Decode(mmrInput);

        _out.WriteLine($"Decoded {actual.Length} bytes (expected {expected.Length})");

        if (actual.Length < expected.Length)
        {
            int rowsDecoded = actual.Length / stride;
            _out.WriteLine($"Decoded only {rowsDecoded} of {height} rows before stopping");
        }

        // Find first divergence at the bit level so the failure pinpoints
        // where in the stream the decoder went wrong.
        int compareLen = Math.Min(actual.Length, expected.Length);
        for (var i = 0; i < compareLen; i++)
        {
            if (actual[i] == expected[i]) continue;
            int row = i / stride;
            int colByte = i % stride;
            _out.WriteLine($"First byte divergence: row={row}, byte={colByte} (x≈{colByte * 8}-{colByte * 8 + 7}): expected=0x{expected[i]:X2}, actual=0x{actual[i]:X2}");
            Assert.Fail($"MMR diverges from jbig2dec at byte {i}");
        }

        if (actual.Length != expected.Length)
            Assert.Fail($"MMR length mismatch: decoded {actual.Length} bytes, expected {expected.Length} ({height - actual.Length / stride} rows missing)");
    }

    private static (byte[] mmr, int width, int height, int stride, byte[] expected) LoadFixture(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (System.Text.Encoding.ASCII.GetString(data, 0, 4) != "MMR1")
            throw new InvalidDataException($"Bad magic in {path}");
        int p = 4;
        int width  = BitConverter.ToInt32(data, p); p += 4;
        int height = BitConverter.ToInt32(data, p); p += 4;
        int stride = BitConverter.ToInt32(data, p); p += 4;
        int bmsize = BitConverter.ToInt32(data, p); p += 4;
        var mmr = new byte[bmsize];
        Buffer.BlockCopy(data, p, mmr, 0, bmsize);
        p += bmsize;
        var expected = new byte[stride * height];
        Buffer.BlockCopy(data, p, expected, 0, expected.Length);
        return (mmr, width, height, stride, expected);
    }
}
