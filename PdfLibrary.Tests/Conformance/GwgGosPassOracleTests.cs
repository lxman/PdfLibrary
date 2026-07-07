using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfLibrary.Conformance;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 9 pass-oracle: every PDF/X-4 file in the Ghent PDF Output Suite is a real conformant file, so the
/// preflight must not reject it. Mirrors the veraPDF pass-oracle (<see cref="CorpusOracleTests"/>) — a rule
/// subset can only under-report, so a flagged conformant file is a real false positive. The one allowed
/// exception is a documented font-embedding baseline. LocalOnly (the GOS checkout is absent on CI).
/// </summary>
[Trait("Category", "LocalOnly")]
public class GwgGosPassOracleTests(ITestOutputHelper output)
{
    /// <summary>
    /// Conformant PDF/X-4 files the current rule set still flags. Empty since the FontEmbeddingRule moved
    /// onto the rendering resource-tree walk (<see cref="ConformanceContext.ReferencedFonts"/>) — all eight
    /// former GOS false positives (unreferenced non-embedded fonts) now pass. Kept as the extension point.
    /// </summary>
    private static readonly IReadOnlySet<string> KnownFontFalsePositives = new HashSet<string>();

    [Fact]
    public void Pdfx4_files_conform_except_the_known_font_baseline()
    {
        Assert.SkipUnless(GwgGosHarness.IsAvailable, "gwg-gos not present at ../gwg-gos");

        var unexpected = new List<string>();
        var staleBaseline = new HashSet<string>(KnownFontFalsePositives);
        int total = 0;

        foreach (string path in GwgGosHarness.PdfX4Files())
        {
            total++;
            string name = Path.GetFileName(path);

            bool conforms;
            List<string> rules;
            try
            {
                PreflightResult result = Preflighter.Check(path, ConformanceProfile.PdfX4);
                conforms = result.Conforms;
                rules = result.Errors.Select(e => e.RuleId).Distinct().ToList();
            }
            catch (Exception ex)
            {
                conforms = false;
                rules = [$"load-error:{ex.GetType().Name}"];
            }

            if (conforms)
                continue;

            staleBaseline.Remove(name);
            if (!KnownFontFalsePositives.Contains(name))
                unexpected.Add($"{name} [{string.Join(",", rules)}]");
        }

        output.WriteLine($"checked {total} PDF/X-4 GOS files; {unexpected.Count} unexpected, {staleBaseline.Count} stale baseline");

        Assert.True(unexpected.Count == 0,
            $"{unexpected.Count} conformant PDF/X-4 file(s) newly flagged: {string.Join("; ", unexpected)}");
        Assert.True(staleBaseline.Count == 0,
            $"{staleBaseline.Count} baseline entr(y/ies) now pass — remove: {string.Join(", ", staleBaseline)}");
    }
}
