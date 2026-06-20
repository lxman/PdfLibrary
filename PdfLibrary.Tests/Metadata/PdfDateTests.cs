using System.Globalization;
using PdfLibrary.Metadata;
using Xunit;

namespace PdfLibrary.Tests.Metadata;

public class PdfDateTests
{
    // ── TryParsePdf ──────────────────────────────────────────────────────────

    [Fact]
    public void TryParsePdf_FullDateWithUtcOffset_Succeeds()
    {
        bool ok = PdfDate.TryParsePdf("D:20260620134500+00'00'", out DateTimeOffset dto);
        Assert.True(ok);
        Assert.Equal(2026, dto.Year);
        Assert.Equal(6,    dto.Month);
        Assert.Equal(20,   dto.Day);
        Assert.Equal(13,   dto.Hour);
        Assert.Equal(45,   dto.Minute);
        Assert.Equal(0,    dto.Second);
        Assert.Equal(TimeSpan.Zero, dto.Offset);
    }

    [Fact]
    public void TryParsePdf_FullDateWithPositiveOffset_Succeeds()
    {
        bool ok = PdfDate.TryParsePdf("D:20260620134500+05'30'", out DateTimeOffset dto);
        Assert.True(ok);
        Assert.Equal(TimeSpan.FromMinutes(330), dto.Offset);
    }

    [Fact]
    public void TryParsePdf_FullDateWithNegativeOffset_Succeeds()
    {
        bool ok = PdfDate.TryParsePdf("D:20260620134500-08'00'", out DateTimeOffset dto);
        Assert.True(ok);
        Assert.Equal(TimeSpan.FromHours(-8), dto.Offset);
    }

    [Fact]
    public void TryParsePdf_PartialDateYearOnly_Succeeds()
    {
        bool ok = PdfDate.TryParsePdf("D:2026", out DateTimeOffset dto);
        Assert.True(ok);
        Assert.Equal(2026, dto.Year);
        Assert.Equal(1,    dto.Month);
        Assert.Equal(1,    dto.Day);
    }

    [Fact]
    public void TryParsePdf_PartialDateYearMonth_Succeeds()
    {
        bool ok = PdfDate.TryParsePdf("D:202606", out DateTimeOffset dto);
        Assert.True(ok);
        Assert.Equal(2026, dto.Year);
        Assert.Equal(6,    dto.Month);
        Assert.Equal(1,    dto.Day);
    }

    [Fact]
    public void TryParsePdf_PartialDateYearMonthDay_Succeeds()
    {
        bool ok = PdfDate.TryParsePdf("D:20260620", out DateTimeOffset dto);
        Assert.True(ok);
        Assert.Equal(2026, dto.Year);
        Assert.Equal(6,    dto.Month);
        Assert.Equal(20,   dto.Day);
    }

    [Fact]
    public void TryParsePdf_NoPrefix_Fails()
    {
        bool ok = PdfDate.TryParsePdf("20260620134500+00'00'", out _);
        Assert.False(ok);
    }

    [Fact]
    public void TryParsePdf_Garbage_Fails()
    {
        bool ok = PdfDate.TryParsePdf("not a date", out _);
        Assert.False(ok);
    }

    [Fact]
    public void TryParsePdf_Empty_Fails()
    {
        bool ok = PdfDate.TryParsePdf("", out _);
        Assert.False(ok);
    }

    // ── FormatPdf ─────────────────────────────────────────────────────────────

    [Fact]
    public void FormatPdf_UtcOffset_EmitsCorrectFormat()
    {
        var dto = new DateTimeOffset(2026, 6, 20, 13, 45, 0, TimeSpan.Zero);
        string s = PdfDate.FormatPdf(dto);
        Assert.Equal("D:20260620134500+00'00'", s);
    }

    [Fact]
    public void FormatPdf_PositiveOffset_EmitsCorrectFormat()
    {
        var dto = new DateTimeOffset(2026, 6, 20, 13, 45, 0, TimeSpan.FromMinutes(330));
        string s = PdfDate.FormatPdf(dto);
        Assert.Equal("D:20260620134500+05'30'", s);
    }

    [Fact]
    public void FormatPdf_NegativeOffset_EmitsCorrectFormat()
    {
        var dto = new DateTimeOffset(2026, 6, 20, 13, 45, 0, TimeSpan.FromHours(-8));
        string s = PdfDate.FormatPdf(dto);
        Assert.Equal("D:20260620134500-08'00'", s);
    }

    // ── TryParseIso ───────────────────────────────────────────────────────────

