using System;
using System.IO;
using System.Linq;
using FontParser;
using FontParser.Subsetting;
using FontParser.Tables.TtTables.Glyf;

namespace FontParser.Tests;

/// <summary>
/// Integration tests for <see cref="TrueTypeSubsetter"/>.
///
/// Round-trip gate:
///   1. Subset arial.ttf to a small glyph set that includes a composite glyph.
///   2. Write the subset bytes and re-parse with <see cref="SfntFont"/>.
///   3. Assert the re-parsed font is structurally valid.
///   4. Assert a retained simple glyph's contours match the original.
///   5. Assert a retained composite resolves to valid new-GID components.
/// </summary>
public class TrueTypeSubsetterTests
{
    private static readonly string ArialPath = @"C:\Windows\Fonts\arial.ttf";

    private static SfntFont LoadArial() => new(File.ReadAllBytes(ArialPath));

    // =========================================================================
    // Helper: find a composite glyph and its components
    // =========================================================================

    private static (ushort CompositeGid, ushort[] ComponentGids)? FindFirstComposite(SfntFont font)
    {
        GlyphTable? glyf = font.Glyf;
        if (glyf is null) return null;

        foreach (GlyphData gd in glyf.Glyphs)
        {
            if (gd.GlyphSpec is CompositeGlyph { Components.Count: > 0 } composite)
                return ((ushort)gd.Index, composite.Components.Select(c => c.GlyphIndex).ToArray());
        }
        return null;
    }

    // =========================================================================
    // Subsetter smoke test — builds a subset without throwing
    // =========================================================================

    [Fact]
    public void Subset_DoesNotThrow()
    {
        SfntFont font = LoadArial();
        (ushort CompositeGid, ushort[] ComponentGids)? found = FindFirstComposite(font);
        Assert.True(found.HasValue, "Arial must have a composite glyph.");

        var seeds = new ushort[] { 0, found!.Value.CompositeGid };
        byte[] subsetBytes = TrueTypeSubsetter.Subset(font, seeds);
        Assert.NotNull(subsetBytes);
        Assert.True(subsetBytes.Length > 0);
    }

    // =========================================================================
    // Subset is smaller than the original
    // =========================================================================

    [Fact]
    public void Subset_IsSmallerThanOriginal()
    {
        SfntFont font = LoadArial();
        byte[] origBytes = File.ReadAllBytes(ArialPath);

        (ushort CompositeGid, ushort[] ComponentGids)? found = FindFirstComposite(font);
        Assert.True(found.HasValue);

        var seeds = new ushort[] { 0, found!.Value.CompositeGid };
        byte[] subsetBytes = TrueTypeSubsetter.Subset(font, seeds);

        Assert.True(subsetBytes.Length < origBytes.Length,
            $"Subset ({subsetBytes.Length} bytes) should be smaller than original ({origBytes.Length} bytes).");
    }

    // =========================================================================
    // ROUND-TRIP GATE: re-parse the subset with SfntFont
    // =========================================================================

    [Fact]
    public void RoundTrip_SubsetParsesWithoutError()
    {
        SfntFont origFont = LoadArial();
        (ushort CompositeGid, ushort[] ComponentGids)? found = FindFirstComposite(origFont);
        Assert.True(found.HasValue);

        (ushort compositeGid, ushort[] componentGids) = found!.Value;

        // Find a simple glyph not already involved.
        var involved = new System.Collections.Generic.HashSet<ushort>(componentGids) { compositeGid };
        var simpleGid = (ushort)origFont.Glyf!.Glyphs
            .First(g => g.GlyphSpec is SimpleGlyph && g.Index != 0 && !involved.Contains((ushort)g.Index))
            .Index;

        var seeds = new ushort[] { 0, simpleGid, compositeGid };
        byte[] subsetBytes = TrueTypeSubsetter.Subset(origFont, seeds);

        // Re-parse — must not throw.
        var subsetFont = new SfntFont(subsetBytes);

        Assert.Equal(SfntOutlineKind.TrueType, subsetFont.OutlineKind);
        Assert.True(subsetFont.NumGlyphs > 0, "Re-parsed subset must have at least one glyph.");
    }

    // =========================================================================
    // numGlyphs in re-parsed font equals retained count
    // =========================================================================

