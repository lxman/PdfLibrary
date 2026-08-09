using System.Linq;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Fonts;
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
        Assert.Equal(program.Length, ((PdfInteger)stream.Dictionary.Get("Length1")!).Value);
        Assert.Null(descriptor.Get("FontFile"));
        Assert.Null(descriptor.Get("FontFile3"));
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
    public void An_unknown_font_id_throws_rather_than_silently_doing_nothing()
    {
        (PdfDocument document, _, _, _) = UnembeddedWithWidths();
        using var editor = new PdfDocumentEditor(document);

        Assert.Throws<ArgumentException>(() =>
            editor.EmbedProgram(new FontId(9999), ArialProgram(), FontProgramFormat.TrueType));
    }
}
