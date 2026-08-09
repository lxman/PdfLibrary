using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// Pins ISO 32000-2 §9.2.4: the /Widths array is what positions glyphs in a simple font, and the
/// font program's own advances are a fallback for when the dictionary cannot answer.
///
/// <para>NOTE 3 of that clause is the reason this matters — TrueType stores advances in units of
/// 1024 or 2048 per em against /Widths' 1000, so the two are EXPECTED to differ slightly. An engine
/// that prefers the program therefore shifts text by a sub-point amount on every glyph the moment a
/// program is embedded, which is exactly what font remediation must never do.</para>
/// </summary>
public class WidthPrecedenceTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfArray Rect(int x0, int y0, int x1, int y1) =>
        new(new PdfInteger(x0), new PdfInteger(y0), new PdfInteger(x1), new PdfInteger(y1));

    [Fact]
    public void A_usable_Widths_entry_wins_over_the_embedded_program()
    {
        // Fixture: a TrueType font dict whose /Widths says 500 for 'A' while the embedded
        // program's hmtx says something else. /Widths must win.
        using PdfDocument document = WidthFixtures.TrueTypeWithWidthsAndProgram(
            code: 'A', declaredWidth: 500);

        double width = WidthFixtures.WidthOf(document, 'A');

        Assert.Equal(500, width, precision: 3);
    }

    [Fact]
    public void A_zero_Widths_entry_falls_through_to_the_program()
    {
        // The degenerate case the original program-first ordering was written for: a /Widths array
        // present but carrying no usable value. Zero is a legal advance, so this is a deliberate
        // concession to broken files rather than a claim that zero is invalid.
        using PdfDocument document = WidthFixtures.TrueTypeWithWidthsAndProgram(
            code: 'A', declaredWidth: 0);

        double width = WidthFixtures.WidthOf(document, 'A');

        Assert.Equal(WidthFixtures.ProgramAdvanceFor('A'), width, precision: 3);
    }

    [Fact]
    public void A_code_outside_FirstChar_LastChar_falls_through_to_the_program()
    {
        using PdfDocument document = WidthFixtures.TrueTypeWithWidthsAndProgram(
            code: 'A', declaredWidth: 500);

        double width = WidthFixtures.WidthOf(document, 'Z');   // outside the declared range

        Assert.Equal(WidthFixtures.ProgramAdvanceFor('Z'), width, precision: 3);
    }

    // --- Type1 counterparts (fix round 1 finding) ---------------------------------------------
    //
    // Type1Font.GetCharacterWidth is the harder branch: it maps charCode -> /Encoding glyph name
    // -> embedded-program advance, with a separate CFF sub-path (Type1CffFixtures.Build), rather
    // than TrueTypeFont's direct charCode -> hmtx lookup. Without a Type1 fixture, no test in the
    // suite could distinguish "program wins" from "/Widths wins" for Type1Font specifically — the
    // three tests above only exercise TrueTypeFont.

    [Fact]
    public void A_usable_Widths_entry_wins_over_the_embedded_program_Type1()
    {
        using PdfDocument document = Type1CffFixtures.Type1WithWidthsAndProgram(
            code: 'A', declaredWidth: 500);

        double width = Type1CffFixtures.WidthOf(document, 'A');

        Assert.Equal(500, width, precision: 3);
    }

    [Fact]
    public void A_zero_Widths_entry_falls_through_to_the_program_Type1()
    {
        using PdfDocument document = Type1CffFixtures.Type1WithWidthsAndProgram(
            code: 'A', declaredWidth: 0);

        double width = Type1CffFixtures.WidthOf(document, 'A');

        Assert.Equal(Type1CffFixtures.ProgramAdvanceFor('A'), width, precision: 3);
    }

    [Fact]
    public void A_code_outside_FirstChar_LastChar_falls_through_to_the_program_Type1()
    {
        using PdfDocument document = Type1CffFixtures.Type1WithWidthsAndProgram(
            code: 'A', declaredWidth: 500);

        double width = Type1CffFixtures.WidthOf(document, 'Z');   // outside the declared range

        Assert.Equal(Type1CffFixtures.ProgramAdvanceFor('Z'), width, precision: 3);
    }
}

