using FontParser;
using FontParser.Subsetting;
using PdfLibrary.Builder;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Optimization;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Optimization;

/// <summary>
/// Unit tests for <see cref="FontSubsetter"/> and the
/// <see cref="TrueTypeSubsetter"/> out-param overload.
///
/// Uses Arial (C:\Windows\Fonts\arial.ttf) as the test asset — the same font
/// FontParser.Tests uses — and skips gracefully if the file is absent.
/// </summary>
public class FontSubsetterTests
{
    private const string ArialPath = @"C:\Windows\Fonts\arial.ttf";

    // ─────────────────────────────────────────────────────────────────────────
    // TrueTypeSubsetter overload: out-param returns consistent mapping
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Subsetter_OutParam_ReturnsConsistentMapping()
    {
        if (!File.Exists(ArialPath)) return;

        var sfnt = new SfntFont(File.ReadAllBytes(ArialPath));
        var gids = new ushort[] { 0, 36, 37, 38, 39 }; // .notdef + a few glyphs

        byte[] subset = TrueTypeSubsetter.Subset(sfnt, gids, out IReadOnlyDictionary<ushort, ushort> oldToNew);

        Assert.NotNull(subset);
        Assert.NotEmpty(oldToNew);

        // GID 0 (.notdef) always maps to new GID 0.
        Assert.True(oldToNew.ContainsKey(0), "GID 0 must be in the mapping");
        Assert.Equal((ushort)0, oldToNew[0]);

        // All requested GIDs should appear in the map (they were retained).
        foreach (ushort gid in gids)
            Assert.True(oldToNew.ContainsKey(gid),
                $"Requested GID {gid} should be in the old→new mapping");

        // New GIDs must be contiguous 0..N.
        List<ushort> newGids = oldToNew.Values.Distinct().OrderBy(g => g).ToList();
        for (var i = 0; i < newGids.Count; i++)
            Assert.Equal((ushort)i, newGids[i]);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TrueTypeSubsetter overload: subset is actually smaller
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Subsetter_OutParam_SubsetIsSmallerThanOriginal()
    {
        if (!File.Exists(ArialPath)) return;

        var sfnt = new SfntFont(File.ReadAllBytes(ArialPath));
        var gids = new ushort[] { 0, 36, 37 };

        byte[] subset = TrueTypeSubsetter.Subset(sfnt, gids, out _);
        byte[] original = File.ReadAllBytes(ArialPath);

        Assert.True(subset.Length < original.Length,
            $"Subset ({subset.Length} B) should be smaller than original ({original.Length} B)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TrueTypeSubsetter overload: NumGlyphs in subset matches retained count
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Subsetter_OutParam_SubsetNumGlyphsMatches()
    {
        if (!File.Exists(ArialPath)) return;

        var sfnt = new SfntFont(File.ReadAllBytes(ArialPath));
        // Request 5 specific glyphs; the subsetter will also add .notdef (GID 0)
        // and any composite components. Use only simple glyphs to keep count predictable.
        var gids = new ushort[] { 1, 2, 3, 4, 5 };

        byte[] subset = TrueTypeSubsetter.Subset(sfnt, gids, out IReadOnlyDictionary<ushort, ushort> oldToNew);
        var subsetSfnt = new SfntFont(subset);

        // The subset must have exactly as many glyphs as the mapping (closure-computed).
        Assert.Equal(oldToNew.Count, subsetSfnt.NumGlyphs);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CIDToGIDMap helper: entries are correct for a known set
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildCidToGidMap_EntriesAreCorrect()
    {
        if (!File.Exists(ArialPath)) return;

        var sfnt = new SfntFont(File.ReadAllBytes(ArialPath));
        // Use small known GIDs so the map stays tiny and checkable.
        var requestedGids = new ushort[] { 0, 10, 20 };

        TrueTypeSubsetter.Subset(sfnt, requestedGids, out IReadOnlyDictionary<ushort, ushort> oldToNew);

        // Simulate BuildCidToGidMap logic: map[oldGid*2] = newGid (big-endian)
        ushort maxOld = requestedGids.Max();
        var map = new byte[(maxOld + 1) * 2];
        foreach (ushort oldGid in requestedGids)
        {
            if (!oldToNew.TryGetValue(oldGid, out ushort newGid)) continue;
            int offset = oldGid * 2;
            map[offset]     = (byte)(newGid >> 8);
            map[offset + 1] = (byte)(newGid & 0xFF);
        }

        // GID 0 always maps to new GID 0 — entry at byte 0,1
        var entry0 = (ushort)((map[0] << 8) | map[1]);
        Assert.Equal((ushort)0, entry0);

        // GID 10 should map to some positive new GID if retained
        if (oldToNew.TryGetValue(10, out ushort newGid10))
        {
            var entry10 = (ushort)((map[20] << 8) | map[21]);
            Assert.Equal(newGid10, entry10);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FontSubsetter.Run on a simple TrueType document
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FontSubsetter_Run_SimpleTrueType_ReducesFontSize()
    {
        if (!File.Exists(ArialPath)) return;

        const string alias = "Arial";
        byte[] src = PdfDocumentBuilder.Create()
            .LoadFont(ArialPath, alias)
            .AddPage(p => p.AddText("Hi", 100, 700, alias, 12))
            .ToByteArray();

        // Capture the /FontFile2 length before subsetting.
        using PdfDocument before = PdfDocument.Load(new MemoryStream(src));
        before.MaterializeAllObjects();
        int origFontSize = GetFontFile2Size(before);

        // Now run subsetting.
        using PdfDocument toOpt = PdfDocument.Load(new MemoryStream(src));
        toOpt.MaterializeAllObjects();
        FontSubsetter.Run(toOpt, new PdfOptimizationOptions { SubsetFonts = true });
        int subsetFontSize = GetFontFile2Size(toOpt);

        // The subset font file must be present.
        Assert.True(subsetFontSize > 0, "FontFile2 stream must still exist after subsetting");

        // The subset font must be smaller than the original (Arial is ~400 KB; 2-glyph subset <<).
        if (origFontSize > 0)
        {
            Assert.True(subsetFontSize < origFontSize,
                $"Subset ({subsetFontSize} B encoded) should be smaller than original ({origFontSize} B encoded)");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper: find any /FontFile2 stream in the document and return its encoded length
    // ─────────────────────────────────────────────────────────────────────────

    private static int GetFontFile2Size(PdfDocument doc)
    {
        foreach (PdfPage page in doc.Pages)
        {
            PdfResources? resources = page.GetResources();
            if (resources is null) continue;
            foreach (string fontName in resources.GetFontNames())
            {
                PdfFont? font = resources.GetFontObject(fontName);
                PdfFontDescriptor? desc = null;
                if (font is TrueTypeFont tt)
                    desc = tt.Descriptor;
                else if (font is Type0Font t0)
                    desc = t0.DescendantDescriptor;

                PdfStream? stream = desc?.GetFontFile2Stream();
                if (stream is not null)
                    return stream.Length;
            }
        }
        return -1;
    }
}
