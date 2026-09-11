using System;
using System.Collections.Generic;
using CcittCodec;
using Xunit;

namespace CcittCodec.Tests;

/// <summary>
/// Regression tests for Group 3 line synchronisation: the fill bits and EOL codes that
/// separate lines in a real T.4 stream.
/// </summary>
/// <remarks>
/// These exist because of a corpus of scanned government contracts in which every Alabama
/// scan decoded to a blank page. The images declared <c>/K 0</c> with no <c>/EndOfLine</c>,
/// yet carried an EOL on every one of their 2,200 lines, each preceded by 6 fill bits so
/// that the line ended byte-aligned. The decoder skipped EOLs only when <c>/EndOfLine</c>
/// was declared, and its skipper gave up after 12 zero bits *without restoring the reader*,
/// so a single fill bit desynchronised the whole stream. A 2,200-row page decoded to 2 rows
/// and rendered white, with no exception thrown anywhere.
///
/// The assertions therefore check decoded *size and content*, not merely that decoding
/// returned something: the original defect produced a perfectly well-formed short buffer.
/// </remarks>
public class Group3EolSynchronizationTests
{
    private const int Columns = 64;

    /// <summary>
    /// Four rows, each: 16 white, 32 black, 16 white. Chosen so every run is a single
    /// terminating code and the expected raster is trivially checkable.
    /// </summary>
    private static readonly int[] RowRuns = [16, 32, 16];

    [Theory]
    [InlineData(0)]   // EOL with no fill -- the plain case the decoder still got wrong,
                      // because skipping was gated on /EndOfLine being declared
    [InlineData(1)]
    [InlineData(6)]   // what Alabama's scans actually use
    [InlineData(23)]  // more fill than the EOL itself is long
    public void UndeclaredEolWithFillBits_DecodesEveryRow(int fillBits)
    {
        const int rows = 4;
        byte[] encoded = Encode(rows, fillBits);

        // endOfLine: false -- deliberately mirrors /DecodeParms that says nothing about EOLs
        // while the stream is full of them. This is the combination that failed.
        byte[] decoded = Ccitt.Decompress(encoded, 0, Columns, rows,
            blackIs1: false, encodedByteAlign: false, endOfLine: false, endOfBlock: true);

        AssertRaster(decoded, rows);
    }

    [Fact]
    public void DeclaredEolWithFillBits_DecodesEveryRow()
    {
        const int rows = 4;
        byte[] encoded = Encode(rows, fillBits: 6);

        byte[] decoded = Ccitt.Decompress(encoded, 0, Columns, rows,
            blackIs1: false, encodedByteAlign: false, endOfLine: true, endOfBlock: true);

        AssertRaster(decoded, rows);
    }

    /// <summary>
    /// Guards the other direction: EOLs are now skipped unconditionally, so a stream that
    /// has none at all must still decode. A run of 11 zeros cannot begin any valid run
    /// code, so the look-ahead is meant to cost nothing here.
    /// </summary>
    [Fact]
    public void StreamWithNoEolMarkers_StillDecodesEveryRow()
    {
        const int rows = 4;
        byte[] encoded = Encode(rows, fillBits: 0, emitEol: false);

        byte[] decoded = Ccitt.Decompress(encoded, 0, Columns, rows,
            blackIs1: false, encodedByteAlign: false, endOfLine: false, endOfBlock: true);

        AssertRaster(decoded, rows);
    }

    private static void AssertRaster(byte[] decoded, int rows)
    {
        int bytesPerRow = (Columns + 7) / 8;
        Assert.Equal(bytesPerRow * rows, decoded.Length);

        // blackIs1 is false, so white is 1 and black is 0.
        // 16 white, 32 black, 16 white over 64 columns => FF FF 00 00 00 00 FF FF.
        byte[] expectedRow = [0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF];
        for (var r = 0; r < rows; r++)
        {
            byte[] actual = decoded[(r * bytesPerRow)..((r + 1) * bytesPerRow)];
            Assert.Equal(expectedRow, actual);
        }
    }

    /// <summary>
    /// Hand-assembles a Group 3 1D stream. Uses <see cref="HuffmanTables"/> rather than
    /// literal code values so the fixture cannot drift away from the decoder's own tables.
    /// </summary>
    private static byte[] Encode(int rows, int fillBits, bool emitEol = true)
    {
        var bits = new List<int>();

        for (var r = 0; r < rows; r++)
        {
            if (emitEol)
            {
                for (var i = 0; i < fillBits; i++) bits.Add(0);
                Append(bits, CcittConstants.EolCode, CcittConstants.EolBits);
            }

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
