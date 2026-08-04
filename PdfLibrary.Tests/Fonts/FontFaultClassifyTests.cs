using CffTestFixtures;
using PdfLibrary.Fonts.Embedded;
using Xunit;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// What the canary decides to write down for one font's program. Split from the diff tests because this
/// is where the canary's original blind spot lived: it only recorded failure-by-exception, so a program
/// that failed by ABSENCE — truncated, every table offset out of range, no reader ever entered, nothing
/// thrown — scored clean.
/// </summary>
public class FontFaultClassifyTests
{
    /// <summary>A structurally valid sfnt header declaring zero tables. Parses fine; yields nothing.
    /// This is the shape a truncated font degrades to, reduced to twelve bytes.</summary>
    private static byte[] EmptySfnt() =>
    [
        0x00, 0x01, 0x00, 0x00, // sfntVersion: TrueType outlines
        0x00, 0x00,             // numTables: 0
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00 // searchRange / entrySelector / rangeShift
    ];

    [Fact]
    public void AProgramThatFailsSilently_IsReportedRatherThanScoredClean()
    {
        var metrics = new EmbeddedFontMetrics(EmptySfnt());

        // The exact hole: unusable, but nothing threw, so there is no fault to record.
        Assert.False(metrics.IsValid);
        Assert.Empty(metrics.Faults);

        Assert.Equal(FontFaultCanary.InvalidNoFaultValue, FontFaultCanary.Classify(metrics));
    }

    [Fact]
    public void ACleanProgram_IsNotReported()
    {
        var metrics = new EmbeddedFontMetrics(MinimalCff.Build(charsetOperand: null, numGlyphs: 4));

        Assert.Null(FontFaultCanary.Classify(metrics));
    }

    [Fact]
    public void AProgramThatThrew_IsReportedByItsFaultsNotAsInvalidNoFault()
    {
        byte[] whole = MinimalCff.Build(charsetOperand: null, numGlyphs: 4);
        var metrics = new EmbeddedFontMetrics(whole[..(whole.Length / 2)]);

        string? row = FontFaultCanary.Classify(metrics);

        Assert.NotNull(row);
        Assert.Contains("RawCff:", row);
        Assert.NotEqual(FontFaultCanary.InvalidNoFaultValue, row);
    }

    [Fact]
    public void NullMetrics_AreReportedAsMetricsNull()
    {
        Assert.Equal(FontFaultCanary.MetricsNullValue, FontFaultCanary.Classify(null));
    }
}
