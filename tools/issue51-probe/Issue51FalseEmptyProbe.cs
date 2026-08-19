using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Content;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// THROWAWAY measurement harness for tracker issue 51 — DELETE once the numbers are recorded.
///
/// <para>Sizes the false-empty <c>UsedCodes</c> population across the real-world corpora. The usage
/// walk behind <see cref="ConformanceContext.UsedTextGlyphs"/> visits page content and Form XObjects
/// only; the discovery walk (<see cref="ConformanceContext.ReferencedFonts"/>) additionally reaches
/// annotation appearance streams, tiling patterns, Type3 CharProc resources and ExtGState /Font.
/// A font drawn ONLY through one of those four therefore has an empty (or short) code set even
/// though it renders.</para>
///
/// <para>The annotation-AP path — the one with the proven corruption in
/// <c>BuildReplacement</c> — is measured EXACTLY, by running the same collector over every AP
/// stream and diffing against the narrow set. The other three are counted structurally only
/// (does the document contain the shape at all), which is an UPPER BOUND, not a confirmed draw.</para>
/// </summary>
[Trait("Category", "LocalOnly")]
public class Issue51FalseEmptyProbe
{
    private static readonly string[] Corpora =
    [
        @"D:\PdfCorpora\real-world\local-708",
        @"D:\PdfCorpora\real-world\cc-main-2021-31-sample",
    ];

    private sealed record DocResult(
        string Corpus,
        string File,
        int FontsTotal,
        int NarrowEmptyFonts,
        int ApOnlyFonts,       // narrow set absent/empty, AP draws codes  -> FULLY false-empty
        int ApExtendedFonts,   // narrow set non-empty, AP adds MORE codes -> PARTIAL (the corruption case)
        int ApExtraCodes,
        int ApOnlyComposite,   // of ApOnlyFonts, how many are Type0 — the only kind replacement rewrites
        int ApExtendedComposite,
        // The issue-44 filter risk: an AP-drawn composite font with EMPTY UsedCodes that shares its
        // ProgramHolderId with a page-drawn font. ExpandHolderGroup drops it from the merge group, the
        // shared program is rewritten without its codes, and its glyphs regress to .notdef.
        int SharedHolderRisk,
        string RiskDetail,
        bool HasType3,
        bool HasPatternFont,
        bool HasExtGStateFont,
        string? Error);

