using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfLibrary.Tests.Fonts.Embedded;

/// <summary>
/// Hand-builds a minimal, self-contained raw CFF (Type1C) font program whose built-in Encoding
/// (format 1) maps a chosen character code to a glyph whose name is NOT what StandardEncoding's
/// Annex D.2 names that code — reproducing the shape of the CC-MAIN reproducer
/// (<c>2000_2000078.pdf</c>, symbolic Type1C Cyrillic Times clones, format-1 built-in encoding
/// mapping the upper band to <c>afiiNNNNN</c> glyphs) that motivated Task 8 (spec Amendment
/// 2026-08-15).
///
/// <para>Modeled on <c>MinimalType1CFont</c> (<c>WidthPrecedenceTests.cs</c>), same layout technique
/// (header | Name INDEX | Top DICT INDEX | String INDEX | empty Global Subr INDEX | Encoding | charset
/// | CharStrings INDEX | Private DICT, every Top DICT number in the fixed 5-byte 0x1D form). Two
/// differences from that fixture: (1) the String INDEX is non-empty — it carries the custom glyph
/// name, since <c>afii10034</c> is not one of the 391 CFF standard strings; (2) the Encoding block is a
/// REAL format-1 table instead of the format byte <c>0xFF</c> ("no encoding parsed") <c>MinimalType1CFont</c>
/// writes — this fixture's whole point is exercising <c>GetGlyphIdByCffEncoding</c> /
/// <c>GetCffGlyphNameByCharCode</c>, which only resolve through a parsed <c>Encoding0</c>/<c>Encoding1</c>.
///
/// <para>The Top DICT carries operator 16 pointing at the Encoding table, as Adobe TN #5176 requires
/// for a custom encoding. Deliberate padding separates the table from the Global Subr INDEX so this
/// fixture also proves the parser follows the operator instead of assuming a structural position.</para>
/// </summary>
internal static class SymbolicCffFixtureFont
{
    private static readonly byte[] FontNameBytes = Encoding.ASCII.GetBytes("SymbolicCffTest");

    private const byte OpEncoding = 16;
    private const byte OpCharset = 15;
    private const byte OpCharStrings = 17;
    private const byte OpPrivate = 18;
    private const byte T2Int16 = 0x1C; // Type2 charstring: next 2 bytes = big-endian signed int16 operand

    // CFF Technical Note #5176 Appendix A standard string SID 137 = "emdash" — a real Adobe-standard
    // glyph name, reachable only by NAME (never by this fixture's built-in Encoding), so the pre-fix
    // defect (StandardEncoding naming code 208 "emdash", and a name-first resolver finding THIS glyph
    // instead of the built-in-encoding one) has something real to wrongly resolve to.
    private const int SidEmdash = 137;

    // Custom string: not one of the 391 CFF standard strings, so it lives in the font's own String
    // INDEX. SID 391 is the first custom-string slot (CFF spec: custom SIDs start at 391).
    private const int SidCustomGlyph = 391;

    private static byte[] PrivateDict => [0x1D, 0, 0, 0, 0, 0x15];

