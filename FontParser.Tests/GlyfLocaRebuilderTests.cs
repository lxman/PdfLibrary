using System;
using System.Linq;
using FontParser;
using FontParser.Subsetting;
using FontParser.Tables.TtTables.Glyf;

namespace FontParser.Tests;

/// <summary>
/// Tests for <see cref="GlyphIdRemap"/> and <see cref="GlyfLocaRebuilder"/> against
/// the real Arial font (C:\Windows\Fonts\arial.ttf).
///
/// Invariants verified:
///   1. GlyphIdRemap is a bijection over the retained set.
///   2. New loca is monotonically non-decreasing and has exactly numGlyphs+1 entries.
///   3. A retained composite glyph has all its component GIDs patched to valid new indices.
///   4. A simple glyph's bytes are copied verbatim (no mutations).
///   5. Glyph at new-GID k occupies loca[k]..loca[k+1] in the new glyf table.
///   6. Short loca is produced when the subset is small enough.
///   7. Zero-length (empty outline) glyphs remain zero-length in the rebuilt table.
/// </summary>
public class GlyfLocaRebuilderTests
{
    // -------------------------------------------------------------------------
    // Font fixture
    // -------------------------------------------------------------------------

    private static readonly string ArialPath = @"C:\Windows\Fonts\arial.ttf";

    private static SfntFont LoadArial() => new(File.ReadAllBytes(ArialPath));

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Find the first composite glyph in the font and return its old GID plus the
    /// old GIDs of all its direct component glyphs.
    /// </summary>
    private static (ushort CompositeGid, ushort[] ComponentGids)? FindFirstCompositeWithComponents(SfntFont font)
    {
        GlyphTable? glyf = font.Glyf;
        if (glyf is null) return null;

        foreach (GlyphData gd in glyf.Glyphs)
        {
            if (gd.GlyphSpec is CompositeGlyph composite && composite.Components.Count > 0)
            {
                return ((ushort)gd.Index, composite.Components.Select(c => c.GlyphIndex).ToArray());
            }
        }
        return null;
    }

    /// <summary>
    /// Build a small but complete seed set: .notdef (GID 0), one simple glyph,
    /// a composite glyph, and all of its components.  Then compute the full
    /// closure so we have a coherent subset, and run the rebuild.
    /// </summary>
    private static (GlyphIdRemap remap, GlyfLocaRebuilder.RebuildResult result, SfntFont font)
        BuildSmallSubset()
    {
        SfntFont font = LoadArial();

        (ushort CompositeGid, ushort[] ComponentGids)? found = FindFirstCompositeWithComponents(font);
        Assert.True(found.HasValue, "Arial must contain a composite glyph for these tests.");

        (ushort compositeGid, ushort[] componentGids) = found!.Value;

        // Find a simple (non-composite) glyph that is NOT already a component.
        var componentSet = new System.Collections.Generic.HashSet<ushort>(componentGids);
        componentSet.Add(compositeGid);

        var simpleGid = (ushort)font.Glyf!.Glyphs
            .First(g => g.GlyphSpec is SimpleGlyph && !componentSet.Contains((ushort)g.Index))
            .Index;

        // Seeds: .notdef + simple + composite (GlyphClosure adds components).
        var seeds = new ushort[] { 0, simpleGid, compositeGid };
        ushort[] closure = GlyphClosure.Compute(font, seeds);

        var remap  = new GlyphIdRemap(closure);
        GlyfLocaRebuilder.RebuildResult result = GlyfLocaRebuilder.Rebuild(
            font.GetTableBytes("glyf")!,
            font.Loca!,
            remap,
            font.Glyf);

        return (remap, result, font);
    }

    // =========================================================================
    // GlyphIdRemap tests
    // =========================================================================

    [Fact]
    public void Remap_ContiguousNewGids()
    {
        SfntFont font = LoadArial();
        (ushort CompositeGid, ushort[] ComponentGids)? found = FindFirstCompositeWithComponents(font);
        Assert.True(found.HasValue);
        (ushort compositeGid, _) = found!.Value;

        ushort[] closure = GlyphClosure.Compute(font, new ushort[] { compositeGid });
        var remap   = new GlyphIdRemap(closure);

        // New GIDs must be exactly 0..N-1.
        ushort[] newGids = remap.OldToNew.Values.OrderBy(x => x).ToArray();
        for (var i = 0; i < newGids.Length; i++)
            Assert.Equal((ushort)i, newGids[i]);
    }

