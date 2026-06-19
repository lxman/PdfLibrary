using System.Linq;
using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Optimization;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Optimization;

/// <summary>
/// Round-trip integration tests for font subsetting via <see cref="PdfOptimizer"/>.
///
/// Fixture strategy: synthesise a PDF in-memory using PdfDocumentBuilder + a system
/// TrueType font (Arial).  No checked-in PDF fixtures are required.
///
/// All tests skip silently when Arial is unavailable (CI without Windows fonts).
/// </summary>
public class FontSubsetIntegrationTests
{
    private const string ArialPath = @"C:\Windows\Fonts\arial.ttf";

    private const string SampleText =
        "The quick brown fox jumps over the lazy dog.";

    // ─────────────────────────────────────────────────────────────────────────
    // Helper: build a PDF with Arial embedded and some text
    // ─────────────────────────────────────────────────────────────────────────

    private static byte[] BuildArialPdf(string text)
    {
        const string alias = "Arial";
        return PdfDocumentBuilder.Create()
            .LoadFont(ArialPath, alias)
            .AddPage(p => p.AddText(text, 50, 700, alias, 12))
            .ToByteArray();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 1: Output re-parses as a valid document
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SubsetFonts_OutputParses_AsValidDocument()
    {
        if (!File.Exists(ArialPath)) return;

        byte[] src = BuildArialPdf(SampleText);
        using var output = new MemoryStream();

        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(src)))
            PdfOptimizer.Optimize(doc, output, new PdfOptimizationOptions { SubsetFonts = true });

        output.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(output);

        Assert.Equal(1, reloaded.PageCount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 2: Extracted text is byte-identical after subsetting (the load-bearing proof)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SubsetFonts_ExtractedText_IsIdenticalToBaseline()
    {
        if (!File.Exists(ArialPath)) return;

        byte[] src = BuildArialPdf(SampleText);

        // Baseline: text from the original document
        string baselineText;
        using (PdfDocument baseline = PdfDocument.Load(new MemoryStream(src)))
            baselineText = baseline.ExtractAllText().Trim();

        // Subset and reload
        using var output = new MemoryStream();
        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(src)))
            PdfOptimizer.Optimize(doc, output, new PdfOptimizationOptions { SubsetFonts = true });

        output.Position = 0;
        string afterText;
        using (PdfDocument after = PdfDocument.Load(output))
            afterText = after.ExtractAllText().Trim();

