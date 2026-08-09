using System;
using System.Collections.Generic;
using System.Linq;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

/// <summary>
/// Hand-built fixtures throughout, following PdfDocumentEditorFontsTests — no corpus file, so
/// these run identically on a dev machine and on a Linux CI runner.
/// </summary>
public class PdfDocumentEditorEmbedProgramTests
{
    private static byte[] ArialProgram()
    {
        FontMatch? match = SystemFontLocator.Default.Resolve(
            new FontRequest("Arial", Bold: false, Italic: false));
        Assert.SkipWhen(match is null, "No Arial on this machine.");
        ClassifiedProgram? classified = FontProgramClassifier.Classify(match!.Data, match.FaceIndex);
        Assert.SkipWhen(classified is null, "Arial did not classify.");
        return classified!.Program;
    }

    /// <summary>A simple TrueType font dict with a descriptor but no font file, and a /Widths array.</summary>
    private static (PdfDocument Document, FontId Font, PdfDictionary Dict, PdfDictionary Descriptor)
        UnembeddedWithWidths()
    {
        var document = new PdfDocument();

        var descriptor = new PdfDictionary();
        descriptor.Set("Type", new PdfName("FontDescriptor"));
        descriptor.Set("FontName", new PdfName("Arial"));
        descriptor.Set("Flags", new PdfInteger(32));
        descriptor.Set("StemV", new PdfInteger(80));
        PdfIndirectReference descriptorRef = document.RegisterObject(descriptor);

        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("TrueType"));
        font.Set("BaseFont", new PdfName("Arial"));
        font.Set("FirstChar", new PdfInteger(65));
        font.Set("LastChar", new PdfInteger(67));
        font.Set("Widths", new PdfArray(new PdfInteger(722), new PdfInteger(722), new PdfInteger(722)));
        font.Set("FontDescriptor", descriptorRef);
        PdfIndirectReference fontRef = document.RegisterObject(font);

