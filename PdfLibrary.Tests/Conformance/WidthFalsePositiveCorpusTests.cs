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

    [Fact]
    public void Reproducer_page_extracts_spacing_accents()
    {
        // Issue 28: the Times specimen accent row (the line directly beneath the punctuation row
        // asserted by Reproducer_page_extracts_curly_quotes, same four style variants) previously
        // extracted as invisible C1 control characters, not visible Latin-1 letters. Root cause,
        // measured 2026-08-15 by re-running this probe with ONLY the GlyphList commit (316a12b)
        // applied and diffing the resulting codepoint stream against the full three-commit fix:
        // the two streams are byte-identical. StandardEncoding's Annex D.2 upper-band fix (issue
        // 28's second commit) has ZERO effect on this row — the row's codes are set via the
        // font's /Differences array (SetCharacterName), which resolves through
        // GlyphList.GetUnicode(charName) directly; it never reaches the StandardEncoding base
        // table PdfFontEncoding.cs falls back to. Pre-fix, the eight new accent names
        // (circumflex/tilde/breve/dotaccent/ring/hungarumlaut/ogonek/caron) had no GlyphList
        // entry, so PdfFontEncoding.GetUnicode's final fallback ("character code as-is, Latin-1")
        // fired on their /Differences code points — which land in the C1 control range
        // (U+0093/0094/0096/0097/009A/009D/009E/009F), not on any visible letter. Counts pinned
        // against pdftotext (WSL poppler) on page index 788, which matches exactly: all twelve
        // accent codepoints occur 4 times each (one per Times style variant: Roman/Italic/Bold/
        // BoldItalic).
        string? corpus = Corpus();
        Assert.SkipWhen(corpus is null, $"corpus not present at {DefaultCorpus} (LocalOnly)");

        using var doc = PdfDocument.Load(File.OpenRead(
            Path.Combine(corpus!, "Postscript Language Reference Manual.pdf")));
        string text = doc.GetPage(788)!.ExtractText();

        Assert.False(string.IsNullOrWhiteSpace(text), "extraction returned nothing — vacuous");
        Assert.Equal(4, text.Count(c => c == '\u02C7'));  // caron
        Assert.Equal(4, text.Count(c => c == '\u02D8'));  // breve
        Assert.Equal(4, text.Count(c => c == '\u02DA'));  // ring
        Assert.Equal(4, text.Count(c => c == '\u02C6'));  // circumflex
        Assert.Equal(4, text.Count(c => c == '\u02DC'));  // small tilde
        Assert.Equal(4, text.Count(c => c == '\u02D9'));  // dot above
        Assert.Equal(4, text.Count(c => c == '\u02DD'));  // hungarumlaut
        Assert.Equal(4, text.Count(c => c == '\u02DB'));  // ogonek
        Assert.Equal(4, text.Count(c => c == '\u00A8'));  // dieresis
        Assert.Equal(4, text.Count(c => c == '\u00B4'));  // acute
        Assert.Equal(4, text.Count(c => c == '\u00AF'));  // macron
        Assert.Equal(4, text.Count(c => c == '\u00B8'));  // cedilla
        // C1 control leakage from the OLD defect must be gone: pre-fix, each style variant's
        // accent row decoded four of its glyphs (circumflex/ring/tilde/breve) to these invisible
        // control codepoints instead (measured via a full-page codepoint diff against the pre-fix
        // files at 38a3f32, 2026-08-15).
        Assert.DoesNotContain('\u009E', text);
        Assert.DoesNotContain('\u0093', text);
        Assert.DoesNotContain('\u009A', text);
        Assert.DoesNotContain('\u0094', text);
    }
}