        Assert.False(string.IsNullOrEmpty(baselineText), "Baseline text must not be empty");
        Assert.Equal(baselineText, afterText);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 3: /FontFile2 stream is smaller after subsetting
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SubsetFonts_FontFile2_IsSmallerAfterSubsetting()
    {
        if (!File.Exists(ArialPath)) return;

        byte[] src = BuildArialPdf(SampleText);

        int origFontBytes;
        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(src)))
        {
            doc.MaterializeAllObjects();
            origFontBytes = GetFontFile2EncodedSize(doc);
        }

        using var output = new MemoryStream();
        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(src)))
            PdfOptimizer.Optimize(doc, output, new PdfOptimizationOptions { SubsetFonts = true });

        output.Position = 0;
        int subsetFontBytes;
        using (PdfDocument after = PdfDocument.Load(output))
        {
            after.MaterializeAllObjects();
            subsetFontBytes = GetFontFile2EncodedSize(after);
        }

        Assert.True(origFontBytes > 0, "Original /FontFile2 must exist");
        Assert.True(subsetFontBytes > 0, "Subset /FontFile2 must exist");
        Assert.True(subsetFontBytes < origFontBytes,
            $"Subset font ({subsetFontBytes} B) should be smaller than original ({origFontBytes} B)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 4: Overall output file is smaller or equal (never bloats)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SubsetFonts_OutputSize_IsNotLargerThanBaseline()
    {
        if (!File.Exists(ArialPath)) return;

        byte[] src = BuildArialPdf(SampleText);

        // Baseline: document saved without any subsetting
        using var plain = new MemoryStream();
        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(src)))
            doc.Save(plain);

        // Subset output
        using var opt = new MemoryStream();
        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(src)))
            PdfOptimizer.Optimize(doc, opt, new PdfOptimizationOptions { SubsetFonts = true });

        Assert.True(opt.Length <= plain.Length,
            $"Optimized ({opt.Length} B) must not be larger than plain ({plain.Length} B)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 5: Control — default options do NOT change the /FontFile2 size
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultOptions_DoNotSubsetFonts()
    {
        if (!File.Exists(ArialPath)) return;

        byte[] src = BuildArialPdf(SampleText);

        int origFontBytes;
        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(src)))
        {
            doc.MaterializeAllObjects();
            origFontBytes = GetFontFile2EncodedSize(doc);
        }

        // Optimize with default options (SubsetFonts = false)
        using var output = new MemoryStream();
        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(src)))
            PdfOptimizer.Optimize(doc, output);

        output.Position = 0;
        int afterFontBytes;
        using (PdfDocument after = PdfDocument.Load(output))
        {
            after.MaterializeAllObjects();
            afterFontBytes = GetFontFile2EncodedSize(after);
        }

        Assert.True(origFontBytes > 0, "Original font must be present");
        Assert.True(afterFontBytes > 0, "After-optimize font must be present");

        // The key invariant: SubsetFonts=false must NOT reduce the glyph count.
        // Re-parse both the reference Arial and the /FontFile2 from the optimized document
        // and assert NumGlyphs is unchanged.
        // Note: capture output bytes before the stream is disposed by the using-block above.
        byte[] outputBytes = output.ToArray();
        var origSfnt = new FontParser.SfntFont(File.ReadAllBytes(ArialPath));
        using PdfDocument afterDoc = PdfDocument.Load(new MemoryStream(outputBytes));
        afterDoc.MaterializeAllObjects();
        byte[]? afterFontFile2 = GetFontFile2Bytes(afterDoc);
        Assert.NotNull(afterFontFile2);
        var afterSfnt = new FontParser.SfntFont(afterFontFile2!);
        Assert.Equal(origSfnt.NumGlyphs, afterSfnt.NumGlyphs);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 6: Subset glyphs can be re-parsed by SfntFont (font is valid sfnt)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SubsetFonts_ResultFont_IsValidSfnt()
    {
        if (!File.Exists(ArialPath)) return;

        byte[] src = BuildArialPdf(SampleText);

        using var output = new MemoryStream();
        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(src)))
            PdfOptimizer.Optimize(doc, output, new PdfOptimizationOptions { SubsetFonts = true });

        output.Position = 0;
        using PdfDocument after = PdfDocument.Load(output);
        after.MaterializeAllObjects();

        // Extract the /FontFile2 raw bytes and parse as sfnt
        byte[]? fontBytes = GetFontFile2Bytes(after);
        Assert.NotNull(fontBytes);
        Assert.True(fontBytes!.Length > 0);

        // Must parse without throwing
        var sfnt = new FontParser.SfntFont(fontBytes);
        Assert.Equal(FontParser.SfntOutlineKind.TrueType, sfnt.OutlineKind);
        // Subset must have fewer glyphs than the full Arial
        Assert.True(sfnt.NumGlyphs < new FontParser.SfntFont(File.ReadAllBytes(ArialPath)).NumGlyphs,
            $"Subset NumGlyphs ({sfnt.NumGlyphs}) should be < original Arial NumGlyphs");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static int GetFontFile2EncodedSize(PdfDocument doc)
    {
        foreach (PdfPage page in doc.Pages)
        {
            PdfResources? resources = page.GetResources();
            if (resources is null) continue;
            foreach (string fontName in resources.GetFontNames())
            {
                PdfFont? font = resources.GetFontObject(fontName);
                PdfFontDescriptor? desc = GetDescriptor(font);
                PdfStream? stream = desc?.GetFontFile2Stream();
                if (stream is not null)
                    return stream.Length;
            }
        }
        return -1;
    }

    private static byte[]? GetFontFile2Bytes(PdfDocument doc)
    {
        foreach (PdfPage page in doc.Pages)
        {
            PdfResources? resources = page.GetResources();
            if (resources is null) continue;
            foreach (string fontName in resources.GetFontNames())
            {
                PdfFont? font = resources.GetFontObject(fontName);
                PdfFontDescriptor? desc = GetDescriptor(font);
                if (desc is null) continue;
                // Get decoded bytes
                byte[]? bytes = desc.GetFontFile2();
                if (bytes is not null)
                    return bytes;
            }
        }
        return null;
    }

    private static PdfFontDescriptor? GetDescriptor(PdfFont? font)
    {
        return font switch
        {
            TrueTypeFont tt => tt.Descriptor,
            Type0Font t0 => t0.DescendantDescriptor,
            _ => null
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CFF (/FontFile3 Type1C) subsetting — allmand spec sheet (body in a Form XObject, AbadiMT)
    // ─────────────────────────────────────────────────────────────────────────

    private static string AllmandPath() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..",
        "ImageLibrary", "TestImages", "jpeg_test", "allmand-backhoe-loaders-spec-e15132.pdf"));

    [Fact]
    public void SubsetFonts_AllmandType1C_PreservesText_AndShrinksFontFile3()
    {
        string path = AllmandPath();
        if (!File.Exists(path)) return; // untracked fixture; guarded

        string before;
        long fontBytesBefore;
        using (PdfDocument d = PdfDocument.Load(path))
        {
            before = d.ExtractAllText();
            fontBytesBefore = TotalType1CBytes(d);
        }
        Assert.Contains("alphabetical notation", before); // body text (Form XObject) must be present

        using var ms = new MemoryStream();
        using (PdfDocument d = PdfDocument.Load(path))
            PdfOptimizer.Optimize(d, ms, new PdfOptimizationOptions { SubsetFonts = true });
        ms.Position = 0;

        using PdfDocument re = PdfDocument.Load(ms);
        Assert.Equal(before, re.ExtractAllText());           // text byte-identical (no dropped glyphs)
        long fontBytesAfter = TotalType1CBytes(re);
        Assert.True(fontBytesAfter < fontBytesBefore,
            $"Type1C font bytes {fontBytesAfter} not < {fontBytesBefore}");
    }

    private static long TotalType1CBytes(PdfDocument doc)
    {
        doc.MaterializeAllObjects();
        long total = 0;
        foreach (PdfStream s in doc.Objects.Values.OfType<PdfStream>())
            if (s.Dictionary.TryGetValue(new PdfName("Subtype"), out PdfObject st) && st.ToString() == "/Type1C")
                total += s.Length;
        return total;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CID-keyed CFF (/FontFile3 CIDFontType0C) — Adobe Kazuraki tutorial (Japanese)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SubsetFonts_KazurakiCidType0C_PreservesText_AndShrinks()
    {
        const string path = @"Z:\PDF Standards\Legacy_Adobe\5901.Kazuraki_Tutorial.pdf";
        if (!File.Exists(path)) return; // untracked CJK CID fixture; guarded

        string before;
        long fontBytesBefore;
        using (PdfDocument d = PdfDocument.Load(path))
        {
            before = d.ExtractAllText();
            fontBytesBefore = TotalCffFontBytes(d);
        }

        using var ms = new MemoryStream();
        using (PdfDocument d = PdfDocument.Load(path))
            PdfOptimizer.Optimize(d, ms, new PdfOptimizationOptions { SubsetFonts = true });
        ms.Position = 0;

        using PdfDocument re = PdfDocument.Load(ms);
        Assert.Equal(before, re.ExtractAllText());          // extraction unchanged
        long fontBytesAfter = TotalCffFontBytes(re);
        Assert.True(fontBytesAfter < fontBytesBefore,
            $"CFF font bytes {fontBytesAfter} not < {fontBytesBefore}");
    }

    private static long TotalCffFontBytes(PdfDocument doc)
    {
        doc.MaterializeAllObjects();
        long total = 0;
        foreach (PdfStream s in doc.Objects.Values.OfType<PdfStream>())
            if (s.Dictionary.TryGetValue(new PdfName("Subtype"), out PdfObject st) &&
                (st.ToString() == "/Type1C" || st.ToString() == "/CIDFontType0C"))
                total += s.Length;
        return total;
    }
}