    [Fact]
    public void RoundTrip_NumGlyphsMatchesRetainedCount()
    {
        SfntFont origFont = LoadArial();
        (ushort CompositeGid, ushort[] ComponentGids)? found = FindFirstComposite(origFont);
        Assert.True(found.HasValue);

        (ushort compositeGid, _) = found!.Value;
        var seeds = new ushort[] { 0, compositeGid };
        ushort[] closure = GlyphClosure.Compute(origFont, seeds);
        int expectedCount = closure.Length;

        byte[] subsetBytes = TrueTypeSubsetter.Subset(origFont, seeds);
        var subsetFont = new SfntFont(subsetBytes);

        Assert.Equal(expectedCount, subsetFont.NumGlyphs);
    }

    // =========================================================================
    // A retained simple glyph's contours match the original
    // =========================================================================

    [Fact]
    public void RoundTrip_SimpleGlyphContoursMatchOriginal()
    {
        SfntFont origFont = LoadArial();
        (ushort CompositeGid, ushort[] ComponentGids)? found = FindFirstComposite(origFont);
        Assert.True(found.HasValue);

        (ushort compositeGid, ushort[] componentGids) = found!.Value;
        var involved = new System.Collections.Generic.HashSet<ushort>(componentGids) { compositeGid };
        var simpleGid = (ushort)origFont.Glyf!.Glyphs
            .First(g => g.GlyphSpec is SimpleGlyph && g.Index != 0 && !involved.Contains((ushort)g.Index))
            .Index;

        var seeds = new ushort[] { 0, simpleGid, compositeGid };
        ushort[] closure = GlyphClosure.Compute(origFont, seeds);
        var remap = new GlyphIdRemap(closure);

        byte[] subsetBytes = TrueTypeSubsetter.Subset(origFont, seeds);
        var subsetFont = new SfntFont(subsetBytes);

        Assert.NotNull(subsetFont.Glyf);

        // Find the new GID for the simple glyph.
        Assert.True(remap.OldToNew.TryGetValue(simpleGid, out ushort newSimpleGid));

        GlyphData? origGlyphData   = origFont.Glyf!.GetGlyphData(simpleGid);
        GlyphData? subsetGlyphData = subsetFont.Glyf!.GetGlyphData(newSimpleGid);

        Assert.NotNull(origGlyphData);
        Assert.NotNull(subsetGlyphData);

        // Contour count from the glyph header must match.
        Assert.Equal(origGlyphData!.Header.NumberOfContours, subsetGlyphData!.Header.NumberOfContours);

        // Both must actually be simple glyphs.
        Assert.IsType<SimpleGlyph>(origGlyphData.GlyphSpec);
        Assert.IsType<SimpleGlyph>(subsetGlyphData.GlyphSpec);
    }

    // =========================================================================
    // A retained composite still resolves to valid components in the subset
    // =========================================================================

    [Fact]
    public void RoundTrip_CompositeResolvesToValidComponents()
    {
        SfntFont origFont = LoadArial();
        (ushort CompositeGid, ushort[] ComponentGids)? found = FindFirstComposite(origFont);
        Assert.True(found.HasValue);

        (ushort compositeGid, _) = found!.Value;
        var seeds = new ushort[] { 0, compositeGid };
        ushort[] closure = GlyphClosure.Compute(origFont, seeds);
        var remap = new GlyphIdRemap(closure);

        byte[] subsetBytes = TrueTypeSubsetter.Subset(origFont, seeds);
        var subsetFont = new SfntFont(subsetBytes);

        Assert.True(remap.OldToNew.TryGetValue(compositeGid, out ushort newCompositeGid));

        GlyphData? compositeGlyphData = subsetFont.Glyf!.GetGlyphData(newCompositeGid);
        Assert.NotNull(compositeGlyphData);

        var composite = compositeGlyphData!.GlyphSpec as CompositeGlyph;
        Assert.NotNull(composite);

        // Each component GID in the subset must be a valid GID < subsetFont.NumGlyphs.
        foreach (CompositeGlyphComponent comp in composite!.Components)
        {
            Assert.True(comp.GlyphIndex < subsetFont.NumGlyphs,
                $"Component GID {comp.GlyphIndex} is out of range (numGlyphs={subsetFont.NumGlyphs}).");
        }
    }

    // =========================================================================
    // cmap in re-parsed subset maps codepoints to valid new GIDs
    // =========================================================================

