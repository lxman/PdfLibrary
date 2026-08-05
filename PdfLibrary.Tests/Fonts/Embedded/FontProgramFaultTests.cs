using CffTestFixtures;
using PdfLibrary.Fonts.Embedded;
using Xunit;

namespace PdfLibrary.Tests.Fonts.Embedded;

/// <summary>
/// <see cref="EmbeddedFontMetrics"/> swallows every font-program parse failure into a fallback. These
/// tests pin the fact that it now also RECORDS each one. The recording is what makes the failure
/// assertable — the Type1C CFF charset bug (engine 6564363) survived for months because a bare
/// <c>catch { _isCffFont = false; }</c> made a broken parser look healthy.
/// <para>These are ordinary unit tests, NOT LocalOnly: they build their fixtures in memory.</para>
/// </summary>
public class FontProgramFaultTests
{
    /// <summary>A structurally valid raw CFF, cut short so the parser runs off the end.</summary>
    private static byte[] TruncatedRawCff()
    {
        byte[] whole = MinimalCff.Build(charsetOperand: null, numGlyphs: 4);
        return whole[..(whole.Length / 2)];
    }

    [Fact]
    public void CleanProgram_RecordsNoFaults()
    {
        var metrics = new EmbeddedFontMetrics(MinimalCff.Build(charsetOperand: null, numGlyphs: 4));

        Assert.True(metrics.IsValid);
        Assert.Empty(metrics.Faults);
    }

    [Fact]
    public void TruncatedRawCff_RecordsARawCffFault()
    {
        var metrics = new EmbeddedFontMetrics(TruncatedRawCff());

        Assert.Contains(metrics.Faults, f => f.Stage == FontProgramStage.RawCff);
    }

    [Fact]
    public void TruncatedRawCff_FallbackBehaviourIsUnchanged()
    {
        // The point of the whole exercise: recording must not alter what the swallow does.
        var metrics = new EmbeddedFontMetrics(TruncatedRawCff());

        Assert.False(metrics.IsCffFont);
        Assert.False(metrics.IsValid);
    }

    [Fact]
    public void FaultsNeverCarryTheExceptionMessage()
    {
        // Messages vary by runtime and locale; a committed baseline keyed on them would churn.
        var metrics = new EmbeddedFontMetrics(TruncatedRawCff());

        Assert.NotEmpty(metrics.Faults);
        Assert.All(metrics.Faults, f =>
        {
            Assert.False(string.IsNullOrEmpty(f.Detail));
            Assert.DoesNotContain(" ", f.Detail); // a short tag, not a sentence
        });
    }

    [Fact]
    public void GarbageSfntProgram_RecordsASfntDirectoryFault()
    {
        // Does not start 0x01 0x00, so it skips the raw-CFF arm and goes straight to the sfnt reader.
        var garbage = new byte[64];
        garbage[0] = 0xDE;
        garbage[1] = 0xAD;

        var metrics = new EmbeddedFontMetrics(garbage);

        Assert.Contains(metrics.Faults, f => f.Stage == FontProgramStage.SfntDirectory);
        Assert.False(metrics.IsValid);
        Assert.Equal(1000, metrics.UnitsPerEm); // the documented fallback, unchanged
    }

    [Fact]
    public void OmittedCharsetOperator_IsNotAFault()
    {
        // The Type1C shape from 6564363. TN #5176 Table 9 defaults charset to 0, so this is a VALID
        // program and must record nothing. This is the "stays fixed" guard: if the charset regression
        // ever returns, this test goes red with a CffTable/RawCff fault.
        var metrics = new EmbeddedFontMetrics(MinimalCff.Build(charsetOperand: null, numGlyphs: 4));

        Assert.Empty(metrics.Faults);
        Assert.True(metrics.IsCffFont);
    }

    [Fact]
    public void FaultsIsNeverNull()
    {
        Assert.NotNull(new EmbeddedFontMetrics(MinimalCff.Build(charsetOperand: null, numGlyphs: 4)).Faults);
        Assert.NotNull(new EmbeddedFontMetrics([], length1: 0, length2: 0).Faults);
    }

    // ---- Per-stage coverage via synthetic sfnts (see MinimalSfnt) ----------------------------
    // Each asserts BOTH that the stage is recorded AND that the documented fallback is unchanged.
    // The second assertion is the load-bearing one: this whole mechanism is only defensible if
    // recording a fault changed no behaviour.