/// <summary>
/// Hand-built font-dictionary fixtures for <see cref="WidthPrecedenceTests"/>, following the
/// established pattern in <c>PdfLibrary.Tests/Editing/PdfDocumentEditorFontsTests.cs</c>: documents
/// built directly with <c>AddObject</c>, no corpus file (<c>TestPDFs/</c> is gitignored and absent on
/// CI).
///
/// <para>The embedded program is the real system-substitute font bundled with this test project
/// (<c>Resources/PublicPixel.ttf</c>, already used by <c>SystemFontLocatorTests</c> and others) rather
/// than a synthetic sfnt — the brief explicitly permits this route. Its glyph advances for 'A' and 'Z'
/// are read directly off the font's own hmtx table via <see cref="EmbeddedFontMetrics"/>, so the
/// fixture proves precedence with the program's ACTUAL advance rather than an asserted number.</para>
/// </summary>
internal static class WidthFixtures
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfArray Rect(int x0, int y0, int x1, int y1) =>
        new(new PdfInteger(x0), new PdfInteger(y0), new PdfInteger(x1), new PdfInteger(y1));

    private static byte[]? _fontBytes;
    private static byte[] FontBytes => _fontBytes ??=
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

    /// <summary>The embedded program's own advance for <paramref name="code"/>, scaled to the PDF
    /// 1000-unit space exactly as <c>TrueTypeFont.GetCharacterWidth</c> does. This is what a test
    /// asserts against when the program is expected to win — never a made-up number.</summary>
    internal static double ProgramAdvanceFor(int code)
    {
        var metrics = new EmbeddedFontMetrics(FontBytes);
        Assert.True(metrics.IsValid);
        ushort raw = metrics.GetCharacterAdvanceWidth((ushort)code);
        Assert.True(raw > 0, $"Fixture font has no usable advance for code {code}.");
        return raw * 1000.0 / metrics.UnitsPerEm;
    }

    /// <summary>
    /// A single simple TrueType font dict (object 10) declaring <paramref name="declaredWidth"/> for
    /// <paramref name="code"/>, over an embedded /FontFile2 (object 12, the real PublicPixel.ttf
    /// bytes) whose hmtx gives that code a genuinely different advance. The setup asserts the two
    /// values differ so the precedence tests cannot pass by coincidence.
    /// </summary>
    internal static PdfDocument TrueTypeWithWidthsAndProgram(int code, int declaredWidth)
    {
        double programAdvance = ProgramAdvanceFor(code);
        Assert.NotEqual(programAdvance, declaredWidth, 3);

        var doc = new PdfDocument();
        doc.AddObject(12, 0, new PdfStream(
            new PdfDictionary { [N("Length1")] = new PdfInteger(FontBytes.Length) }, FontBytes));
        doc.AddObject(11, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("PublicPixelTest"),
            [N("Flags")] = new PdfInteger(32),
            [N("FontFile2")] = Ref(12),
        });
        // FirstChar/LastChar span exactly the fixture's declared code, so a lookup for anything
        // else (e.g. 'Z' when the fixture was built for 'A') is genuinely out of range.
        doc.AddObject(10, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("PublicPixelTest"),
            [N("Encoding")] = N("WinAnsiEncoding"),
            [N("FirstChar")] = new PdfInteger(code),
            [N("LastChar")] = new PdfInteger(code),
            [N("Widths")] = new PdfArray(new PdfInteger(declaredWidth)),
            [N("FontDescriptor")] = Ref(11),
        });
        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf <41> Tj ET")));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(4),
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = Ref(10) } },
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    /// <summary>Resolves the font dict at object 10 to a <see cref="PdfFont"/> and asks it for the
    /// width of <paramref name="code"/> — the actual production code path under test.</summary>
    internal static double WidthOf(PdfDocument document, int code)
    {
        var dictionary = (PdfDictionary)document.Objects[10];
        PdfFont? font = PdfFont.Create(dictionary, document);
        Assert.NotNull(font);
        return font!.GetCharacterWidth(code);
    }
}