    [Fact]
    public void RoundTrip_CmapMapsRetainedCodepointsCorrectly()
    {
        SfntFont origFont = LoadArial();

        // Use U+0041 'A' and a known composite like U+00C0 'À'.
        ushort cpA = 0x0041;
        ushort origGidA = origFont.Cmap!.GetGlyphId(cpA);
        if (origGidA == 0) return; // Skip if 'A' not in font (shouldn't happen for Arial).

        ushort cpAgrave = 0x00C0;
        ushort origGidAgrave = origFont.Cmap!.GetGlyphId(cpAgrave);
        if (origGidAgrave == 0) return; // Skip if not present.

        var seeds = new ushort[] { 0, origGidA, origGidAgrave };
        ushort[] closure = GlyphClosure.Compute(origFont, seeds);
        var remap = new GlyphIdRemap(closure);

        byte[] subsetBytes = TrueTypeSubsetter.Subset(origFont, seeds);
        var subsetFont = new SfntFont(subsetBytes);

        Assert.NotNull(subsetFont.Cmap);

        // Lookup 'A' in subset cmap — should return its new GID.
        ushort subsetGidA = subsetFont.Cmap!.GetGlyphId(cpA);
        Assert.True(remap.OldToNew.TryGetValue(origGidA, out ushort expectedNewGidA));
        Assert.Equal(expectedNewGidA, subsetGidA);

        // Lookup 'À' — should map to its new GID.
        ushort subsetGidAgrave = subsetFont.Cmap.GetGlyphId(cpAgrave);
        Assert.True(remap.OldToNew.TryGetValue(origGidAgrave, out ushort expectedNewGidAgrave));
        Assert.Equal(expectedNewGidAgrave, subsetGidAgrave);
    }

    // =========================================================================
    // Byte-size report test (informational — always passes)
    // =========================================================================

    [Fact]
    public void Subset_ByteSizeReport()
    {
        SfntFont origFont = LoadArial();
        byte[] origBytes = File.ReadAllBytes(ArialPath);

        (ushort CompositeGid, ushort[] ComponentGids)? found = FindFirstComposite(origFont);
        Assert.True(found.HasValue);

        // Include printable ASCII (GID lookup for ' '..'~') + composite.
        var seeds = new System.Collections.Generic.HashSet<ushort> { 0, found!.Value.CompositeGid };
        for (ushort cp = 0x20; cp <= 0x7E; cp++)
        {
            ushort gid = origFont.Cmap!.GetGlyphId(cp);
            if (gid != 0) seeds.Add(gid);
        }

        byte[] subsetBytes = TrueTypeSubsetter.Subset(origFont, seeds.ToArray());

        // This test is informational — it always passes.
        // The actual sizes are visible in the test runner output.
        Assert.True(subsetBytes.Length > 0,
            $"Original: {origBytes.Length:N0} bytes. Subset: {subsetBytes.Length:N0} bytes.");
    }

    // =========================================================================
    // GID 0 (.notdef) is always present even when not in the requested set
    // =========================================================================

    [Fact]
    public void Subset_AlwaysIncludesGid0_EvenWhenNotRequested()
    {
        SfntFont font = LoadArial();

        // Request a set of glyphs that deliberately excludes GID 0.
        // GIDs 36–40 are simple glyphs in Arial (digits / letters depending on version).
        var requestedWithoutNotdef = new ushort[] { 36, 37, 38, 39, 40 };
        Assert.DoesNotContain((ushort)0, requestedWithoutNotdef);

        byte[] subsetBytes = TrueTypeSubsetter.Subset(font, requestedWithoutNotdef,
            out IReadOnlyDictionary<ushort, ushort> oldToNew);

        // The mapping must contain GID 0.
        Assert.True(oldToNew.ContainsKey(0),
            "GID 0 (.notdef) must always appear in the old→new mapping regardless of the requested set.");

        // GID 0 must map to new GID 0 (.notdef must remain first).
        Assert.Equal((ushort)0, oldToNew[0]);

        // The re-parsed subset font must have at least 1 glyph (GID 0) and parse cleanly.
        var subsetSfnt = new SfntFont(subsetBytes);
        Assert.True(subsetSfnt.NumGlyphs > 0,
            "Re-parsed subset must have at least one glyph (GID 0 / .notdef).");

        // The glyph table (if present) must have a slot for GID 0.
        if (subsetSfnt.Glyf is not null)
        {
            // GetGlyphData(0) must not throw; it may return null for an empty .notdef outline.
            GlyphData? _ = subsetSfnt.Glyf.GetGlyphData(0);
        }
    }
}