    [Fact]
    public void ShortHeadTable_RecordsAHeadFaultAndKeepsTheUnitsPerEmFallback()
    {
        var metrics = new EmbeddedFontMetrics(MinimalSfnt.Build(("head", MinimalSfnt.TooShort())));

        Assert.Contains(metrics.Faults, f => f.Stage == FontProgramStage.Head);
        Assert.Equal(1000, metrics.UnitsPerEm); // documented "Fallback default"
        Assert.False(metrics.IsValid);
    }

    [Fact]
    public void ShortMaxpTable_RecordsAMaxPFaultAndLeavesNumGlyphsZero()
    {
        var metrics = new EmbeddedFontMetrics(MinimalSfnt.Build(("maxp", MinimalSfnt.TooShort())));

        Assert.Contains(metrics.Faults, f => f.Stage == FontProgramStage.MaxP);
        Assert.Equal(0, metrics.NumGlyphs);
    }

    [Fact]
    public void ShortHheaTable_RecordsAnHheaFaultAndLeavesMetricsAtZero()
    {
        var metrics = new EmbeddedFontMetrics(MinimalSfnt.Build(("hhea", MinimalSfnt.TooShort())));

        Assert.Contains(metrics.Faults, f => f.Stage == FontProgramStage.Hhea);
        Assert.Equal(0, metrics.Ascender);
        Assert.Equal(0, metrics.Descender);
        Assert.Equal(0, metrics.NumberOfHMetrics);
    }

    [Fact]
    public void ShortNameTable_RecordsANameFaultAndLeavesNamesNull()
    {
        var metrics = new EmbeddedFontMetrics(MinimalSfnt.Build(("name", MinimalSfnt.TooShort())));

        Assert.Contains(metrics.Faults, f => f.Stage == FontProgramStage.Name);
        Assert.Null(metrics.FamilyName);
        Assert.Null(metrics.PostScriptName);
    }

    [Fact]
    public void GarbageCmapTable_RecordsACmapFaultAndLeavesLookupsAtNotdef()
    {
        // A 4-byte cmap returns CLEANLY — the reader never runs off the end. cmap only throws on
        // garbage CONTENT. head/maxp/hhea/name fail the opposite way. Do not "simplify" this to
        // TooShort(): the test would then pass while asserting nothing.
        var metrics = new EmbeddedFontMetrics(MinimalSfnt.Build(("cmap", MinimalSfnt.Garbage(64))));

        Assert.Contains(metrics.Faults, f => f.Stage == FontProgramStage.Cmap);
        Assert.False(metrics.HasCmapTable);
        Assert.Equal(0, metrics.GetGlyphId(65));
    }

    [Fact]
    public void ShortCmapTable_RecordsNothing_PinningTheAsymmetry()
    {
        // The counterpart guard for the comment above. If a future parser change makes a short cmap
        // throw, this test goes red and tells the next reader the asymmetry is gone.
        var metrics = new EmbeddedFontMetrics(MinimalSfnt.Build(("cmap", MinimalSfnt.TooShort())));

        Assert.DoesNotContain(metrics.Faults, f => f.Stage == FontProgramStage.Cmap);
    }

    [Fact]
    public void BrokenLocaTable_RecordsAGlyfLocaFaultOnlyAfterAnOutlineIsRequested()
    {
        // loca/glyf load lazily, so the fault cannot exist until something asks for an outline.
        // Requires a parseable head (all-zero parses) and a maxp with NumGlyphs > 0, or
        // LoadGlyphTables returns before reaching the loca reader.
        var metrics = new EmbeddedFontMetrics(MinimalSfnt.Build(
            ("head", MinimalSfnt.ZeroHead()),
            ("maxp", MinimalSfnt.Maxp(4)),
            ("loca", MinimalSfnt.TooShort()),
            ("glyf", MinimalSfnt.Garbage(16))));

        Assert.DoesNotContain(metrics.Faults, f => f.Stage == FontProgramStage.GlyfLoca);

        Assert.Null(metrics.GetGlyphOutline(0));

        Assert.Contains(metrics.Faults, f => f.Stage == FontProgramStage.GlyfLoca);
    }

