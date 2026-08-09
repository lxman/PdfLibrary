using System.Collections.Generic;
using System.Linq;
using System.Text;
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

public class FontDescriptorMetricsTests
{
    private static byte[] RealFont(string family = "Arial")
    {
        FontMatch? match = SystemFontLocator.Default.Resolve(
            new FontRequest(family, Bold: false, Italic: false));
        Assert.SkipWhen(match is null, $"No {family} on this machine.");
        ClassifiedProgram? classified = FontProgramClassifier.Classify(match!.Data, match.FaceIndex);
        Assert.SkipWhen(classified is null, $"{family} did not classify.");
        return classified!.Program;
    }

    [Fact]
    public void Computes_a_plausible_descriptor_from_a_real_font()
    {
        byte[] program = RealFont();

        FontDescriptorValues? values = FontDescriptorMetrics.Compute(program, FontProgramFormat.TrueType);

        Assert.NotNull(values);
        Assert.Equal(4, values.FontBBox.Length);
        Assert.True(values.FontBBox[0] < values.FontBBox[2], "BBox llx must be left of urx");
        Assert.True(values.FontBBox[1] < values.FontBBox[3], "BBox lly must be below ury");
        Assert.True(values.Ascent > 0, "Ascent must be positive");
        Assert.True(values.Descent < 0, $"Descent must be negative, was {values.Descent}");
        Assert.True(values.CapHeight > 0, "CapHeight must be positive");
        Assert.InRange(values.StemV, 1, 400);
    }

    [Fact]
    public void The_values_are_in_1000_unit_glyph_space_not_raw_font_units()
    {
        // Arial's unitsPerEm is 2048. A reader that forgot to scale would report an Ascent near
        // 1854 rather than near 905, so a generous upper bound still catches the mistake.
        byte[] program = RealFont();

        FontDescriptorValues? values = FontDescriptorMetrics.Compute(program, FontProgramFormat.TrueType);

        Assert.NotNull(values);
        Assert.InRange(values.Ascent, 400, 1200);
        Assert.InRange(values.CapHeight, 400, 1100);
        Assert.InRange(values.FontBBox[3], 400, 1400);
    }

    [Fact]
    public void CapHeight_comes_from_OS2_when_the_table_provides_it()
    {
        // Not "is a number" — WHICH source answered. Arial ships a version-4 OS/2 with a real
        // sCapHeight, so a fallback here means the OS/2 read silently failed.
        byte[] program = RealFont();

        FontDescriptorValues? values = FontDescriptorMetrics.Compute(program, FontProgramFormat.TrueType);

        Assert.NotNull(values);
        Assert.InRange(values.CapHeight, 650, 750);   // Arial's cap height is ~716/1000
    }

    [Fact]
    public void StemV_reports_which_source_produced_it()
    {
        byte[] program = RealFont();

        FontDescriptorValues? values = FontDescriptorMetrics.Compute(program, FontProgramFormat.TrueType);

        Assert.NotNull(values);
        Assert.Contains(values.StemVSource, new[] { "cff-stdvw", "measured-I", "weight-class" });
    }

    [Fact]
    public void Garbage_bytes_yield_null_rather_than_a_descriptor_of_zeroes()
    {
        // A descriptor full of zeroes would be written into the file and would be worse than the
        // wrong-but-plausible one it replaced.
        Assert.Null(FontDescriptorMetrics.Compute("not a font"u8.ToArray(), FontProgramFormat.TrueType));
    }

    [Fact]
    public void StemV_source_is_measured_I_for_a_TrueType_font_not_the_weight_class_guess()
    {
        // Arial is TrueType, not CFF — there is no StdVW to read, so a real measurement of the 'I'
        // glyph's stem must answer. If this comes back "weight-class" the measured-I path silently
        // failed to find/measure the glyph.
        byte[] program = RealFont();

        FontDescriptorValues? values = FontDescriptorMetrics.Compute(program, FontProgramFormat.TrueType);

        Assert.NotNull(values);
        Assert.Equal("measured-I", values.StemVSource);
    }

    // --- Fix round 1 finding: cff-stdvw was never exercised -----------------------------------
    //
    // StemVSource exists to expose exactly the failure mode where a real source (StdVW) was
    // available but a parsing slip silently handed back the measured-I or weight-class value
    // instead. Neither Arial (TrueType, no CFF Private DICT at all) nor any prior test could ever
    // reach that branch. These two tests use a hand-built raw-CFF program (same technique as
    // WidthPrecedenceTests.cs's MinimalType1CFont, copied rather than shared per the fix-round
    // instruction not to touch that already-reviewed file) whose Private DICT does or does not
    // carry a StdVW entry.

    [Fact]
    public void StemV_reports_cff_stdvw_when_the_program_carries_a_real_one()
    {
        const int plantedStdVw = 85;
        byte[] program = MinimalCffWithPrivateDict.Build(plantedStdVw);

        FontDescriptorValues? values = FontDescriptorMetrics.Compute(program, FontProgramFormat.Type1C);

        Assert.NotNull(values);
        Assert.Equal("cff-stdvw", values.StemVSource);
        // This fixture carries no FontMatrix, so EmbeddedFontMetrics defaults UnitsPerEm to 1000 and
        // the 1000/UnitsPerEm scale factor is exactly 1 — the planted value should come back unchanged.
        Assert.Equal(plantedStdVw, values.StemV);
    }

