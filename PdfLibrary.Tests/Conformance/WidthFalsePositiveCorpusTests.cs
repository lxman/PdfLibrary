using System.Linq;
using PdfLibrary.Conformance;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Corpus gates for the issue 24/25/26 fixes (spec: 2026-08-15-font-width-encoding-defects).
/// DoD is oracle-anchored: across the five local-708 documents whose font-program width sub-test
/// fired pre-fix, findings reduce to the TWO veraPDF also flags (the PowerBASIC files, whose OTTO
/// subsets genuinely carry an all-1000 hmtx — confirmed by reading the raw table independently).
/// LocalOnly: the corpus exists only on the dev box (and, mounted, on self-hosted runners — the
/// trait is the only thing keeping this out of CI; see .github/workflows filters).
/// </summary>
[Trait("Category", "LocalOnly")]
public class WidthFalsePositiveCorpusTests
{
    private const string CorpusVariable = "PDFLIBRARY_LOCAL708_CORPUS";
    private const string DefaultCorpus = @"D:\PdfCorpora\real-world\local-708";

    private static string? Corpus()
    {
        string root = Environment.GetEnvironmentVariable(CorpusVariable) ?? DefaultCorpus;
        return Directory.Exists(root) ? root : null;
    }

    private static int WidthFindings(string path)
    {
        PreflightResult result = Preflighter.Check(path, ConformanceProfile.PdfA2b);
        return result.Findings.Count(f =>
            f.RuleId == "font-program" && ParitySnapshot.ClauseKey(f.Clause) == "6.2.11.5");
    }

    [Theory]
    // The three false positives — each was a different engine defect, none a rule defect:
    [InlineData("XS Benefits overview.pdf", 0)]                          // issue 24 (/W indirect)
    [InlineData("Postscript Language Reference Manual.pdf", 0)]          // issue 25 (StandardEncoding)
    [InlineData("Visual Studio Icon Library - Common Elements.pdf", 0)]  // issue 26 (zero advance)
    public void False_positive_documents_report_no_width_finding(string file, int expected)
    {
        string? corpus = Corpus();
        // The corpus IS present on this development machine, so SkipWhen firing here is a
        // FAILURE of the run, not a legitimate "nothing to check" pass.
        Assert.SkipWhen(corpus is null, $"corpus not present at {DefaultCorpus} (LocalOnly)");
        Assert.Equal(expected, WidthFindings(Path.Combine(corpus!, file)));
    }

    [Theory]
    // The two genuine positives veraPDF agrees on. They anchor the gate against vacuous passes:
    // if the width sub-test silently died, these would go to zero and fail.
    [InlineData("PowerBASIC Compiler for Windows v10.0.pdf")]
    [InlineData("PowerBASIC Console Compiler v6.0.pdf")]
    public void Genuine_documents_still_report_a_width_finding(string file)
    {
        string? corpus = Corpus();
        Assert.SkipWhen(corpus is null, $"corpus not present at {DefaultCorpus} (LocalOnly)");
        Assert.True(WidthFindings(Path.Combine(corpus!, file)) >= 1,
            $"{file} lost its width finding — veraPDF flags it (degenerate all-1000 hmtx), so a "
            + "zero here means the width sub-test regressed, not that the document got better.");
    }

    [Fact]
    public void Reproducer_page_extracts_curly_quotes()
    {
        // Gate 4 (issue 25). Page index and counts measured 2026-08-15, cross-checked against
        // poppler `pdftotext -f 789 -l 789` (WSL), which matches the fixed engine's counts exactly.
        //
        // The brief's own probe (lowest page containing ANY curly quote post-fix) turned out not
        // to isolate the bug: on most early pages, quoteright/quoteleft reach the extractor through
        // a font's explicit /Differences array (which sets the Unicode via the AGL directly,
        // bypassing the buggy StandardEncoding fallback table entirely) — confirmed by re-running
        // the same probe against the pre-fix files (`git checkout 30c7166 -- .../PdfFontEncoding.cs`
        // et al.), where those early pages already extracted curly quotes correctly. Page index 788
        // (page 789, 1-based) is the LOWEST page where the revert actually changes the output —
        // pre-fix it reports straight=8/backtick=8/curly=0, post-fix curly=4/4, straight=4/4.
        //
        // Page 788 is Appendix E.1's font-specimen chart, showing the character map for the four
        // Times style variants (Roman, Italic, Bold, BoldItalic). Each variant's punctuation row
        // legitimately contains BOTH the curly quoteright(U+2019)/quoteleft(U+2018) glyphs AND the
        // distinct quotesingle(U+0027)/grave(U+0060) glyphs side by side — 4 style variants x 1 of
        // each = 4/4/4/4. Pre-fix, StandardEncoding's ASCII-fallback table collapsed quoteright/
        // quoteleft into the same codepoints as quotesingle/grave, so all 8 "quote-shaped"
        // occurrences per pair landed on the straight forms; the fix separates them back into their
        // correct, independently poppler-verified glyphs. Per the brief's own fallback for a page
        // with legitimate non-curly quote characters, this gate asserts exact measured counts for
        // all four codepoints instead of DoesNotContain — both straight forms are real content here,
        // not extraction defects.
        string? corpus = Corpus();
        Assert.SkipWhen(corpus is null, $"corpus not present at {DefaultCorpus} (LocalOnly)");

        using var doc = PdfDocument.Load(File.OpenRead(
            Path.Combine(corpus!, "Postscript Language Reference Manual.pdf")));
        string text = doc.GetPage(788)!.ExtractText();

        Assert.False(string.IsNullOrWhiteSpace(text), "extraction returned nothing — vacuous");
        Assert.Equal(4, text.Count(c => c == '\u2019'));  // quoteright
        Assert.Equal(4, text.Count(c => c == '\u2018'));  // quoteleft
        Assert.Equal(4, text.Count(c => c == '\u0027'));  // quotesingle — genuinely distinct glyph
        Assert.Equal(4, text.Count(c => c == '\u0060'));  // grave — genuinely distinct glyph
    }
}