    [Fact]
    public void Measure()
    {
        var results = new ConcurrentBag<DocResult>();

        foreach (string corpus in Corpora)
        {
            Assert.True(Directory.Exists(corpus), $"corpus missing: {corpus}");
            string[] files = Directory.GetFiles(corpus, "*.pdf", SearchOption.TopDirectoryOnly);

            Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 8 }, file =>
            {
                try { results.Add(Probe(corpus, file)); }
                catch (Exception ex)
                {
                    results.Add(new DocResult(corpus, Path.GetFileName(file),
                        0, 0, 0, 0, 0, 0, 0, 0, "", false, false, false, ex.GetType().Name));
                }
            });
        }

        WriteReport([.. results]);
    }

    private static DocResult Probe(string corpus, string file)
    {
        using PdfDocument doc = PdfDocument.Load(new FileStream(file, FileMode.Open, FileAccess.Read), string.Empty);
        var ctx = new ConformanceContext(doc, ConformanceProfile.PdfA2b);

        // Narrow: what the engine believes is drawn today. Keyed by the font's OBJECT identity, not by
        // PdfFont reference — every PdfResources instance mints its own PdfFont wrapper for the same
        // underlying dictionary, so reference identity cannot join across two walks (and inflated the
        // first run of this probe past the document's own font count).
        var narrow = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        foreach (UsedFontCodes usage in ctx.UsedTextGlyphs)
        {
            if (!narrow.TryGetValue(Key(usage.Font), out HashSet<int>? set))
                narrow[Key(usage.Font)] = set = [];
            set.UnionWith(usage.Codes);
        }

        // Wide (AP only): the same collector, run over every annotation appearance stream.
        (Dictionary<string, HashSet<int>> ap, HashSet<string> composite) = CollectFromAppearances(ctx, doc);

        var apOnly = 0;
        var apExtended = 0;
        var apExtraCodes = 0;
        var apOnlyComposite = 0;
        var apExtendedComposite = 0;
        foreach ((string key, HashSet<int> codes) in ap)
        {
            if (codes.Count == 0)
                continue;

            if (!narrow.TryGetValue(key, out HashSet<int>? seen) || seen.Count == 0)
            {
                apOnly++;
                apExtraCodes += codes.Count;
                if (composite.Contains(key)) apOnlyComposite++;
                continue;
            }

            int extra = codes.Count(c => !seen.Contains(c));
            if (extra <= 0)
                continue;

            apExtended++;
            apExtraCodes += extra;
            if (composite.Contains(key)) apExtendedComposite++;
        }

        // The question issue 44's filter actually turns on: does a falsely-undrawn composite font share
        // its program holder with a genuinely-drawn one? Only worth the inventory build when one exists.
        var sharedHolderRisk = 0;
        List<string> riskDetail = [];
        if (apOnlyComposite > 0)
        {
            IReadOnlyList<FontInventoryEntry> inv = FontInventory.Read(doc);
            HashSet<int> apComposite =
            [
                .. composite.Where(k => k.StartsWith("obj:", StringComparison.Ordinal))
                    .Select(k => int.Parse(k[4..]))
            ];

            foreach (FontInventoryEntry e in inv)
            {
                if (e.UsedCodes.Count > 0 || !apComposite.Contains(e.Id.ObjectNumber))
                    continue;
                if (e.ProgramHolderId is not { } holder)
                    continue;

                bool sharedWithDrawn = inv.Any(o => !ReferenceEquals(o, e)
                    && o.UsedCodes.Count > 0
                    && o.ProgramHolderId?.ObjectNumber == holder.ObjectNumber);

                if (!sharedWithDrawn)
                    continue;

                sharedHolderRisk++;
                riskDetail.Add($"font obj {e.Id.ObjectNumber} -> holder {holder.ObjectNumber}");
            }
        }

        int narrowEmpty = ctx.ReferencedFonts.Count - narrow.Count(kv => kv.Value.Count > 0);

        return new DocResult(
            Path.GetFileName(corpus), Path.GetFileName(file),
            ctx.ReferencedFonts.Count, narrowEmpty,
            apOnly, apExtended, apExtraCodes, apOnlyComposite, apExtendedComposite,
            sharedHolderRisk, string.Join("; ", riskDetail),
            HasType3(ctx, doc), HasPatternFont(ctx, doc), HasExtGStateFont(ctx, doc),
            Error: null);
    }

    // ── the AP walk, mirroring ReferencedFontWalker.WalkAppearance ────────────────────────────────

    /// <summary>The identity every real consumer dedups on — see <c>FontProgramRule.DedupKey</c>:
    /// object number for an indirect dictionary, base-font name for a direct one (which has no
    /// object identity of its own).</summary>
    private static string Key(PdfFont font) =>
        font.FontDictionary.IsIndirect
            ? $"obj:{font.FontDictionary.ObjectNumber}"
            : $"name:{font.BaseFont}";

    private static (Dictionary<string, HashSet<int>> Codes, HashSet<string> Composite) CollectFromAppearances(
        ConformanceContext ctx, PdfDocument doc)
    {
        var merged = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        var composite = new HashSet<string>(StringComparer.Ordinal);

        foreach (PdfDictionary annot in ctx.Annotations)
        {
            if (Resolve(doc, annot.Get("AP")) is not PdfDictionary appearance)
                continue;

            foreach (PdfObject state in appearance.Values) // /N, /D, /R
            {
                switch (Resolve(doc, state))
                {
                    case PdfStream stream:
                        Run(stream, merged, composite, doc);
                        break;
                    case PdfDictionary subStates: // per-state appearances (button on/off)
                        foreach (PdfObject sub in subStates.Values)
                            if (Resolve(doc, sub) is PdfStream subStream)
                                Run(subStream, merged, composite, doc);
                        break;
                }
            }
        }

        return (merged, composite);
    }

    private static void Run(
        PdfStream stream, Dictionary<string, HashSet<int>> into, HashSet<string> composite, PdfDocument doc)
    {
        if (Resolve(doc, stream.Dictionary.Get("Resources")) is not PdfDictionary resDict)
            return;

        byte[] content;
        try { content = stream.GetDecodedData(doc.Decryptor); }
        catch (Exception) { return; }

        var collector = new ToUnicodeUsageCollector(new PdfResources(resDict, doc), doc);
        try { collector.ProcessOperators(PdfContentParser.Parse(content)); }
        catch (Exception) { return; }

        foreach ((PdfFont font, HashSet<int> codes) in collector.Result)
        {
            if (!into.TryGetValue(Key(font), out HashSet<int>? existing))
                into[Key(font)] = [.. codes];
            else
                existing.UnionWith(codes);

            if (font is Type0Font)
                composite.Add(Key(font));
        }
    }

    // ── structural upper bounds for the other three paths ─────────────────────────────────────────

    private static bool HasType3(ConformanceContext ctx, PdfDocument doc) =>
        ctx.ReferencedFonts.Any(f => (Resolve(doc, f.Get("Subtype")) as PdfName)?.Value == "Type3");

    private static bool HasPatternFont(ConformanceContext ctx, PdfDocument doc) =>
        ctx.Pages.Any(p => p.GetResources()?.GetPatterns() is { } patterns
            && patterns.Values.Any(v => Resolve(doc, v) is PdfStream s
                && Resolve(doc, s.Dictionary.Get("Resources")) is PdfDictionary rd
                && rd.Get("Font") is not null));

    private static bool HasExtGStateFont(ConformanceContext ctx, PdfDocument doc) =>
        ctx.Pages.Any(p => p.GetResources()?.GetExtGStates() is { } gs
            && gs.Values.Any(v => Resolve(doc, v) is PdfDictionary d && d.Get("Font") is not null));

    private static PdfObject? Resolve(PdfDocument doc, PdfObject? obj) =>
        obj is PdfIndirectReference r ? doc.ResolveReference(r) : obj;

    // ── report ────────────────────────────────────────────────────────────────────────────────────

    private static void WriteReport(IReadOnlyList<DocResult> all)
    {
        var sb = new StringBuilder();
        DocResult[] ok = [.. all.Where(r => r.Error is null)];
        DocResult[] failed = [.. all.Where(r => r.Error is not null)];

        sb.AppendLine("# Issue 51 — false-empty UsedCodes population (throwaway probe)");
        sb.AppendLine();
        sb.AppendLine($"documents scanned      : {all.Count}");
        sb.AppendLine($"  parsed OK            : {ok.Length}");
        sb.AppendLine($"  failed to parse      : {failed.Length}");
        sb.AppendLine();
        sb.AppendLine("## CONFIRMED (annotation-AP path, exact)");
        sb.AppendLine($"docs with >=1 AP-only font (fully false-empty) : {ok.Count(r => r.ApOnlyFonts > 0)}");
        sb.AppendLine($"docs with >=1 AP-extended font (partial)       : {ok.Count(r => r.ApExtendedFonts > 0)}");
        sb.AppendLine($"total AP-only fonts                            : {ok.Sum(r => r.ApOnlyFonts)}");
        sb.AppendLine($"total AP-extended fonts                        : {ok.Sum(r => r.ApExtendedFonts)}");
        sb.AppendLine($"total codes invisible to the narrow walk       : {ok.Sum(r => r.ApExtraCodes)}");
        sb.AppendLine();
        sb.AppendLine("### of those, COMPOSITE (Type0) — the only kind whole-face replacement rewrites");
        sb.AppendLine($"docs with >=1 AP-only COMPOSITE font           : {ok.Count(r => r.ApOnlyComposite > 0)}");
        sb.AppendLine($"docs with >=1 AP-extended COMPOSITE font       : {ok.Count(r => r.ApExtendedComposite > 0)}");
        sb.AppendLine($"total AP-only composite fonts                  : {ok.Sum(r => r.ApOnlyComposite)}");
        sb.AppendLine($"total AP-extended composite fonts              : {ok.Sum(r => r.ApExtendedComposite)}");
        sb.AppendLine();
        sb.AppendLine("### ISSUE-44 FILTER RISK — falsely-undrawn composite SHARING a holder with a drawn font");
        sb.AppendLine($"docs at risk                                   : {ok.Count(r => r.SharedHolderRisk > 0)}");
        sb.AppendLine($"fonts at risk                                  : {ok.Sum(r => r.SharedHolderRisk)}");
        foreach (DocResult r in ok.Where(r => r.SharedHolderRisk > 0))
            sb.AppendLine($"  {r.Corpus}/{r.File}  {r.RiskDetail}");
        sb.AppendLine();
        sb.AppendLine("## UPPER BOUND (other three paths, structural presence only)");
        sb.AppendLine($"docs containing a Type3 font                   : {ok.Count(r => r.HasType3)}");
        sb.AppendLine($"docs containing a tiling pattern with /Font    : {ok.Count(r => r.HasPatternFont)}");
        sb.AppendLine($"docs containing an ExtGState with /Font        : {ok.Count(r => r.HasExtGStateFont)}");
        sb.AppendLine();
        sb.AppendLine("## Context");
        sb.AppendLine($"docs where some referenced font has no narrow codes : {ok.Count(r => r.NarrowEmptyFonts > 0)}");
        sb.AppendLine($"  (includes genuinely-undrawn fonts — the issue 44 population — so this is NOT the defect count)");
        sb.AppendLine();

        sb.AppendLine("## Affected documents (AP path)");
        foreach (DocResult r in ok.Where(r => r.ApOnlyFonts > 0 || r.ApExtendedFonts > 0)
                     .OrderByDescending(r => r.ApOnlyFonts).ThenByDescending(r => r.ApExtraCodes))
        {
            sb.AppendLine($"{r.Corpus}/{r.File}  fonts={r.FontsTotal} apOnly={r.ApOnlyFonts} " +
                          $"(composite {r.ApOnlyComposite}) apExtended={r.ApExtendedFonts} " +
                          $"(composite {r.ApExtendedComposite}) extraCodes={r.ApExtraCodes}");
        }
        sb.AppendLine();

        sb.AppendLine("## Parse failures by type");
        foreach (IGrouping<string, DocResult> g in failed.GroupBy(r => r.Error!).OrderByDescending(g => g.Count()))
            sb.AppendLine($"{g.Count(),5}  {g.Key}");

        string outPath = Environment.GetEnvironmentVariable("ISSUE51_REPORT")
                         ?? Path.Combine(Path.GetTempPath(), "issue51-probe.md");
        File.WriteAllText(outPath, sb.ToString());
    }
}