    [Fact]
    public void StemV_falls_back_to_weight_class_when_the_program_has_neither_StdVW_nor_a_measurable_I()
    {
        // Same builder, StdVW omitted, and the fixture never defines a glyph named 'I' — so both
        // higher-priority sources are genuinely absent, not merely unread.
        byte[] program = MinimalCffWithPrivateDict.Build(stdVw: null);

        FontDescriptorValues? values = FontDescriptorMetrics.Compute(program, FontProgramFormat.Type1C);

        Assert.NotNull(values);
        Assert.Equal("weight-class", values.StemVSource);
        // No OS/2 table exists for a raw CFF program, so FontDescriptorMetrics uses its documented
        // default weight class of 400 (Regular): 50 + (400/100)^2 * 3 = 98.
        Assert.Equal(98, values.StemV);
    }
}

/// <summary>
/// Hand-builds a minimal, self-contained raw CFF (Type1C) font program carrying exactly one glyph
/// (.notdef) and a Private DICT that optionally includes a <c>StdVW</c> entry — built specifically to
/// exercise <c>FontDescriptorMetrics</c>'s <c>"cff-stdvw"</c> branch, which no other test in the suite
/// reaches.
///
/// <para>This is a deliberate copy of <c>WidthPrecedenceTests.MinimalType1CFont</c>'s layout
/// technique (header | Name INDEX | Top DICT INDEX | empty String INDEX | empty Global Subr INDEX |
/// Encoding format byte | charset | CharStrings INDEX | Private DICT, every Top DICT number in the
/// fixed 5-byte 0x1D form so offsets are knowable before the bytes are laid out) — not a shared
/// reference, per the fix-round instruction to leave that already-reviewed file untouched. Simplified
/// relative to the original: a single glyph (no named 'A'/'Z' — this fixture's whole point is that
/// <c>GetGlyphIdByName("I")</c> must return 0, so the measured-I fallback is genuinely unavailable,
/// not merely unread), so the charset is the empty format-0 table (GID 0 / .notdef is implicit and
/// carries no charset entry).
/// </para>
/// </summary>
internal static class MinimalCffWithPrivateDict
{
    private static readonly byte[] FontNameBytes = Encoding.ASCII.GetBytes("StdVwTestFont");

    private const byte OpCharset = 15;
    private const byte OpCharStrings = 17;
    private const byte OpPrivate = 18;
    private const byte OpStdVw = 11; // single-byte operator (CFF Private DICT operator 0x0B)
    private const byte OpNominalWidthX = 21; // single-byte operator (CFF Private DICT operator 0x15)

    /// <param name="stdVw">The Private DICT's StdVW value, or null to omit the entry entirely
    /// (so the parser reports it absent, not zero).</param>
    public static byte[] Build(int? stdVw)
    {
        byte[] charStrings0 = [0x0E]; // bare endchar — glyph 0/.notdef, never measured by these tests
        var charStrings = new List<byte[]> { charStrings0 };
        byte[] charsetTable = [0x00]; // format 0, zero SID entries: the only glyph is .notdef

        byte[] privateDict = BuildPrivateDict(stdVw);

        int nameIndexSize = IndexSize([FontNameBytes], 1);
        const int topDictLen = 6 + 6 + 11; // charset op + CharStrings op + Private (size+offset+op)
        int charStringsSize = IndexSize(charStrings, 1);

        // Layout: header | name | topDict | string | globalSubr | encoding | charset | charStrings | private
        int charsetOffset = 4 + nameIndexSize + IndexSize([new byte[topDictLen]], 1) + 2 + 2 + 1;
        int charStringsOffset = charsetOffset + charsetTable.Length;
        int privateOffset = charStringsOffset + charStringsSize;

        var top = new List<byte>();
        AppendNumberOp(top, charsetOffset, OpCharset);
        AppendNumberOp(top, charStringsOffset, OpCharStrings);
        AppendInt32(top, privateDict.Length);
        AppendInt32(top, privateOffset);
        top.Add(OpPrivate);
        Verify(top.Count == topDictLen, "Top DICT length differs from the value the offsets were built on");

        var data = new List<byte>();
        data.AddRange([0x01, 0x00, 0x04, 0x01]); // major, minor, hdrSize, offSize
        AppendIndex(data, [FontNameBytes], offSize: 1);
        AppendIndex(data, [top.ToArray()], offSize: 1);
        data.AddRange([0x00, 0x00]); // empty String INDEX
        data.AddRange([0x00, 0x00]); // empty Global Subr INDEX
        // The constructor reads an Encoding format byte wherever the Global Subr INDEX ends. 0xFF is
        // neither 0 nor 1, so no Encoding is parsed.
        data.Add(0xFF);
        Verify(charsetOffset == data.Count, "charset offset differs from where the charset bytes actually land");
        data.AddRange(charsetTable);
        Verify(charStringsOffset == data.Count, "CharStrings offset differs from where the INDEX actually lands");
        AppendIndex(data, charStrings, offSize: 1);
        Verify(privateOffset == data.Count, "Private DICT offset differs from where its bytes actually land");
        data.AddRange(privateDict);
        return data.ToArray();
    }

    /// <summary>nominalWidthX 0 always present; StdVW present only when <paramref name="stdVw"/> is
    /// non-null — an absent operator must read back as "no source", not as a zero value.</summary>
    private static byte[] BuildPrivateDict(int? stdVw)
    {
        byte[] nominalWidthX = [0x1D, 0, 0, 0, 0, OpNominalWidthX];
        if (stdVw is null)
            return nominalWidthX;

        int value = stdVw.Value;
        byte[] stdVwEntry =
        [
            0x1D, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value, OpStdVw
        ];
        return [.. nominalWidthX, .. stdVwEntry];
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
    /// parser result.</summary>
    private static void Verify(bool condition, string message)
    {
        if (!condition) throw new System.InvalidOperationException($"MinimalCffWithPrivateDict fixture is inconsistent: {message}");
    }
}
