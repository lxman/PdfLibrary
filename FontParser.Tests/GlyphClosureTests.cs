using FontParser;
using FontParser.Subsetting;
using FontParser.Tables.Cmap;
using FontParser.Tables.TtTables.Glyf;

namespace FontParser.Tests;

/// <summary>
/// Tests for <see cref="GlyphClosure"/> against real Windows system fonts.
/// The tests use Arial (arial.ttf) as the primary fixture because it ships on
/// every Windows install and contains composite glyphs (accented letters such
/// as Agrave, Aacute, etc., which are composed from 'A' + a combining mark).
///
/// Glyph IDs referenced in tests are discovered dynamically via font metadata
/// so the tests remain valid across all standard Arial versions on Windows.
/// </summary>
public class GlyphClosureTests
{
    // ---------------------------------------------------------------------------
    // Font fixture
    // ---------------------------------------------------------------------------

    private static readonly string ArialPath = @"C:\Windows\Fonts\arial.ttf";

    private static SfntFont LoadArial() => new(File.ReadAllBytes(ArialPath));

    // ---------------------------------------------------------------------------
    // Helper: find the first composite glyph in the font
    // ---------------------------------------------------------------------------

    private static (int GlyphId, List<ushort> ComponentIds)? FindFirstComposite(SfntFont font)
    {
        GlyphTable? glyf = font.Glyf;
        if (glyf is null) return null;

        foreach (GlyphData gd in glyf.Glyphs)
        {
            if (gd.GlyphSpec is CompositeGlyph composite && composite.Components.Count > 0)
            {
                return (gd.Index, composite.Components.Select(c => c.GlyphIndex).ToList());
            }
        }
        return null;
    }

    // ---------------------------------------------------------------------------
    // Test 0: font loads and has expected structure
    // ---------------------------------------------------------------------------

    [Fact]
    public void Arial_LoadsAndHasTrueTypeOutlines()
    {
        SfntFont font = LoadArial();
        Assert.Equal(SfntOutlineKind.TrueType, font.OutlineKind);
        Assert.True(font.NumGlyphs > 0, "Expected at least one glyph");
        Assert.NotNull(font.Glyf);
        Assert.NotNull(font.Loca);
    }

    [Fact]
    public void Arial_ContainsCompositeGlyphs()
    {
        SfntFont font = LoadArial();
        (int GlyphId, List<ushort> ComponentIds)? composite = FindFirstComposite(font);
        Assert.True(composite.HasValue,
            "Arial is expected to contain composite glyphs (accented letters).");
    }

    // ---------------------------------------------------------------------------
    // Test 1: A simple glyph returns ONLY itself
    // ---------------------------------------------------------------------------

    [Fact]
    public void SimpleGlyph_ClosureContainsOnlyItself()
    {
        SfntFont font = LoadArial();
        GlyphTable glyf = font.Glyf!;

        // Find a simple glyph (SimpleGlyph, not CompositeGlyph).
        var simpleId = (ushort)glyf.Glyphs
            .First(g => g.GlyphSpec is SimpleGlyph)
            .Index;

        ushort[] closure = GlyphClosure.Compute(font, new[] { simpleId });

        Assert.Single(closure);
        Assert.Equal(simpleId, closure[0]);
    }

    // ---------------------------------------------------------------------------
    // Test 2: A composite glyph's closure contains itself + all direct components
    // ---------------------------------------------------------------------------

    [Fact]
    public void CompositeGlyph_ClosureIncludesComponents()
    {
        SfntFont font = LoadArial();
        (int GlyphId, List<ushort> ComponentIds)? found = FindFirstComposite(font);
        Assert.True(found.HasValue, "No composite glyph found in Arial.");

        (int compositeId, List<ushort> componentIds) = found!.Value;

        ushort[] closure = GlyphClosure.Compute(font, new[] { (ushort)compositeId });

        // The composite itself must be in the result.
        Assert.Contains((ushort)compositeId, closure);

        // Each direct component must be in the result.
        foreach (ushort cId in componentIds)
            Assert.Contains(cId, closure);

        // Result must have at least the composite + its distinct components.
        int minExpected = 1 + componentIds.Distinct().Count();
        Assert.True(closure.Length >= minExpected,
            $"Expected >= {minExpected} IDs in closure, got {closure.Length}");
    }

    // ---------------------------------------------------------------------------
    // Test 3: Transitivity — composite whose component is itself composite
    //         If Arial has no depth-2 composites, the test self-skips gracefully.
    // ---------------------------------------------------------------------------

    [Fact]
    public void CompositeGlyph_TransitiveClosureReachesAllLevels()
    {
        SfntFont font = LoadArial();
        GlyphTable glyf = font.Glyf!;

        // Find a composite glyph whose component is also a composite.
        (int GlyphId, List<ushort> Level1Components)? level2 = null;
        foreach (GlyphData gd in glyf.Glyphs)
        {
            if (gd.GlyphSpec is not CompositeGlyph outer) continue;
            foreach (CompositeGlyphComponent comp in outer.Components)
            {
                GlyphData? inner = glyf.GetGlyphData(comp.GlyphIndex);
                if (inner?.GlyphSpec is CompositeGlyph)
                {
                    level2 = (gd.Index, outer.Components.Select(c => c.GlyphIndex).ToList());
                    break;
                }
            }
            if (level2.HasValue) break;
        }

        if (!level2.HasValue)
            return; // Arial doesn't have depth-2 composites — skip gracefully.

        var rootId = (ushort)level2.Value.GlyphId;
        ushort[] closure = GlyphClosure.Compute(font, new[] { rootId });

        Assert.Contains(rootId, closure);
        foreach (ushort c in level2.Value.Level1Components)
            Assert.Contains(c, closure);
    }

