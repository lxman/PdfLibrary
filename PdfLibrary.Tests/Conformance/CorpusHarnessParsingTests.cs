namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Unit tests for the veraPDF corpus file-name parser. These need no corpus on disk, so they run on CI
/// (unlike the LocalOnly oracle in <see cref="CorpusOracleTests"/>) and lock the filename convention the
/// harness depends on.
/// </summary>
public class CorpusHarnessParsingTests
{
    [Theory]
    [InlineData("veraPDF test suite 6-1-13-t09-fail-e.pdf", "6.1.13", 9, 'e', false)]
    [InlineData("veraPDF test suite 6-1-3-t01-fail-a.pdf", "6.1.3", 1, 'a', false)]
    [InlineData("veraPDF test suite 6-2-11-4-1-t01-fail-a.pdf", "6.2.11.4.1", 1, 'a', false)]
    [InlineData("veraPDF test suite 6-6-4-t02-pass-a.pdf", "6.6.4", 2, 'a', true)]
    [InlineData("veraPDF test suite 6-8-t02-pass-d.pdf", "6.8", 2, 'd', true)]
    public void Parses_clause_test_variant_and_verdict(string name, string clause, int test, char variant, bool pass)
    {
        Assert.True(CorpusHarness.TryParseFileName(name, out string c, out int t, out char v, out bool p));
        Assert.Equal(clause, c);
        Assert.Equal(test, t);
        Assert.Equal(variant, v);
        Assert.Equal(pass, p);
    }

    [Theory]
    [InlineData("conformant-pdfa2b.pdf")]              // our vendored fixture — not a corpus file
    [InlineData("veraPDF test suite 6-1-3-t01.pdf")]   // missing verdict + variant
    [InlineData("random.pdf")]
    [InlineData("6-1-3-fail-a.pdf")]                    // missing the tNN segment
    public void Rejects_names_without_the_corpus_pattern(string name)
    {
        Assert.False(CorpusHarness.TryParseFileName(name, out _, out _, out _, out _));
    }
}