    [Fact]
    public void BrokenCffTableInsideAnSfnt_RecordsACffTableFaultAndFallsBackToNonCff()
    {
        // The stage that hid the Type1C charset bug for months. An OpenType/CFF wrapper whose
        // 'CFF ' payload is truncated: the sfnt directory parses, the CFF parser throws, and the
        // font silently becomes "not a CFF font" — after which GlyphPathService sends it down the
        // TrueType path to read a glyf table it does not have, and draws nothing.
        byte[] wholeCff = MinimalCff.Build(charsetOperand: null, numGlyphs: 4);
        byte[] truncatedCff = wholeCff[..(wholeCff.Length / 2)];

        var metrics = new EmbeddedFontMetrics(MinimalSfnt.Build(("CFF ", truncatedCff)));

        Assert.Contains(metrics.Faults, f => f.Stage == FontProgramStage.CffTable);
        Assert.False(metrics.IsCffFont);      // the silent-success fallback, unchanged
        Assert.False(metrics.IsValid);
        Assert.Null(metrics.GetCffGlyphOutlineDirect(0));
    }

    [Fact]
    public void IntactCffTableInsideAnSfnt_RecordsNothing()
    {
        // Scope guard: the fault must be caused by the breakage, not by the sfnt wrapper itself.
        // Without this, the test above would pass even if MinimalSfnt produced a wrapper the CFF
        // reader could never parse — the fixture-passes-for-the-wrong-reason failure mode.
        byte[] wholeCff = MinimalCff.Build(charsetOperand: null, numGlyphs: 4);

        var metrics = new EmbeddedFontMetrics(MinimalSfnt.Build(("CFF ", wholeCff)));

        Assert.DoesNotContain(metrics.Faults, f => f.Stage == FontProgramStage.CffTable);
        Assert.True(metrics.IsCffFont);
    }

    [Fact]
    public void ZeroHeadTable_ParsesButYieldsUnitsPerEmZero_WhichIsClampedAndRecorded()
    {
        // A 54-byte all-zero head parses SUCCESSFULLY — nothing throws, so before this clamp there
        // was no fault and IsValid stayed true, while UnitsPerEm came out 0.
        var metrics = new EmbeddedFontMetrics(MinimalSfnt.Build(("head", MinimalSfnt.ZeroHead())));

        Assert.Equal(1000, metrics.UnitsPerEm);
        Assert.Contains(metrics.Faults, f => f.Stage == FontProgramStage.Head && f.Detail == "UnitsPerEmZero");
    }

    [Fact]
    public void AClampedUnitsPerEm_MakesScaleToUserUnitsFinite()
    {
        // Demonstrates the clamp working through one specific consumer, ScaleToUserUnits — which
        // happens to be the one member with no production caller (only this test calls it). It does
        // NOT prove the clamp protects production code; that is what UnitsPerEm's own
        // Assert.Equal(1000, ...) above already covers directly, for every consumer at once. Kept
        // because it is still a genuine, cheap check that the division itself behaves once clamped.
        var metrics = new EmbeddedFontMetrics(MinimalSfnt.Build(("head", MinimalSfnt.ZeroHead())));

        double scaled = metrics.ScaleToUserUnits(500, 12.0);

        Assert.True(double.IsFinite(scaled), $"expected a finite scale, got {scaled}");
    }

    [Fact]
    public void AHealthyProgram_IsNotClampedAndRecordsNoUnitsPerEmFault()
    {
        // Scope guard: the clamp must fire on zero alone, never blanket.
        var metrics = new EmbeddedFontMetrics(MinimalCff.Build(charsetOperand: null, numGlyphs: 4));

        Assert.Equal(1000, metrics.UnitsPerEm); // from the CFF FontMatrix default, not the clamp
        Assert.DoesNotContain(metrics.Faults, f => f.Detail == "UnitsPerEmZero");
    }

    [Fact]
    public void AHealthyUnitsPerEm_PassesThroughTheClampUnchangedAndRecordsNothing()
    {
        // The pass-through half of the clamp, which nothing else on CI exercises: without this,
        // deleting the helper's `if (parsed != 0) return parsed;` guard would leave the suite green
        // while every font in the corpus silently rescaled to 1000 units per em.
        var metrics = new EmbeddedFontMetrics(MinimalSfnt.Build(("head", MinimalSfnt.Head(2048))));

        Assert.Equal(2048, metrics.UnitsPerEm);
        Assert.DoesNotContain(metrics.Faults, f => f.Detail == "UnitsPerEmZero");
    }
}
