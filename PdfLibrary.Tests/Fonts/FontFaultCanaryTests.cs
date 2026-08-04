using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PdfLibrary.Tests.Conformance;
using Xunit;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// Corpus canary: no embedded font program in the GWG corpus may start failing to parse without someone
/// deciding that is acceptable and re-pinning the baseline.
/// <para>This exists because the failure it watches for is invisible by construction. When Type1Table
/// threw on GWG090's embedded Type1C program, EmbeddedFontMetrics set <c>_isCffFont = false</c> and the
/// font rendered through the TrueType path against a <c>glyf</c> table it did not have. Nothing was red.
/// The bug survived months of green runs and was found only by reading bytes.</para>
/// <para>Baseline, not zero-tolerance: a known-and-accepted fault is a row, so this stays usable if it is
/// ever pointed at a corpus containing deliberately-broken programs. Regenerate with
/// <c>PDFLIBRARY_FONT_FAULT_REGEN=1</c>; the run rewrites the baseline and still fails, so a
/// regeneration can never be mistaken for a pass.</para>
/// <para>LocalOnly: needs the sibling <c>../gwg-gos</c> checkout, absent on CI. The diff it relies on is
/// unit-tested separately in <c>FontFaultCompareTests</c>, which does run on CI.</para>
/// </summary>
[Trait("Category", "LocalOnly")]
public class FontFaultCanaryTests
{
    private const string BaselineFileName = "font-program-fault-baseline.txt";
    private const string RegenVariable = "PDFLIBRARY_FONT_FAULT_REGEN";

    private readonly ITestOutputHelper _out;
    public FontFaultCanaryTests(ITestOutputHelper o) => _out = o;

    /// <summary>Walk up to the test project directory so regeneration writes the source file, not the
    /// copy under bin/.</summary>
    private static string BaselinePath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "PdfLibrary.Tests.csproj")))
                return Path.Combine(dir.FullName, "Fonts", BaselineFileName);
        throw new InvalidOperationException("could not locate the PdfLibrary.Tests project directory");
    }

    private static Dictionary<string, string> ReadBaseline(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return map;
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            int tab = line.LastIndexOf('\t');
            if (tab <= 0) continue;
            map[line[..tab]] = line[(tab + 1)..];
        }
        return map;
    }

    [Fact]
    public void No_embedded_font_program_faults_outside_the_baseline()
    {
        string path = BaselinePath();
        Dictionary<string, string> expected = ReadBaseline(path);
        var files = new List<string>(GwgGosHarness.PatchFiles());

        // Read the baseline BEFORE deciding to skip. A populated baseline sitting next to zero discovered
        // files means the gate is checking nothing — that must fail loudly, not skip quietly.
        if (files.Count == 0)
        {
            Assert.True(expected.Count == 0,
                $"gwg-gos corpus not present, but {BaselineFileName} has {expected.Count} committed " +
                "entries. The gate is NOT checking any of them — this must fail, not skip. Restore the " +
                "corpus (or set GWG_GOS) before trusting a green run of this test.");
            Assert.Skip("gwg-gos corpus not present (LocalOnly); baseline is also empty.");
            return;
        }

        var actual = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var programsExamined = 0;
        var unreadable = 0;

        foreach (string file in files)
        {
            try { FontFaultCanary.Scan(file, GwgGosHarness.RelativeKey(file), actual, ref programsExamined); }
            catch (Exception ex)
            {
                unreadable++;
                _out.WriteLine($"unreadable: {GwgGosHarness.RelativeKey(file)}: {ex.GetType().Name}");
            }
        }

        bool regen = Environment.GetEnvironmentVariable(RegenVariable) == "1";
        if (regen)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Embedded font-program parse faults across the GWG corpus.");
            sb.AppendLine("# key = <corpus-relative path>\\t<BaseFont>, value = Stage:ExceptionType");
            sb.AppendLine("# (multiple faults on one program join with '+'; 'MetricsNull' means the font");
            sb.AppendLine("# class swallowed the failure and returned null before any fault list existed).");
            sb.AppendLine("# An EMPTY body is the healthy state: every embedded program parsed cleanly.");
            sb.AppendLine($"# {programsExamined} programs examined across {files.Count} files ({unreadable} unreadable).");
            sb.AppendLine($"# {actual.Count} faulting fonts. Regenerate with {RegenVariable}=1 and review the diff.");
            foreach (KeyValuePair<string, string> kv in actual)
                sb.AppendLine($"{kv.Key}\t{kv.Value}");
            File.WriteAllText(path, sb.ToString());

            Assert.Fail($"{RegenVariable}=1: baseline rewritten with {actual.Count} entries at {path} " +
                        $"({programsExamined} programs examined). Review the diff and re-run without the variable set.");
        }

        // The canary-of-the-canary. With a healthy (empty) baseline, an empty result set is
        // indistinguishable from a walk that silently stopped finding fonts — so coverage is asserted
        // directly. This is the guard doing the real work; note it deliberately replaces the GWG hash
        // gate's `expected.Count > 0` assertion, which cannot apply when zero faults is the goal state.
        Assert.True(programsExamined > 0,
            $"the canary examined ZERO embedded font programs across {files.Count} corpus files. The " +
            "resource walk has regressed, or the corpus is not what it was. An empty result here would " +
            "otherwise read as a clean pass.");

        List<string> problems = FontFaultCanary.Compare(actual, expected);

        _out.WriteLine($"{programsExamined} programs examined across {files.Count} files " +
                       $"({unreadable} unreadable), {actual.Count} faulting, {expected.Count} baselined, " +
                       $"{problems.Count} differences.");

        Assert.True(problems.Count == 0,
            $"embedded font-program faults diverged from {BaselineFileName}:\n" + string.Join("\n", problems) +
            $"\n\nA NEW row means a font program that used to parse no longer does — check the parser " +
            $"before accepting it. A MISSING row means one started parsing, which is usually good news " +
            $"and still needs re-pinning. If these changes are intended, regenerate with " +
            $"{RegenVariable}=1 and review the diff.");
    }
}
