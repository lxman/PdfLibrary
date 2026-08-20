using System;
using System.Collections.Generic;
using System.Linq;

namespace CffTestFixtures;

/// <summary>
/// Builds minimal but structurally valid CFF font programs for charset tests: header, Name INDEX, Top
/// DICT INDEX, empty String and Global Subr INDEXes, a CharStrings INDEX of <c>endchar</c>-only glyphs,
/// and whatever a given shape needs beyond that. Every Top DICT number uses the fixed 5-byte (0x1D)
/// integer form, so the dict's length — and therefore every absolute offset inside it — is known before
/// the bytes are laid out.
/// <para>Shared source, compiled into both FontParser.Tests and PdfLibrary.Tests (see the Compile/Link
/// item in PdfLibrary.Tests.csproj): the parser-level and the EmbeddedFontMetrics-level charset tests
/// need the same fixtures, and a linked file beats a test-project-to-test-project reference.</para>
/// </summary>
internal static class MinimalCff
{
    private static readonly byte[] FontNameBytes = "TestFont"u8.ToArray();

    private const byte OpCharset = 15;
    private const byte OpCharStrings = 17;
    private const byte OpPrivate = 18;
    private const byte OpEscape = 0x0C;
    private const byte OpRosLow = 0x1E;
    private const byte OpFdArrayLow = 0x24;
    private const byte OpFdSelectLow = 0x25;
    private const byte OpFontNameLow = 0x26;

    /// <summary>nominalWidthX 0 — the smallest Private DICT the parser will accept.</summary>
    private static byte[] PrivateDict => [0x1D, 0, 0, 0, 0, 0x15];

    /// <summary>
    /// A non-CID CFF. <paramref name="charsetOperand"/> null omits the charset operator entirely (the
    /// spec default); <paramref name="customCharsetSids"/> emits a real format-0 charset table and points
    /// the operator at it, overriding <paramref name="charsetOperand"/>.
    /// </summary>
    public static byte[] Build(int? charsetOperand, int numGlyphs, ushort[]? customCharsetSids = null,
        List<byte[]>? customCharStrings = null)
    {
        List<byte[]> glyphs = customCharStrings ?? EndCharGlyphs(numGlyphs);
        int nameIndexSize = IndexSize([FontNameBytes], 1);
        int topDictLen = (charsetOperand is null && customCharsetSids is null ? 0 : 6) // charset
                         + 6                                                          // CharStrings
                         + 11;                                                        // Private
        int charStringsSize = IndexSize(glyphs, 2);
        byte[] charsetTable = BuildCharsetTable(customCharsetSids);

        // Layout: header | name | topDict | string | globalSubr | pad | charset | charStrings | private
        int charsetOffset = 4 + nameIndexSize + IndexSize([new byte[topDictLen]], 1) + 2 + 2 + 1;
        int charStringsOffset = charsetOffset + charsetTable.Length;
        int privateOffset = charStringsOffset + charStringsSize;

        var top = new List<byte>();
        if (customCharsetSids is not null) AppendNumberOp(top, charsetOffset, OpCharset);
        else if (charsetOperand is not null) AppendNumberOp(top, charsetOperand.Value, OpCharset);
        AppendNumberOp(top, charStringsOffset, OpCharStrings);
        AppendInt32(top, PrivateDict.Length);
        AppendInt32(top, privateOffset);
        top.Add(OpPrivate);
        Verify(top.Count == topDictLen, "Top DICT length differs from the value the offsets were built on");

        var data = new List<byte>();
        AppendPreamble(data, top);
        data.AddRange(charsetTable);
        AppendIndex(data, glyphs, offSize: 2);
        data.AddRange(PrivateDict);
        Verify(charStringsOffset == data.Count - charStringsSize - PrivateDict.Length,
            "CharStrings landed somewhere other than the offset written into the Top DICT");
        return data.ToArray();
    }