/// <summary>
/// Type1 counterparts of <see cref="WidthFixtures"/> (fix round 1 finding: the original
/// <c>WidthPrecedenceTests</c> only built <c>Subtype /TrueType</c> fixtures, so nothing in the suite
/// could tell "program wins" from "/Widths wins" for <c>Type1Font</c> — the harder branch, which maps
/// a character code through <c>/Encoding</c> to a glyph NAME and looks the advance up by name, with a
/// separate CFF sub-path).
///
/// <para><b>Why a hand-built CFF instead of a committed asset:</b> <c>PublicPixel.ttf</c> (used by
/// <see cref="WidthFixtures"/>) cannot back a Type1/CFF dictionary — traced through
/// <c>EmbeddedFontMetrics.GetGlyphIdByName</c>, name-based glyph lookup only works when
/// <c>IsType1Font</c> or <c>IsCffFont</c> is set, and a plain TrueType sfnt (PublicPixel's own
/// sfntVersion is <c>00 01 00 00</c>, confirmed by reading its header) sets neither — the lookup
/// always returns 0 and the "program wins" case could never be exercised. A repo-wide search
/// (<c>git ls-files | grep -iE '\.(cff|pfb|pfa|otf|t1)$'</c>) found no committed CFF/Type1/OTF asset
/// anywhere in this repository, so there was nothing existing to reuse. Rather than add a new binary
/// asset, this follows the repo's own established precedent for synthetic font-program fixtures:
/// <c>CffTestFixtures.MinimalCff</c> (<c>FontParser.Tests/Cff/MinimalCff.cs</c>, shared into this
/// project — see <c>PdfLibrary.Tests.csproj</c>'s Compile/Link item) already hand-builds minimal raw
/// CFF programs for the CFF-parser tests. That builder's CharStrings are uniformly bare
/// <c>endchar</c> with no glyph-specific width, which is exactly what a "does the program's OWN
/// advance win" test needs, so this is a parallel, self-contained builder (not an edit to the shared
/// file) that adds per-glyph Type2-charstring-encoded widths on top of the same layout technique.
/// </para>
/// </summary>
internal static class Type1CffFixtures
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfArray Rect(int x0, int y0, int x1, int y1) =>
        new(new PdfInteger(x0), new PdfInteger(y0), new PdfInteger(x1), new PdfInteger(y1));

    // CFF Technical Note #5176 Appendix A standard strings: SID 34 = "A", SID 59 = "Z". Distinct raw
    // widths (font units == PDF units here, since this fixture's CFF has no FontMatrix and therefore
    // defaults UnitsPerEm to 1000) so the two glyphs can never coincidentally agree with each other
    // or with a declared /Widths value.
    private const int WidthA = 600;
    private const int WidthZ = 300;

    private static byte[]? _cffBytes;
    private static byte[] CffBytes => _cffBytes ??= MinimalType1CFont.Build(WidthA, WidthZ);

    /// <summary>The embedded CFF program's own advance for 'A' (SID 34) or 'Z' (SID 59), via the
    /// exact lookup <c>Type1Font.GetCharacterWidth</c> uses for a non-Type1-format CFF program:
    /// glyph name -> <see cref="EmbeddedFontMetrics.GetGlyphIdByName"/> -> <see
    /// cref="EmbeddedFontMetrics.GetAdvanceWidth(ushort)"/>, scaled to the PDF 1000-unit space. Never
    /// a made-up literal, even though this fixture's widths are ones we chose ourselves — the point
    /// is exercising the same lookup path production code takes, not the specific numbers.</summary>
    internal static double ProgramAdvanceFor(int code)
    {
        var metrics = new EmbeddedFontMetrics(CffBytes);
        Assert.True(metrics.IsValid);
        Assert.True(metrics.IsCffFont);
        string glyphName = code switch { 'A' => "A", 'Z' => "Z", _ => throw new ArgumentOutOfRangeException(nameof(code)) };
        ushort glyphId = metrics.GetGlyphIdByName(glyphName);
        Assert.True(glyphId > 0, $"Fixture CFF has no glyph named '{glyphName}'.");
        ushort raw = metrics.GetAdvanceWidth(glyphId);
        Assert.True(raw > 0, $"Fixture CFF glyph '{glyphName}' has no usable advance.");
        return raw * 1000.0 / metrics.UnitsPerEm;
    }

    /// <summary>
    /// A single simple Type1 font dict (object 10) declaring <paramref name="declaredWidth"/> for
    /// <paramref name="code"/>, over an embedded /FontFile3 (object 12, a hand-built CFF program —
    /// see <see cref="MinimalType1CFont"/>) whose 'A' and 'Z' glyphs carry genuinely different, real
    /// advances (600 and 300 respectively). The setup asserts the program advance and the declared
    /// width differ so the precedence tests cannot pass by coincidence.
    /// </summary>
    internal static PdfDocument Type1WithWidthsAndProgram(int code, int declaredWidth)
    {
        double programAdvance = ProgramAdvanceFor(code);
        Assert.NotEqual(programAdvance, declaredWidth, 3);

        var doc = new PdfDocument();
        doc.AddObject(12, 0, new PdfStream(new PdfDictionary(), CffBytes));
        doc.AddObject(11, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("Type1CffTest"),
            [N("Flags")] = new PdfInteger(32),
            [N("FontFile3")] = Ref(12),
        });
        // FirstChar/LastChar span exactly the fixture's declared code, so a lookup for anything else
        // (e.g. 'Z' when the fixture was built for 'A') is genuinely out of range.
        doc.AddObject(10, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type1"),
            [N("BaseFont")] = N("Type1CffTest"),
            [N("Encoding")] = N("WinAnsiEncoding"),
            [N("FirstChar")] = new PdfInteger(code),
            [N("LastChar")] = new PdfInteger(code),
            [N("Widths")] = new PdfArray(new PdfInteger(declaredWidth)),
            [N("FontDescriptor")] = Ref(11),
        });
        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf (A) Tj ET")));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(4),
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = Ref(10) } },
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    /// <summary>Resolves the font dict at object 10 to a <see cref="PdfFont"/> and asks it for the
    /// width of <paramref name="code"/> — the actual production code path under test.</summary>
    internal static double WidthOf(PdfDocument document, int code)
    {
        var dictionary = (PdfDictionary)document.Objects[10];
        PdfFont? font = PdfFont.Create(dictionary, document);
        Assert.NotNull(font);
        return font!.GetCharacterWidth(code);
    }
}