    /// <summary>
    /// Builds the program. <paramref name="code"/>'s built-in Encoding entry resolves to GID 1, named
    /// <paramref name="customGlyphName"/> with advance <paramref name="customAdvance"/>; GID 2 is a
    /// second, unrelated glyph named "emdash" with advance <paramref name="emdashAdvance"/>, present in
    /// the charset but NOT reachable through the built-in Encoding.
    /// </summary>
    public static byte[] Build(byte code, string customGlyphName, int customAdvance, int emdashAdvance)
    {
        byte[] customGlyphNameBytes = Encoding.ASCII.GetBytes(customGlyphName);
        var charStrings = new List<byte[]> { Endchar(), Charstring(customAdvance), Charstring(emdashAdvance) };
        byte[] charsetTable = BuildCharsetTable([SidCustomGlyph, SidEmdash]); // GID1=custom, GID2=emdash

        int nameIndexSize = IndexSize([FontNameBytes], 1);
        // Encoding op + charset op + CharStrings op + Private (size+offset+op)
        const int topDictLen = 6 + 6 + 6 + 11;
        int stringIndexSize = IndexSize([customGlyphNameBytes], 1);
        int charStringsSize = IndexSize(charStrings, 2);

        // Format 1: format byte, nRanges byte, then nRanges * (First, NumberLeft) — one range mapping
        // exactly `code` to the sequentially-first-assigned GID (1), per Adobe TN #5176 §12 / Table 12.
        byte[] encodingTable = [0x01, 0x01, code, 0x00];

        // Layout: header | name | topDict | string | globalSubr | padding | encoding | charset | charStrings | private
        const int encodingPaddingLength = 3;
        int encodingOffset = 4 + nameIndexSize + IndexSize([new byte[topDictLen]], 1)
            + stringIndexSize + 2 /* empty Global Subr INDEX */ + encodingPaddingLength;
        int charsetOffset = encodingOffset + encodingTable.Length;
        int charStringsOffset = charsetOffset + charsetTable.Length;
        int privateOffset = charStringsOffset + charStringsSize;

        var top = new List<byte>();
        // Op 16 (Encoding): per TN #5176 its absence formally declares Standard encoding, so this
        // fixture's built-in Encoding must be pointed at explicitly to be spec-valid.
        AppendNumberOp(top, encodingOffset, OpEncoding);
        AppendNumberOp(top, charsetOffset, OpCharset);
        AppendNumberOp(top, charStringsOffset, OpCharStrings);
        AppendInt32(top, PrivateDict.Length);
        AppendInt32(top, privateOffset);
        top.Add(OpPrivate);
        Verify(top.Count == topDictLen, "Top DICT length differs from the value the offsets were built on");

        var data = new List<byte>();
        data.AddRange([0x01, 0x00, 0x04, 0x01]); // major, minor, hdrSize, offSize
        AppendIndex(data, [FontNameBytes], offSize: 1);
        AppendIndex(data, [top.ToArray()], offSize: 1);
        AppendIndex(data, [customGlyphNameBytes], offSize: 1); // String INDEX: SID 391 = customGlyphName
        data.AddRange([0x00, 0x00]); // empty Global Subr INDEX
        data.AddRange([0xFF, 0xFE, 0xFD]); // unreachable padding; old positional parser stops at 0xFF
        Verify(encodingOffset == data.Count, "encoding offset differs from where the encoding bytes actually land");
        data.AddRange(encodingTable); // real format-1 built-in Encoding — the point of this fixture
        Verify(charsetOffset == data.Count, "charset offset differs from where the charset bytes actually land");
        data.AddRange(charsetTable);
        Verify(charStringsOffset == data.Count, "CharStrings offset differs from where the INDEX actually lands");
        AppendIndex(data, charStrings, offSize: 2);
        Verify(privateOffset == data.Count, "Private DICT offset differs from where its bytes actually land");
        data.AddRange(PrivateDict);
        return data.ToArray();
    }

    /// <summary>A charstring encoding one leading width number (via the 2-byte int16 form) then
    /// <c>endchar</c> — stack count 1 at a normally-zero-arg operator, so the interpreter reads the
    /// number as (nominalWidthX + delta) rather than a real endchar operand.</summary>
    private static byte[] Charstring(int width) => [T2Int16, (byte)(width >> 8), (byte)width, 0x0E];

    /// <summary>Bare <c>endchar</c>, no leading number — glyph 0/.notdef.</summary>
    private static byte[] Endchar() => [0x0E];

    private static byte[] BuildCharsetTable(int[] sids)
    {
        var table = new List<byte> { 0x00 }; // format 0
        foreach (int sid in sids)
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
        if (!condition) throw new System.InvalidOperationException($"SymbolicCffFixtureFont fixture is inconsistent: {message}");
    }
}