    [Fact]
    public void Remap_Bijection_OldToNew_And_NewToOld()
    {
        SfntFont font = LoadArial();
        GlyphTable glyf   = font.Glyf!;
        ushort[] seeds  = glyf.Glyphs.Take(30).Select(g => (ushort)g.Index).ToArray();
        ushort[] closure = GlyphClosure.Compute(font, seeds);
        var remap   = new GlyphIdRemap(closure);

        // OldToNew and NewToOld must be mutual inverses.
        for (var newGid = 0; newGid < remap.Count; newGid++)
        {
            ushort oldGid = remap.NewToOld[newGid];
            Assert.True(remap.OldToNew.TryGetValue(oldGid, out ushort roundTrip));
            Assert.Equal((ushort)newGid, roundTrip);
        }

        // Every entry in OldToNew must appear in NewToOld at the correct index.
        foreach (KeyValuePair<ushort, ushort> kv in remap.OldToNew)
        {
            ushort expectedOld = remap.NewToOld[kv.Value];
            Assert.Equal(kv.Key, expectedOld);
        }
    }

    [Fact]
    public void Remap_CountMatchesClosure()
    {
        SfntFont font = LoadArial();
        (ushort CompositeGid, ushort[] ComponentGids)? found = FindFirstCompositeWithComponents(font);
        Assert.True(found.HasValue);
        (ushort compositeGid, _) = found!.Value;

        ushort[] closure = GlyphClosure.Compute(font, new ushort[] { compositeGid });
        var remap   = new GlyphIdRemap(closure);

        Assert.Equal(closure.Length, remap.Count);
        Assert.Equal(closure.Length, remap.OldToNew.Count);
        Assert.Equal(closure.Length, remap.NewToOld.Count);
    }

    // =========================================================================
    // GlyfLocaRebuilder — loca invariants
    // =========================================================================

    [Fact]
    public void NewLoca_LengthIsNumGlyphsPlusOne()
    {
        (GlyphIdRemap remap, GlyfLocaRebuilder.RebuildResult result, _) = BuildSmallSubset();
        Assert.Equal(remap.Count + 1, result.LocaOffsets.Length);
    }

    [Fact]
    public void NewLoca_IsMonotonicallyNonDecreasing()
    {
        (_, GlyfLocaRebuilder.RebuildResult result, _) = BuildSmallSubset();
        for (var i = 1; i <= result.NumGlyphs; i++)
        {
            Assert.True(result.LocaOffsets[i] >= result.LocaOffsets[i - 1],
                $"loca is not monotonic at index {i}: {result.LocaOffsets[i - 1]} > {result.LocaOffsets[i]}");
        }
    }

    [Fact]
    public void NewLoca_ByteEncodingMatchesOffsets()
    {
        (_, GlyfLocaRebuilder.RebuildResult result, _) = BuildSmallSubset();

        if (result.UseShortLoca)
        {
            Assert.Equal((result.NumGlyphs + 1) * 2, result.LocaBytes.Length);
            for (var i = 0; i <= result.NumGlyphs; i++)
            {
                var encoded = (ushort)((result.LocaBytes[i * 2] << 8) | result.LocaBytes[i * 2 + 1]);
                Assert.Equal(result.LocaOffsets[i], (uint)(encoded * 2));
            }
        }
        else
        {
            Assert.Equal((result.NumGlyphs + 1) * 4, result.LocaBytes.Length);
            for (var i = 0; i <= result.NumGlyphs; i++)
            {
                var encoded = (uint)(
                    (result.LocaBytes[i * 4]     << 24) |
                    (result.LocaBytes[i * 4 + 1] << 16) |
                    (result.LocaBytes[i * 4 + 2] << 8)  |
                     result.LocaBytes[i * 4 + 3]);
                Assert.Equal(result.LocaOffsets[i], encoded);
            }
        }
    }

    [Fact]
    public void SmallSubset_UsesShortLoca()
    {
        // A small subset (a handful of glyphs from a typical font) must always fit in
        // short loca format (max offset well under 0x1FFFE).
        (_, GlyfLocaRebuilder.RebuildResult result, _) = BuildSmallSubset();
        Assert.True(result.UseShortLoca,
            "A tiny subset should always produce short-format loca.");
    }

    // =========================================================================
    // GlyfLocaRebuilder — per-glyph placement
    // =========================================================================

    [Fact]
    public void Glyph_SitsAtCorrectLocaRange()
    {
        // For every new GID k, the glyph data in the rebuilt glyf table must start at
        // loca[k] and end at loca[k+1] (with possible 2-byte padding).
        (GlyphIdRemap remap, GlyfLocaRebuilder.RebuildResult result, SfntFont font) = BuildSmallSubset();

        byte[] origGlyf = font.GetTableBytes("glyf")!;

        for (var newGid = 0; newGid < result.NumGlyphs; newGid++)
        {
            ushort oldGid = remap.NewToOld[newGid];

            uint origStart = font.Loca!.Offsets[oldGid];
            uint origEnd   = font.Loca.Offsets[oldGid + 1];
            uint origLen   = origEnd - origStart;

            uint newStart = result.LocaOffsets[newGid];
            uint newEnd   = result.LocaOffsets[newGid + 1];
            uint newLen   = newEnd - newStart;

            if (origLen == 0)
            {
                // Zero-length glyphs must map to zero-length entries.
                Assert.Equal(0u, newLen);
            }
            else
            {
                // Padded length must be at least origLen and differ by at most 1 byte.
                Assert.True(newLen >= origLen && newLen <= origLen + 1,
                    $"new-GID {newGid} (old {oldGid}): original length {origLen}, rebuilt length {newLen}");
            }
        }
    }