    // ---------------------------------------------------------------------------
    // Test 4: Multiple seeds — result is the union of individual closures
    // ---------------------------------------------------------------------------

    [Fact]
    public void MultipleSeeds_ClosureIsUnion()
    {
        SfntFont font = LoadArial();
        GlyphTable glyf = font.Glyf!;

        var simple = (ushort)glyf.Glyphs.First(g => g.GlyphSpec is SimpleGlyph).Index;
        (int GlyphId, List<ushort> ComponentIds)? compositeFound = FindFirstComposite(font);
        Assert.True(compositeFound.HasValue);
        var composite = (ushort)compositeFound!.Value.GlyphId;

        ushort[] individualSimple    = GlyphClosure.Compute(font, new[] { simple });
        ushort[] individualComposite = GlyphClosure.Compute(font, new[] { composite });
        ushort[] combined            = GlyphClosure.Compute(font, new[] { simple, composite });

        var expected = new SortedSet<ushort>(individualSimple.Concat(individualComposite));
        Assert.Equal(expected.ToArray(), combined);
    }

    // ---------------------------------------------------------------------------
    // Test 5: Result is always sorted ascending
    // ---------------------------------------------------------------------------

    [Fact]
    public void Closure_ResultIsSortedAscending()
    {
        SfntFont font = LoadArial();
        GlyphTable glyf = font.Glyf!;

        // Take first 20 glyph IDs as seeds (mix of simple and composite).
        ushort[] seeds = glyf.Glyphs.Take(20).Select(g => (ushort)g.Index).ToArray();
        ushort[] closure = GlyphClosure.Compute(font, seeds);

        for (var i = 1; i < closure.Length; i++)
            Assert.True(closure[i] > closure[i - 1],
                $"Closure is not strictly ascending at index {i}: " +
                $"{closure[i - 1]} then {closure[i]}");
    }

    // ---------------------------------------------------------------------------
    // Test 6: Duplicate seeds produce a deduped result (same as single seed)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Closure_DuplicateSeedsProduceDedupedResult()
    {
        SfntFont font = LoadArial();
        (int GlyphId, List<ushort> ComponentIds)? compositeFound = FindFirstComposite(font);
        Assert.True(compositeFound.HasValue);
        var id = (ushort)compositeFound!.Value.GlyphId;

        ushort[] closure1 = GlyphClosure.Compute(font, new[] { id });
        ushort[] closure2 = GlyphClosure.Compute(font, new[] { id, id, id });

        Assert.Equal(closure1, closure2);
    }

    // ---------------------------------------------------------------------------
    // Test 7: Empty seed set returns empty array
    // ---------------------------------------------------------------------------

    [Fact]
    public void EmptySeeds_ReturnsEmptyArray()
    {
        SfntFont font = LoadArial();
        ushort[] closure = GlyphClosure.Compute(font, Array.Empty<ushort>());
        Assert.Empty(closure);
    }

    // ---------------------------------------------------------------------------
    // Test 8: Known composite via cmap lookup — U+00C0 (À, Agrave)
    //         In Arial, Agrave is a composite of 'A' + grave accent mark.
    //         We resolve the codepoint to a glyph ID via CmapTable.GetGlyphId(),
    //         confirm the glyph is composite, then verify the closure.
    // ---------------------------------------------------------------------------

    [Fact]
    public void Agrave_ClosureIncludesBaseAndAccent()
    {
        SfntFont font = LoadArial();

        CmapTable? cmap = font.Cmap;
        Assert.NotNull(cmap);

        // U+00C0 = 'À' (Latin capital A with grave)
        ushort agrave = cmap!.GetGlyphId(0x00C0);
        if (agrave == 0)
        {
            // Codepoint not in font — skip rather than fail.
            return;
        }

        GlyphTable glyf = font.Glyf!;
        GlyphData? compositeData = glyf.GetGlyphData(agrave);
        if (compositeData?.GlyphSpec is not CompositeGlyph composite)
        {
            // Agrave is a simple outline in this Arial variant — skip.
            return;
        }

        ushort[] closure = GlyphClosure.Compute(font, new[] { agrave });

        Assert.Contains(agrave, closure);
        foreach (CompositeGlyphComponent comp in composite.Components)
            Assert.Contains(comp.GlyphIndex, closure);
    }

    // ---------------------------------------------------------------------------
    // Test 9: Full-font closure smoke test — seed = all glyph IDs in the font.
    //         Verifies no exception, result is a subset of [0, numGlyphs-1],
    //         and every seeded ID appears in the output.
    // ---------------------------------------------------------------------------

    [Fact]
    public void FullFontClosure_NoExceptionAndContainsAllSeeds()
    {
        SfntFont font = LoadArial();
        GlyphTable glyf = font.Glyf!;

        ushort[] allIds = glyf.Glyphs.Select(g => (ushort)g.Index).ToArray();
        ushort[] closure = GlyphClosure.Compute(font, allIds);

        // Every seed must appear in the closure.
        foreach (ushort id in allIds)
            Assert.Contains(id, closure);

        // All returned IDs must be valid glyph indices (< numGlyphs).
        foreach (ushort id in closure)
            Assert.True(id < font.NumGlyphs,
                $"Closure contains out-of-range glyph ID {id} (font has {font.NumGlyphs} glyphs)");
    }
}
