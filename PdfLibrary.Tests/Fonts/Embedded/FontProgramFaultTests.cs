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
            Assert.False(string.IsNullOrEmpty(f.ExceptionType));
            Assert.DoesNotContain(" ", f.ExceptionType); // a type name, not a sentence
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
}