        return (document, new FontId(fontRef.ObjectNumber), font, descriptor);
    }

    [Fact]
    public void Writes_the_program_to_FontFile2_with_Length1_for_TrueType()
    {
        (PdfDocument document, FontId id, _, PdfDictionary descriptor) = UnembeddedWithWidths();
        byte[] program = ArialProgram();
        using var editor = new PdfDocumentEditor(document);

        editor.EmbedProgram(id, program, FontProgramFormat.TrueType);

        var stream = Assert.IsType<PdfStream>(document.GetObject(
            ((PdfIndirectReference)descriptor.Get("FontFile2")!).ObjectNumber));
        Assert.Equal(program.Length, stream.Data.Length);
        Assert.Equal(program, stream.Data); // the payload itself, not merely its length, must survive
        Assert.Equal(program.Length, ((PdfInteger)stream.Dictionary.Get("Length1")!).Value);
        Assert.Null(descriptor.Get("FontFile"));
        Assert.Null(descriptor.Get("FontFile3"));
    }

    [Fact]
    public void Writes_the_program_to_FontFile3_with_the_exact_bytes_for_Type1C()
    {
        (PdfDocument document, FontId id, _, PdfDictionary descriptor) = UnembeddedWithWidths();
        byte[] program = [0x01, 0x00, 0x04, 0x02, 0x03, 0x10, 0x20, 0x30, 0x40, 0x50, 0x99, 0x01];
        using var editor = new PdfDocumentEditor(document);

        editor.EmbedProgram(id, program, FontProgramFormat.Type1C);

        var stream = Assert.IsType<PdfStream>(document.GetObject(
            ((PdfIndirectReference)descriptor.Get("FontFile3")!).ObjectNumber));
        Assert.Equal(program, stream.Data); // the /FontFile3 path builds a different stream dict;
        // confirm it doesn't diverge on the payload the way /FontFile2 could have.
        Assert.Equal("Type1C", ((PdfName)stream.Dictionary.Get("Subtype")!).Value);
    }

    [Fact]
    public void A_program_that_fails_to_parse_leaves_existing_descriptor_metrics_untouched()
    {
        // FontDescriptorMetrics.Compute returns null for bytes it cannot parse as a loadable font.
        // EmbedProgram must leave whatever metrics already sit on the descriptor rather than
        // overwrite them with zeroes -- a descriptor of zeroes would be worse than the
        // stale-but-plausible values it replaces.
        //
        // TrueType is the format used here deliberately: WriteProgramStream never parses or
        // validates the bytes for that format (it only measures Length1 = the byte count), so
        // garbage reaches FontDescriptorMetrics.Compute and returns null there instead of throwing
        // earlier in WriteProgramStream -- which is what a non-PFB Type1 program would do.
        (PdfDocument document, FontId id, _, PdfDictionary descriptor) = UnembeddedWithWidths();
        descriptor.Set("FontBBox", new PdfArray(
            new PdfInteger(-100), new PdfInteger(-200), new PdfInteger(900), new PdfInteger(800)));
        using var editor = new PdfDocumentEditor(document);
        byte[] garbage = [0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99];

        editor.EmbedProgram(id, garbage, FontProgramFormat.TrueType);

        Assert.Equal(80, ((PdfInteger)descriptor.Get("StemV")!).Value);
        int[] bbox = ((PdfArray)descriptor.Get("FontBBox")!).Select(n => ((PdfInteger)n).Value).ToArray();
        Assert.Equal([-100, -200, 900, 800], bbox);
    }

    [Fact]
    public void An_existing_Widths_array_is_left_byte_identical()
    {
        // Design §5.1. The /Widths array is what preserves the document's layout; the substitute's
        // own metrics are irrelevant to it. This is the single most important assertion in F-2.
        (PdfDocument document, FontId id, PdfDictionary font, _) = UnembeddedWithWidths();
        PdfObject? before = font.Get("Widths");
        int[] valuesBefore = ((PdfArray)before!).Select(n => ((PdfInteger)n).Value).ToArray();
        using var editor = new PdfDocumentEditor(document);

        editor.EmbedProgram(id, ArialProgram(), FontProgramFormat.TrueType);

        int[] valuesAfter = ((PdfArray)font.Get("Widths")!).Select(n => ((PdfInteger)n).Value).ToArray();
        Assert.Equal(valuesBefore, valuesAfter);
        Assert.Equal(65, ((PdfInteger)font.Get("FirstChar")!).Value);
        Assert.Equal(67, ((PdfInteger)font.Get("LastChar")!).Value);
    }

    [Fact]
    public void A_missing_Widths_array_is_written_from_the_embedded_program()
    {
        (PdfDocument document, FontId id, PdfDictionary font, _) = UnembeddedWithWidths();
        font.Remove(new PdfName("Widths"));
        font.Remove(new PdfName("FirstChar"));
        font.Remove(new PdfName("LastChar"));
        using var editor = new PdfDocumentEditor(document);

        editor.EmbedProgram(id, ArialProgram(), FontProgramFormat.TrueType);

        var widths = Assert.IsType<PdfArray>(font.Get("Widths"));
        Assert.NotEmpty(widths);
        int first = ((PdfInteger)font.Get("FirstChar")!).Value;
        int last = ((PdfInteger)font.Get("LastChar")!).Value;
        Assert.Equal(last - first + 1, widths.Count);
        Assert.All(widths, w => Assert.InRange(((PdfInteger)w).Value, 0, 2000));
    }

    [Fact]
    public void The_symbolic_Flags_bit_is_preserved_and_the_metrics_are_recomputed()
    {
        // Design §5.2's one carve-out: /Flags decides how the encoding is interpreted, so a fix
        // that flipped it would change which glyph a code selects as a side effect.
        (PdfDocument document, FontId id, _, PdfDictionary descriptor) = UnembeddedWithWidths();
        descriptor.Set("Flags", new PdfInteger(4));      // Symbolic
        using var editor = new PdfDocumentEditor(document);

        editor.EmbedProgram(id, ArialProgram(), FontProgramFormat.TrueType);

        Assert.Equal(4, ((PdfInteger)descriptor.Get("Flags")!).Value);
        Assert.NotEqual(80, ((PdfInteger)descriptor.Get("StemV")!).Value);  // was the placeholder
        Assert.Equal(4, ((PdfArray)descriptor.Get("FontBBox")!).Count);
        Assert.True(((PdfInteger)descriptor.Get("Descent")!).Value < 0);
    }

    [Fact]
    public void A_font_dictionary_with_no_descriptor_gets_one()
    {
        (PdfDocument document, FontId id, PdfDictionary font, _) = UnembeddedWithWidths();
        font.Remove(new PdfName("FontDescriptor"));
        using var editor = new PdfDocumentEditor(document);

        editor.EmbedProgram(id, ArialProgram(), FontProgramFormat.TrueType);

        PdfObject? descriptorObj = font.Get("FontDescriptor");
        Assert.NotNull(descriptorObj);
        var descriptor = (PdfDictionary)document.GetObject(
            ((PdfIndirectReference)descriptorObj!).ObjectNumber)!;
        Assert.Equal("FontDescriptor", ((PdfName)descriptor.Get("Type")!).Value);
        Assert.NotNull(descriptor.Get("FontFile2"));
    }

    [Fact]
    public void A_TrueType_program_in_a_Type1_dictionary_rewrites_the_dictionary_subtype()
    {
        // Final-review Critical: the stream key was chosen from the program's format alone, so a
        // /Subtype /Type1 dictionary (the commonest real case — /BaseFont /Helvetica) receiving a
        // system TrueType substitute got a /FontFile2 written into a Type1 descriptor. ISO 32000-2
        // §9.9.1 Table 124 permits /FontFile2 only "for a TrueType font dictionary or … a
        // CIDFontType2 CIDFont", so F-2 closed font-embedded by opening a different violation.
        (PdfDocument document, FontId id, PdfDictionary font, PdfDictionary descriptor) = UnembeddedWithWidths();
        font.Set("Subtype", new PdfName("Type1"));
        using var editor = new PdfDocumentEditor(document);

        editor.EmbedProgram(id, ArialProgram(), FontProgramFormat.TrueType);

        Assert.Equal("TrueType", ((PdfName)font.Get("Subtype")!).Value);
        Assert.NotNull(descriptor.Get("FontFile2"));
        Assert.Null(descriptor.Get("FontFile"));
        Assert.Null(descriptor.Get("FontFile3"));
        // The rewrite is legal precisely because nothing else has to move with it (Table 109/111).
        Assert.Equal("Arial", ((PdfName)font.Get("BaseFont")!).Value);
        Assert.Equal(65, ((PdfInteger)font.Get("FirstChar")!).Value);
    }

    [Fact]
    public void A_Type1C_program_leaves_a_Type1_dictionary_declaring_Type1()
    {
        // The other side of the reconciliation: Table 124 permits /FontFile3 /Type1C "for a Type1 or
        // MMType1 font dictionary", so this pair was already correct and must not be disturbed.
        (PdfDocument document, FontId id, PdfDictionary font, PdfDictionary descriptor) = UnembeddedWithWidths();
        font.Set("Subtype", new PdfName("Type1"));
        byte[] program = [0x01, 0x00, 0x04, 0x02, 0x03, 0x10, 0x20, 0x30, 0x40, 0x50, 0x99, 0x01];
        using var editor = new PdfDocumentEditor(document);

        editor.EmbedProgram(id, program, FontProgramFormat.Type1C);

        Assert.Equal("Type1", ((PdfName)font.Get("Subtype")!).Value);
        var stream = Assert.IsType<PdfStream>(document.GetObject(
            ((PdfIndirectReference)descriptor.Get("FontFile3")!).ObjectNumber));
        Assert.Equal("Type1C", ((PdfName)stream.Dictionary.Get("Subtype")!).Value);
    }

    [Fact]
    public void An_MMType1_dictionary_keeps_its_subtype_for_a_Type1C_program()
    {
        // Table 124 names Type1 and MMType1 equally for /Type1C, so the reconciliation must not
        // flatten a multiple-master dictionary into a plain Type1 one.
        (PdfDocument document, FontId id, PdfDictionary font, _) = UnembeddedWithWidths();
        font.Set("Subtype", new PdfName("MMType1"));
        using var editor = new PdfDocumentEditor(document);

        editor.EmbedProgram(id, [0x01, 0x00, 0x04, 0x02, 0x03, 0x10, 0x20], FontProgramFormat.Type1C);

        Assert.Equal("MMType1", ((PdfName)font.Get("Subtype")!).Value);
    }

    /// <summary>Builds a minimal, structurally valid OpenType/CFF ('OTTO') sfnt with a single 'CFF '
    /// table and no 'glyf' table, wrapping <see cref="CffTestFixtures.MinimalCff.Build"/>'s non-CID
    /// CFF bytes. <see cref="FontParser.SfntFont"/> does not validate the table directory checksum
    /// or searchRange/entrySelector/rangeShift, so those are left at zero.</summary>
    private static byte[] OpenTypeCffProgram()
    {
        byte[] cff = CffTestFixtures.MinimalCff.Build(charsetOperand: null, numGlyphs: 4);
        const int headerSize = 12;
        const int directoryEntrySize = 16;
        const int tableOffset = headerSize + directoryEntrySize; // exactly one table

        var data = new List<byte>();
        data.AddRange([0x4F, 0x54, 0x54, 0x4F]); // sfntVersion 'OTTO'
        AppendUInt16(data, 1); // numTables
        AppendUInt16(data, 0); // searchRange (unused by the parser)
        AppendUInt16(data, 0); // entrySelector (unused)
        AppendUInt16(data, 0); // rangeShift (unused)
        data.AddRange("CFF "u8.ToArray()); // tag
        AppendUInt32(data, 0); // checksum (not validated)
        AppendUInt32(data, tableOffset);
        AppendUInt32(data, (uint)cff.Length);
        data.AddRange(cff);
        return data.ToArray();
    }

    private static void AppendUInt16(List<byte> data, ushort value)
    {
        data.Add((byte)(value >> 8));
        data.Add((byte)value);
    }

    private static void AppendUInt32(List<byte> data, uint value)
    {
        data.Add((byte)(value >> 24));
        data.Add((byte)(value >> 16));
        data.Add((byte)(value >> 8));
        data.Add((byte)value);
    }

    [Fact]
    public void An_MMType1_dictionary_receiving_a_CFF_flavoured_OpenType_program_becomes_Type1()
    {
        // Re-review Critical: ResolveOpenType's CFF-without-CIDFont-operators arm used to preserve
        // /MMType1 via Type1Flavoured, but Table 124's OpenType row names only "a Type1 font
        // dictionary" for that case — /MMType1 appears in the /FontFile and /FontFile3 /Type1C rows,
        // never in the OpenType one. An /MMType1 dictionary that resolves a CFF-flavoured OpenType
        // substitute must therefore become /Type1, or the pair (/MMType1, /FontFile3 /OpenType)
        // — permitted nowhere in the table — gets written.
        (PdfDocument document, FontId id, PdfDictionary font, PdfDictionary descriptor) = UnembeddedWithWidths();
        font.Set("Subtype", new PdfName("MMType1"));
        using var editor = new PdfDocumentEditor(document);

        editor.EmbedProgram(id, OpenTypeCffProgram(), FontProgramFormat.OpenType);

        Assert.Equal("Type1", ((PdfName)font.Get("Subtype")!).Value);
        var stream = Assert.IsType<PdfStream>(document.GetObject(
            ((PdfIndirectReference)descriptor.Get("FontFile3")!).ObjectNumber));
        Assert.Equal("OpenType", ((PdfName)stream.Dictionary.Get("Subtype")!).Value);
    }

    [Fact]
    public void A_CID_keyed_program_is_refused_for_a_simple_font_and_writes_nothing()
    {
        // Table 124: /CIDFontType0C "may appear in the font descriptor for a CIDFontType0 CIDFont
        // dictionary" — and nowhere else. There is no permitted pair for a simple font, so the only
        // honest outcome is a refusal; the planner declines this before it can ever get here.
        (PdfDocument document, FontId id, PdfDictionary font, PdfDictionary descriptor) = UnembeddedWithWidths();
        font.Set("Subtype", new PdfName("Type1"));
        using var editor = new PdfDocumentEditor(document);

        NotSupportedException ex = Assert.Throws<NotSupportedException>(() =>
            editor.EmbedProgram(id, [0x01, 0x00, 0x04, 0x02, 0x03], FontProgramFormat.CidFontType0C));

        Assert.Contains("Table 124", ex.Message, StringComparison.Ordinal);
        // Refused BEFORE anything was written: a half-embedded font would be worse than none.
        Assert.Null(descriptor.Get("FontFile"));
        Assert.Null(descriptor.Get("FontFile2"));
        Assert.Null(descriptor.Get("FontFile3"));
        Assert.Equal("Type1", ((PdfName)font.Get("Subtype")!).Value);
    }

    [Fact]
    public void Derived_widths_are_measured_through_the_fonts_own_Encoding()
    {
        // Final-review Critical: the derived-/Widths branch fed the raw character code to a UNICODE
        // lookup, which is wrong for every code WinAnsiEncoding remaps — 0x92 is `quoteright`
        // (U+2019), not U+0092. §5.1 makes the derived case the one where written widths genuinely
        // position text, so the old code gave a curly quote the width of an unmapped control
        // character (i.e. /MissingWidth) and moved every line containing one.
        (PdfDocument document, FontId id, PdfDictionary font, _) = UnembeddedWithWidths();
        font.Set("Subtype", new PdfName("TrueType"));
        font.Set("Encoding", new PdfName("WinAnsiEncoding"));
        font.Remove(new PdfName("Widths"));
        font.Remove(new PdfName("FirstChar"));
        font.Remove(new PdfName("LastChar"));

        byte[] program = ArialProgram();
        var metrics = new EmbeddedFontMetrics(program);
        double scale = 1000.0 / metrics.UnitsPerEm;
        ushort quoterightAdvance = metrics.GetUnicodeAdvanceWidth(0x2019);
        // The fixture's own premises, asserted rather than assumed: the substitute really does have
        // a quoteright glyph, and the two readings of code 0x92 — WinAnsi's U+2019 and the old
        // code's raw-code-as-Unicode U+0092 — provably DISAGREE on the advance, so this test can
        // discriminate them. (They disagree by a lot: the raw reading lands on a full-width glyph.)
        Assert.True(quoterightAdvance > 0, "The resolved substitute has no glyph for U+2019.");
        Assert.NotEqual(quoterightAdvance, metrics.GetUnicodeAdvanceWidth(0x92));

        using var editor = new PdfDocumentEditor(document);
        editor.EmbedProgram(id, program, FontProgramFormat.TrueType);

        var widths = Assert.IsType<PdfArray>(font.Get("Widths"));
        int first = ((PdfInteger)font.Get("FirstChar")!).Value;
        int last = ((PdfInteger)font.Get("LastChar")!).Value;
        Assert.InRange(0x92, first, last);

        var expected = (int)Math.Round(quoterightAdvance * scale);
        Assert.Equal(expected, ((PdfInteger)widths[0x92 - first]).Value);

        // And a code whose WinAnsi glyph IS its Latin-1 reading still lands on the same advance.
        Assert.Equal(
            (int)Math.Round(metrics.GetUnicodeAdvanceWidth('A') * scale),
            ((PdfInteger)widths['A' - first]).Value);
    }

    [Fact]
    public void An_unknown_font_id_throws_rather_than_silently_doing_nothing()
    {
        (PdfDocument document, _, _, _) = UnembeddedWithWidths();
        using var editor = new PdfDocumentEditor(document);

        Assert.Throws<ArgumentException>(() =>
            editor.EmbedProgram(new FontId(9999), ArialProgram(), FontProgramFormat.TrueType));
    }
}