    [Fact]
    public void SimpleGlyph_BytesAreCopiedVerbatim()
    {
        SfntFont font = LoadArial();

        (ushort CompositeGid, ushort[] ComponentGids)? found = FindFirstCompositeWithComponents(font);
        Assert.True(found.HasValue);
        (ushort compositeGid, ushort[] componentGids) = found!.Value;

        // Find a simple glyph that is NOT a component of the composite we picked.
        var componentSet = new System.Collections.Generic.HashSet<ushort>(componentGids);
        componentSet.Add(compositeGid);

        var simpleGid = (ushort)font.Glyf!.Glyphs
            .First(g => g.GlyphSpec is SimpleGlyph && !componentSet.Contains((ushort)g.Index))
            .Index;

        var seeds   = new ushort[] { simpleGid, compositeGid };
        ushort[] closure = GlyphClosure.Compute(font, seeds);
        var remap   = new GlyphIdRemap(closure);
        GlyfLocaRebuilder.RebuildResult result  = GlyfLocaRebuilder.Rebuild(
            font.GetTableBytes("glyf")!,
            font.Loca!,
            remap,
            font.Glyf);

        // Locate the simple glyph in the rebuilt table.
        Assert.True(remap.OldToNew.TryGetValue(simpleGid, out ushort newGid));

        uint newStart = result.LocaOffsets[newGid];
        uint newEnd   = result.LocaOffsets[newGid + 1];
        Assert.True(newEnd > newStart, "Simple glyph must have non-zero length.");
        uint newLen = newEnd - newStart;

        // Original bytes.
        byte[] origGlyf = font.GetTableBytes("glyf")!;
        uint origStart = font.Loca!.Offsets[simpleGid];
        uint origLen   = font.Loca.Offsets[simpleGid + 1] - origStart;

        // The copy must be byte-identical for the original glyph length.
        for (var i = 0; i < (int)origLen; i++)
        {
            byte expected = origGlyf[origStart + i];
            byte actual   = result.GlyfBytes[newStart + i];
            Assert.True(expected == actual,
                $"Simple glyph byte mismatch at offset {i}: expected 0x{expected:X2}, got 0x{actual:X2}");
        }
    }

    // =========================================================================
    // Composite glyph patching
    // =========================================================================

    [Fact]
    public void CompositeGlyph_ComponentGidsArePatched()
    {
        SfntFont font = LoadArial();

        (ushort CompositeGid, ushort[] ComponentGids)? found = FindFirstCompositeWithComponents(font);
        Assert.True(found.HasValue);
        (ushort compositeGid, ushort[] componentGids) = found!.Value;

        var seeds   = new ushort[] { compositeGid };
        ushort[] closure = GlyphClosure.Compute(font, seeds);
        var remap   = new GlyphIdRemap(closure);
        GlyfLocaRebuilder.RebuildResult result  = GlyfLocaRebuilder.Rebuild(
            font.GetTableBytes("glyf")!,
            font.Loca!,
            remap,
            font.Glyf);

        // Locate composite in rebuilt table.
        Assert.True(remap.OldToNew.TryGetValue(compositeGid, out ushort newCompositeGid));

        uint newStart = result.LocaOffsets[newCompositeGid];
        uint newEnd   = result.LocaOffsets[newCompositeGid + 1];
        Assert.True(newEnd > newStart);

        // Parse the rebuilt composite glyph raw bytes to verify patched GIDs.
        // Walk the component chain in the rebuilt glyf bytes.
        List<ushort> rebuiltComponents = ReadCompositeComponentGids(result.GlyfBytes, (int)newStart);

        // Every rebuilt component GID must be a valid new GID (< remap.Count).
        foreach (ushort rebuiltGid in rebuiltComponents)
        {
            Assert.True(rebuiltGid < remap.Count,
                $"Patched component GID {rebuiltGid} is out of range (remap.Count={remap.Count}).");
        }

        // Each old component GID must map to the GID we see in the rebuilt bytes.
        Assert.Equal(componentGids.Length, rebuiltComponents.Count);
        for (var i = 0; i < componentGids.Length; i++)
        {
            ushort expectedNew = remap.OldToNew[componentGids[i]];
            Assert.Equal(expectedNew, rebuiltComponents[i]);
        }
    }