    /// <summary>
    /// A CID-keyed CFF (Top DICT carries ROS + FDArray + FDSelect) with NO charset operator — the shape a
    /// malformed CID font takes, and the one the ISOAdobe synthesis must refuse to guess at.
    /// </summary>
    public static byte[] BuildCid(int numGlyphs)
    {
        int nameIndexSize = IndexSize([FontNameBytes], 1);
        const int topDictLen = 17  // ROS (3 operands, 2-byte op)
                               + 6  // CharStrings
                               + 7  // FDArray (2-byte op)
                               + 7; // FDSelect (2-byte op)
        int charStringsSize = IndexSize(EndCharGlyphs(numGlyphs), 2);
        const int fontDictLen = 11 + 7; // Private (size, offset) + FontName (SID, 2-byte op)
        int fdArraySize = IndexSize([new byte[fontDictLen]], 2);
        int fdSelectSize = 1 + numGlyphs; // format 0: one FD index byte per glyph

        // Layout: header | name | topDict | string | globalSubr | pad | charStrings | fdArray | fdSelect | private
        int charStringsOffset = 4 + nameIndexSize + IndexSize([new byte[topDictLen]], 1) + 2 + 2 + 1;
        int fdArrayOffset = charStringsOffset + charStringsSize;
        int fdSelectOffset = fdArrayOffset + fdArraySize;
        int privateOffset = fdSelectOffset + fdSelectSize;

        var top = new List<byte>();
        AppendInt32(top, 1); // Registry SID   — any resolvable standard string
        AppendInt32(top, 1); // Ordering SID
        AppendInt32(top, 0); // Supplement
        top.Add(OpEscape);
        top.Add(OpRosLow);
        AppendNumberOp(top, charStringsOffset, OpCharStrings);
        AppendInt32(top, fdArrayOffset);
        top.Add(OpEscape);
        top.Add(OpFdArrayLow);
        AppendInt32(top, fdSelectOffset);
        top.Add(OpEscape);
        top.Add(OpFdSelectLow);
        Verify(top.Count == topDictLen, "CID Top DICT length differs from the value the offsets were built on");

        var fontDict = new List<byte>();
        AppendInt32(fontDict, PrivateDict.Length);
        AppendInt32(fontDict, privateOffset);
        fontDict.Add(OpPrivate);
        AppendInt32(fontDict, 1); // FontName SID
        fontDict.Add(OpEscape);
        fontDict.Add(OpFontNameLow);
        Verify(fontDict.Count == fontDictLen, "Font DICT length differs from the value the offsets were built on");

        var data = new List<byte>();
        AppendPreamble(data, top);
        AppendIndex(data, EndCharGlyphs(numGlyphs), offSize: 2);
        AppendIndex(data, [fontDict.ToArray()], offSize: 2);
        data.Add(0x00);                                  // FDSelect format 0
        data.AddRange(Enumerable.Repeat((byte)0, numGlyphs)); // every glyph -> FD 0
        data.AddRange(PrivateDict);
        return data.ToArray();
    }

    /// <summary>Header through the Encoding format byte — identical for both shapes.</summary>
    private static void AppendPreamble(List<byte> data, List<byte> topDict)
    {
        data.AddRange([0x01, 0x00, 0x04, 0x01]); // major, minor, hdrSize, offSize
        AppendIndex(data, [FontNameBytes], offSize: 1);
        AppendIndex(data, [topDict.ToArray()], offSize: 1);
        data.AddRange([0x00, 0x00]); // empty String INDEX
        data.AddRange([0x00, 0x00]); // empty Global Subr INDEX
        // The constructor reads an Encoding format byte at whatever position follows the Global Subr
        // INDEX (it does not seek to the Top DICT's Encoding offset). 0xFF is neither 0 nor 1, so no
        // Encoding is parsed and the reader is left where the next section starts.
        data.Add(0xFF);
    }

    private static List<byte[]> EndCharGlyphs(int numGlyphs) =>
        Enumerable.Repeat<byte[]>([0x0E], numGlyphs).ToList();

    private static byte[] BuildCharsetTable(ushort[]? sids)
    {
        if (sids is null) return [];
        var table = new List<byte> { 0x00 }; // format 0
        foreach (ushort sid in sids)
        {
            table.Add((byte)(sid >> 8));
            table.Add((byte)sid);
        }
        return table.ToArray();
    }

    private static int IndexSize(List<byte[]> entries, int offSize) =>
        2 + 1 + offSize * (entries.Count + 1) + entries.Sum(e => e.Length);

    private static void AppendNumberOp(List<byte> dict, int value, byte op)
    {
        AppendInt32(dict, value);
        dict.Add(op);
    }

    private static void AppendInt32(List<byte> dict, int value)
    {
        dict.Add(0x1D); // 5-byte integer operand (CFF spec Table 3)
        dict.Add((byte)(value >> 24));
        dict.Add((byte)(value >> 16));
        dict.Add((byte)(value >> 8));
        dict.Add((byte)value);
    }

    private static void AppendIndex(List<byte> data, List<byte[]> entries, int offSize)
    {
        data.Add((byte)(entries.Count >> 8));
        data.Add((byte)entries.Count);
        data.Add((byte)offSize);
        var offset = 1;
        AppendOffset(data, offset, offSize);
        foreach (byte[] entry in entries)
        {
            offset += entry.Length;
            AppendOffset(data, offset, offSize);
        }
        foreach (byte[] entry in entries) data.AddRange(entry);
    }

    private static void AppendOffset(List<byte> data, int offset, int offSize)
    {
        for (int shift = (offSize - 1) * 8; shift >= 0; shift -= 8)
            data.Add((byte)(offset >> shift));
    }

    /// <summary>Fails loudly on a self-inconsistent fixture — a builder bug must never present as a
    /// parser result. Plain exception rather than an assert, so the file stays framework-neutral.</summary>
    private static void Verify(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"MinimalCff fixture is inconsistent: {message}");
    }
}