    [Fact]
    public void TryParseIso_FullUtc_Succeeds()
    {
        bool ok = PdfDate.TryParseIso("2026-06-20T13:45:00+00:00", out DateTimeOffset dto);
        Assert.True(ok);
        Assert.Equal(2026, dto.Year);
        Assert.Equal(6,    dto.Month);
        Assert.Equal(20,   dto.Day);
        Assert.Equal(13,   dto.Hour);
        Assert.Equal(45,   dto.Minute);
        Assert.Equal(TimeSpan.Zero, dto.Offset);
    }

    [Fact]
    public void TryParseIso_WithPositiveOffset_Succeeds()
    {
        bool ok = PdfDate.TryParseIso("2026-06-20T13:45:00+05:30", out DateTimeOffset dto);
        Assert.True(ok);
        Assert.Equal(TimeSpan.FromMinutes(330), dto.Offset);
    }

    [Fact]
    public void TryParseIso_Garbage_Fails()
    {
        bool ok = PdfDate.TryParseIso("not-a-date", out _);
        Assert.False(ok);
    }

    // ── FormatIso ─────────────────────────────────────────────────────────────

    [Fact]
    public void FormatIso_UtcOffset_EmitsCorrectFormat()
    {
        var dto = new DateTimeOffset(2026, 6, 20, 13, 45, 0, TimeSpan.Zero);
        string s = PdfDate.FormatIso(dto);
        Assert.Equal("2026-06-20T13:45:00+00:00", s);
    }

    [Fact]
    public void FormatIso_PositiveOffset_EmitsCorrectFormat()
    {
        var dto = new DateTimeOffset(2026, 6, 20, 13, 45, 0, TimeSpan.FromMinutes(330));
        string s = PdfDate.FormatIso(dto);
        Assert.Equal("2026-06-20T13:45:00+05:30", s);
    }

    // ── Round-trips ───────────────────────────────────────────────────────────

    [Fact]
    public void PdfRoundTrip_PreservesDateTimeOffset()
    {
        var original = new DateTimeOffset(2026, 6, 20, 13, 45, 0, TimeSpan.FromHours(2));
        string pdfStr = PdfDate.FormatPdf(original);
        bool ok = PdfDate.TryParsePdf(pdfStr, out DateTimeOffset roundTripped);
        Assert.True(ok);
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void IsoRoundTrip_PreservesDateTimeOffset()
    {
        var original = new DateTimeOffset(2026, 6, 20, 13, 45, 0, TimeSpan.FromMinutes(-330));
        string isoStr = PdfDate.FormatIso(original);
        bool ok = PdfDate.TryParseIso(isoStr, out DateTimeOffset roundTripped);
        Assert.True(ok);
        Assert.Equal(original, roundTripped);
    }

    // ── Culture-invariance ────────────────────────────────────────────────────

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void FormatPdf_UnderNonInvariantCulture_ProducesInvariantOutput(string cultureName)
    {
        CultureInfo prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            var dto = new DateTimeOffset(2026, 6, 20, 13, 45, 0, TimeSpan.Zero);
            string s = PdfDate.FormatPdf(dto);
            Assert.Equal("D:20260620134500+00'00'", s);
        }
        finally { CultureInfo.CurrentCulture = prev; }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void TryParsePdf_UnderNonInvariantCulture_Succeeds(string cultureName)
    {
        CultureInfo prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            bool ok = PdfDate.TryParsePdf("D:20260620134500+00'00'", out DateTimeOffset dto);
            Assert.True(ok);
            Assert.Equal(2026, dto.Year);
        }
        finally { CultureInfo.CurrentCulture = prev; }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void FormatIso_UnderNonInvariantCulture_ProducesInvariantOutput(string cultureName)
    {
        CultureInfo prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            var dto = new DateTimeOffset(2026, 6, 20, 13, 45, 0, TimeSpan.Zero);
            string s = PdfDate.FormatIso(dto);
            Assert.Equal("2026-06-20T13:45:00+00:00", s);
        }
        finally { CultureInfo.CurrentCulture = prev; }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void TryParseIso_UnderNonInvariantCulture_Succeeds(string cultureName)
    {
        CultureInfo prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            bool ok = PdfDate.TryParseIso("2026-06-20T13:45:00+00:00", out DateTimeOffset dto);
            Assert.True(ok);
            Assert.Equal(2026, dto.Year);
        }
        finally { CultureInfo.CurrentCulture = prev; }
    }

    // ── Cross-format conversion ───────────────────────────────────────────────

    [Fact]
    public void PdfToIso_RoundTrip()
    {
        const string pdfDate = "D:20260620134500+00'00'";
        PdfDate.TryParsePdf(pdfDate, out DateTimeOffset dto);
        string iso = PdfDate.FormatIso(dto);
        Assert.Equal("2026-06-20T13:45:00+00:00", iso);
    }
}