    [Fact]
    public void CompositeGlyph_PatchedComponentsResolveToValidNewIndices()
    {
        (GlyphIdRemap remap, GlyfLocaRebuilder.RebuildResult result, SfntFont font) = BuildSmallSubset();

        byte[] origGlyf = font.GetTableBytes("glyf")!;
        GlyphTable glyfTable   = font.Glyf!;

        for (var newGid = 0; newGid < result.NumGlyphs; newGid++)
        {
            ushort oldGid = remap.NewToOld[newGid];
            GlyphData? gd = glyfTable.GetGlyphData(oldGid);
            if (gd?.GlyphSpec is not CompositeGlyph) continue;

            uint start = result.LocaOffsets[newGid];
            uint end   = result.LocaOffsets[newGid + 1];
            if (end <= start) continue;

            List<ushort> patchedComponentGids = ReadCompositeComponentGids(result.GlyfBytes, (int)start);

            foreach (ushort componentNewGid in patchedComponentGids)
            {
                Assert.True(componentNewGid < remap.Count,
                    $"Composite new-GID {newGid} has component new-GID {componentNewGid} " +
                    $"which is >= remap.Count ({remap.Count}).");
            }
        }
    }

    // =========================================================================
    // Edge cases
    // =========================================================================

    [Fact]
    public void EmptyClosure_ProducesEmptyTables()
    {
        SfntFont font = LoadArial();
        var remap  = new GlyphIdRemap(Array.Empty<ushort>());
        GlyfLocaRebuilder.RebuildResult result = GlyfLocaRebuilder.Rebuild(
            font.GetTableBytes("glyf")!,
            font.Loca!,
            remap,
            font.Glyf);

        Assert.Empty(result.GlyfBytes);
        Assert.Single(result.LocaOffsets);      // numGlyphs+1 = 0+1
        Assert.Equal(0u, result.LocaOffsets[0]);
    }

    [Fact]
    public void FullFontRebuild_LocaIsMonotonicAndCoversFull()
    {
        // Rebuild the entire font as a smoke test; verify loca is monotonic end-to-end.
        SfntFont font   = LoadArial();
        GlyphTable glyf        = font.Glyf!;
        ushort[] allIds      = glyf.Glyphs.Select(g => (ushort)g.Index).ToArray();

        // The closure of all IDs should be all IDs (no new ones appear).
        ushort[] closure = GlyphClosure.Compute(font, allIds);
        var remap   = new GlyphIdRemap(closure);
        GlyfLocaRebuilder.RebuildResult result  = GlyfLocaRebuilder.Rebuild(
            font.GetTableBytes("glyf")!,
            font.Loca!,
            remap,
            glyf);

        Assert.Equal(remap.Count + 1, result.LocaOffsets.Length);
        for (var i = 1; i <= result.NumGlyphs; i++)
        {
            Assert.True(result.LocaOffsets[i] >= result.LocaOffsets[i - 1],
                $"Full-font rebuild: loca not monotonic at {i}");
        }
    }

    // =========================================================================
    // Internal helper — parse component GIDs from raw rebuilt glyf bytes
    // =========================================================================

    /// <summary>
    /// Walk the raw composite glyph bytes starting at <paramref name="glyphOffset"/>
    /// and return the component GlyphIndex values in order.
    /// This mirrors the production code's walk so tests are independent.
    /// </summary>
    private static System.Collections.Generic.List<ushort> ReadCompositeComponentGids(
        byte[] glyfBytes, int glyphOffset)
    {
        const ushort FlagArg1And2AreWords  = 1 << 0;
        const ushort FlagWeHaveAScale      = 1 << 3;
        const ushort FlagMoreComponents    = 1 << 5;
        const ushort FlagWeHaveAnXAndYScale = 1 << 6;
        const ushort FlagWeHaveATwoByTwo   = 1 << 7;

        var result = new System.Collections.Generic.List<ushort>();
        int pos = glyphOffset + 10; // skip the 10-byte header

        while (pos + 4 <= glyfBytes.Length)
        {
            var flags = (ushort)((glyfBytes[pos] << 8) | glyfBytes[pos + 1]);
            pos += 2;

            var gid = (ushort)((glyfBytes[pos] << 8) | glyfBytes[pos + 1]);
            result.Add(gid);
            pos += 2;

            pos += (flags & FlagArg1And2AreWords) != 0 ? 4 : 2;

            if      ((flags & FlagWeHaveATwoByTwo)   != 0) pos += 8;
            else if ((flags & FlagWeHaveAnXAndYScale) != 0) pos += 4;
            else if ((flags & FlagWeHaveAScale)       != 0) pos += 2;

            if ((flags & FlagMoreComponents) == 0) break;
        }

        return result;
    }
}
