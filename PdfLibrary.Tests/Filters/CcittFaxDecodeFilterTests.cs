using CcittCodec;
using PdfLibrary.Filters;

namespace PdfLibrary.Tests.Filters;

/// <summary>
/// Guards the <c>CCITTFaxDecode</c> filter against a Group 3 stream whose lines are separated by
/// fill bits and EOL codes that <c>/DecodeParms</c> does not declare.
/// </summary>
/// <remarks>
/// <para>
/// This duplicates coverage that also exists in <c>CcittCodec.Tests</c>, deliberately. Neither
/// <c>ci.yml</c> nor <c>publish-nuget.yml</c> runs that project -- both run
/// <c>PdfLibrary.Tests</c> only -- so a codec-level regression would reach a release unchallenged.
/// </para>
/// <para>
/// The defect this guards was silent: a 2200-row scan decoded to 2 rows and rendered as a blank
/// white page with no exception, and a document corpus indexed 45 pages as legitimately empty. So
/// the assertions are on decoded size and pixel content, never on "decoding returned something" --
/// the bug produced a perfectly well-formed short buffer.
/// </para>
/// </remarks>
public class CcittFaxDecodeFilterTests
{
    private const int Columns = 64;
    private const int Rows = 4;

    /// <summary>16 white, 32 black, 16 white -- every run is a single terminating code.</summary>
    private static readonly int[] RowRuns = [16, 32, 16];

    [Theory]
    [InlineData(0)]
    [InlineData(6)]   // byte-aligns the line, which is why encoders emit it
    [InlineData(23)]
    public void UndeclaredEolAndFillBits_DecodeEveryRow(int fillBits)
    {
        var filter = new CcittFaxDecodeFilter();

        // Note what is NOT here: no EndOfLine, no EncodedByteAlign. This is exactly what the
        // offending PDFs declare, while their streams carry an EOL on every line.
        var parms = new Dictionary<string, object>
        {
            ["K"] = 0,
            ["Columns"] = Columns,
            ["Rows"] = Rows
        };

        byte[] decoded = filter.Decode(Encode(fillBits), parms);

        int bytesPerRow = (Columns + 7) / 8;
        Assert.Equal(bytesPerRow * Rows, decoded.Length);

        // BlackIs1 defaults to false, so white is 1 and black is 0.
        byte[] expectedRow = [0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF];
        for (var r = 0; r < Rows; r++)
        {
            Assert.Equal(expectedRow, decoded[(r * bytesPerRow)..((r + 1) * bytesPerRow)]);
        }
    }

    /// <summary>
    /// Builds a Group 3 1D stream from <see cref="HuffmanTables"/> rather than literal code values,
    /// so the fixture cannot drift away from the decoder's own tables.
    /// </summary>
    private static byte[] Encode(int fillBits)
    {
        var bits = new List<int>();

        for (var r = 0; r < Rows; r++)
        {
            for (var i = 0; i < fillBits; i++) bits.Add(0);
            Append(bits, CcittConstants.EolCode, CcittConstants.EolBits);

            var white = true;
            foreach (int run in RowRuns)
            {
                HuffmanTables.HuffmanCode code = white
                    ? HuffmanTables.WhiteTerminatingCodes[run]
                    : HuffmanTables.BlackTerminatingCodes[run];
                Append(bits, code.Code, code.BitLength);
                white = !white;
            }
        }

        var bytes = new byte[(bits.Count + 7) / 8];
        for (var i = 0; i < bits.Count; i++)
        {
            if (bits[i] != 0) bytes[i / 8] |= (byte)(0x80 >> (i % 8));
        }
        return bytes;
    }

    private static void Append(List<int> bits, int code, int bitLength)
    {
        for (int i = bitLength - 1; i >= 0; i--) bits.Add((code >> i) & 1);
    }
}
