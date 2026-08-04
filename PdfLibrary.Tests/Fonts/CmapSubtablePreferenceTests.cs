using FontParser.Tables.Cmap;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// A cmap lookup keyed by a Unicode codepoint must consult a Unicode subtable, not whichever subtable
/// happens to sit at the lowest offset in the file.
///
/// <para>Found 2026-08-04 rendering GWG090: <c>C059-Italic.otf</c> lists its subtables Mac/Roman,
/// Unicode, Windows/UnicodeBmp, and <see cref="CmapTable.GetGlyphId"/> returned the first non-zero
/// result in offset order — so U+00E4 'ä' was resolved through MacRoman, where 0xE4 is a different
/// glyph entirely. Below U+0080 MacRoman and Unicode coincide, so ASCII looked correct and only
/// accented text was wrong. <c>timesi.ttf</c> happened to list Unicode first and worked by luck.</para>
/// </summary>
public class CmapSubtablePreferenceTests
{
    private static void U16(List<byte> b, int v) { b.Add((byte)(v >> 8)); b.Add((byte)v); }
    private static void U32(List<byte> b, uint v)
    { b.Add((byte)(v >> 24)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 8)); b.Add((byte)v); }

    /// <summary>Format 0: a flat 256-entry byte array indexed by character code. This is the shape a
    /// Mac/Roman subtable takes.</summary>
    private static byte[] Format0(Dictionary<byte, byte> map)
    {
        var t = new List<byte>();
        U16(t, 0);            // format
        U16(t, 262);          // length
        U16(t, 0);            // language
        for (var i = 0; i < 256; i++) t.Add(map.TryGetValue((byte)i, out byte g) ? g : (byte)0);
        return t.ToArray();
    }

    /// <summary>Format 4 with a single one-character segment plus the mandatory 0xFFFF terminator.
    /// This is the shape a Windows/UnicodeBmp subtable takes.</summary>
    private static byte[] Format4(ushort codePoint, ushort glyphId)
    {
        var t = new List<byte>();
        const int segCount = 2;
        U16(t, 4);                    // format
        U16(t, 16 + segCount * 8);    // length
        U16(t, 0);                    // language
        U16(t, segCount * 2);         // segCountX2
        U16(t, 2); U16(t, 0); U16(t, 0);            // searchRange/entrySelector/rangeShift
        U16(t, codePoint); U16(t, 0xFFFF);          // endCode[]
        U16(t, 0);                                   // reservedPad
        U16(t, codePoint); U16(t, 0xFFFF);          // startCode[]
        U16(t, (ushort)(glyphId - codePoint)); U16(t, 1);   // idDelta[]
        U16(t, 0); U16(t, 0);                        // idRangeOffset[]
        return t.ToArray();
    }

    /// <summary>Assembles a cmap whose Mac/Roman subtable sits at a LOWER offset than the
    /// Windows/UnicodeBmp one — the ordering that exposed the bug.</summary>
    private static byte[] CmapMacFirst(ushort codePoint, ushort macGlyph, ushort unicodeGlyph)
    {
        byte[] mac = Format0(new Dictionary<byte, byte> { [(byte)codePoint] = (byte)macGlyph });
        byte[] win = Format4(codePoint, unicodeGlyph);

        const int headerSize = 4 + 2 * 8;          // version + numTables + two 8-byte records
        var macOffset = (uint)headerSize;          // Mac FIRST in the file
        var winOffset = (uint)(headerSize + mac.Length);

        var f = new List<byte>();
        U16(f, 0);                                  // version
        U16(f, 2);                                  // numTables
        U16(f, 1); U16(f, 0); U32(f, macOffset);    // (1,0) Macintosh/Roman
        U16(f, 3); U16(f, 1); U32(f, winOffset);    // (3,1) Windows/UnicodeBmp
        f.AddRange(mac);
        f.AddRange(win);
        return f.ToArray();
    }

    [Fact]
    public void A_unicode_codepoint_resolves_through_the_unicode_subtable_not_the_mac_one()
    {
        // 0xE4 means two different glyphs in the two subtables, exactly as it does in a real font:
        // Unicode U+00E4 is 'ä', MacRoman 0xE4 is not.
        var table = new CmapTable(CmapMacFirst(codePoint: 0x00E4, macGlyph: 122, unicodeGlyph: 202));

        Assert.Equal(202, table.GetGlyphId(0x00E4));
    }

    [Fact]
    public void A_mac_only_font_still_resolves_through_its_mac_subtable()
    {
        // Preference must not become a requirement: when no Unicode subtable exists, the Mac one is
        // still the right answer rather than a miss.
        byte[] mac = Format0(new Dictionary<byte, byte> { [0x41] = 36 });
        const int headerSize = 4 + 8;

        var f = new List<byte>();
        U16(f, 0); U16(f, 1);
        U16(f, 1); U16(f, 0); U32(f, (uint)headerSize);
        f.AddRange(mac);

        var table = new CmapTable(f.ToArray());

        Assert.Equal(36, table.GetGlyphId(0x41));
    }

    [Fact]
    public void Ascii_is_unaffected_because_both_encodings_agree_below_0x80()
    {
        // The regression that hid this bug: ASCII matched either way, so only accented text broke.
        var table = new CmapTable(CmapMacFirst(codePoint: 0x0041, macGlyph: 36, unicodeGlyph: 36));

        Assert.Equal(36, table.GetGlyphId(0x0041));
    }
}