/// <summary>
/// Hand-builds a minimal, self-contained raw CFF (Type1C) font program with two real named glyphs
/// ('A' = SID 34, 'Z' = SID 59 — CFF Technical Note #5176 Appendix A standard strings) carrying
/// distinct, explicit advance widths.
///
/// <para>Modeled directly on <c>CffTestFixtures.MinimalCff</c>'s layout technique (header | Name
/// INDEX | Top DICT INDEX | empty String INDEX | empty Global Subr INDEX | Encoding format byte |
/// charset | CharStrings INDEX | Private DICT, with every Top DICT number in the fixed 5-byte 0x1D
/// form so offsets are knowable before the bytes are laid out) — see
/// <c>FontParser.Tests/Cff/MinimalCff.cs</c>. That shared fixture's CharStrings are uniformly bare
/// <c>endchar</c> (no glyph carries an explicit width), so it cannot produce a program whose advance
/// differs from anything; this builder adds a width to each glyph's CharString via the Type2
/// charstring convention (CFF spec / Adobe TN #5177 §3.1, "Type 2 Charstring Format"): nominalWidthX
/// is set to 0 in the Private DICT, and each CharString pushes one number (the width, encoded via
/// operator 0x1C / 2-byte signed int16) before <c>endchar</c> (0x0E) — a stack count of 1 at a
/// stack-clearing operator that normally takes 0 args is the spec's signal that the leading number is
/// a width, not a real operand.</para>
/// </summary>
internal static class MinimalType1CFont
{
    private static readonly byte[] FontNameBytes = Encoding.ASCII.GetBytes("TestFont");

    private const byte OpCharset = 15;
    private const byte OpCharStrings = 17;
    private const byte OpPrivate = 18;
    private const byte T2Int16 = 0x1C; // Type2 charstring: next 2 bytes = big-endian signed int16 operand

    /// <summary>nominalWidthX 0 (op 21 / 0x15), so an encoded charstring number IS the glyph's raw
    /// width — identical bytes to <c>CffTestFixtures.MinimalCff.PrivateDict</c>.</summary>
    private static byte[] PrivateDict => [0x1D, 0, 0, 0, 0, 0x15];

    public static byte[] Build(int widthA, int widthZ)
    {
        var charStrings = new List<byte[]> { Endchar(), Charstring(widthA), Charstring(widthZ) };
        byte[] charsetTable = BuildCharsetTable([34, 59]); // SID 'A', SID 'Z' — GID 1, GID 2

        int nameIndexSize = IndexSize([FontNameBytes], 1);
        const int topDictLen = 6 + 6 + 11; // charset op + CharStrings op + Private (size+offset+op)
        int charStringsSize = IndexSize(charStrings, 2);

        // Layout: header | name | topDict | string | globalSubr | encoding | charset | charStrings | private
        int charsetOffset = 4 + nameIndexSize + IndexSize([new byte[topDictLen]], 1) + 2 + 2 + 1;
        int charStringsOffset = charsetOffset + charsetTable.Length;
        int privateOffset = charStringsOffset + charStringsSize;

        var top = new List<byte>();
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
        data.AddRange([0x00, 0x00]); // empty String INDEX
        data.AddRange([0x00, 0x00]); // empty Global Subr INDEX
        // The constructor reads an Encoding format byte at whatever position follows the Global Subr
        // INDEX. 0xFF is neither 0 nor 1, so no Encoding is parsed (matches MinimalCff).
        data.Add(0xFF);
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

    /// <summary>Bare <c>endchar</c>, no leading number — glyph 0/.notdef, whose width is never asked
    /// for by these tests.</summary>
    private static byte[] Endchar() => [0x0E];

    private static byte[] BuildCharsetTable(ushort[] sids)
    {
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
        if (!condition) throw new InvalidOperationException($"MinimalType1CFont fixture is inconsistent: {message}");
    }
}
