using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// AUDIT, not a gate. Issue #9: <c>ShadingBuilder</c> handles axial (2), radial (3), Coons (6) and
/// tensor-product (7); types <b>1</b> (function-based), <b>4</b> (free-form Gouraud) and <b>5</b>
/// (lattice-form Gouraud) return null and therefore paint NOTHING.
///
/// <para>
/// Unlike the other audited entries this is a genuine missing feature — an unimplemented type is a
/// silently blank area, not a device concept or a producer bug. What the census establishes is
/// FREQUENCY: how often types 1/4/5 actually occur, and via which route (a bare <c>sh</c> operator's
/// <c>/Shading</c> resource, or a shading <i>pattern</i> reached through <c>scn</c>), so the work can
/// be prioritised against real usage rather than the spec's type count.
/// </para>
///
/// <para>LocalOnly: needs ../veraPDF-corpus and/or ../gwg-gos.</para>
/// </summary>
[Trait("Category", "LocalOnly")]
public class ShadingTypeAuditTests
{
    private sealed class Census
    {
        public int FilesScanned, FilesFailed, ShadingsFound;
        public readonly Dictionary<int, int> ByType = [];
        public readonly Dictionary<int, int> ByTypeAsPattern = [];
        public readonly Dictionary<int, int> BuiltOk = [];
        public readonly Dictionary<int, HashSet<string>> FilesByType = [];

        public void Record(int type, bool asPattern, string file, bool built)
        {
            ShadingsFound++;
            ByType[type] = ByType.GetValueOrDefault(type) + 1;
            if (asPattern) ByTypeAsPattern[type] = ByTypeAsPattern.GetValueOrDefault(type) + 1;
            if (built) BuiltOk[type] = BuiltOk.GetValueOrDefault(type) + 1;
            if (!FilesByType.TryGetValue(type, out HashSet<string>? set))
                FilesByType[type] = set = [];
            set.Add(Path.GetFileName(file));
        }
    }

    [Fact]
    public void Audit_ShadingTypes_AcrossLocalCorpora()
    {
        // PDF_AUDIT_DIRS (';'-separated) adds corpora beyond the two siblings. The default corpora are
        // prepress/conformance suites and are NOT representative for mesh shadings, which come from
        // technical/scientific vector output — so pointing this at e.g. the Asymptote gallery is how
        // the type 4/5 question actually gets answered.
        string[] extra = (Environment.GetEnvironmentVariable("PDF_AUDIT_DIRS") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string[] roots = new[] { "../veraPDF-corpus", "../gwg-gos" }
            .Select(r => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..", r)))
            .Concat(extra.Select(Path.GetFullPath))
            .Where(Directory.Exists)
            .ToArray();

        Assert.SkipWhen(roots.Length == 0,
            "No corpus found (../veraPDF-corpus, ../gwg-gos, or $PDF_AUDIT_DIRS).");

        var c = new Census();
        foreach (string file in roots.SelectMany(r => Directory.EnumerateFiles(r, "*.pdf", SearchOption.AllDirectories)))
        {
            c.FilesScanned++;
            try { ScanDocument(file, c); }
            catch { c.FilesFailed++; }
        }

        // "supported" is MEASURED, not declared: a type counts as supported when every occurrence of it
        // actually produced a ShadingDescriptor. A hardcoded flag would have gone stale the moment
        // types 4/5 landed.
        static string Row(Census c, int t, string name)
        {
            int total = c.ByType.GetValueOrDefault(t);
            int ok = c.BuiltOk.GetValueOrDefault(t);
            int pat = c.ByTypeAsPattern.GetValueOrDefault(t);
            int files = c.FilesByType.GetValueOrDefault(t)?.Count ?? 0;
            string mark = total == 0 ? "  --    " : ok == total ? "  ok    " : ok == 0 ? "MISSING " : "PARTIAL ";
            return $"  type {t} {name,-22} {mark} {total,5} occurrences  {ok,5} built  {files,4} files  ({pat} as pattern)";
        }

        var missingExamples = new[] { 1, 4, 5 }
            .Where(t => c.FilesByType.ContainsKey(t))
            .Select(t => $"type {t}: {string.Join(", ", c.FilesByType[t].Take(4))}")
            .ToList();

        string report =
            $"""

             ===== Shading type audit (issue #9) =====
             files scanned .................. {c.FilesScanned}   (unreadable: {c.FilesFailed})
             shading dictionaries found ..... {c.ShadingsFound}

             {Row(c, 1, "function-based")}
             {Row(c, 2, "axial")}
             {Row(c, 3, "radial")}
             {Row(c, 4, "free-form Gouraud")}
             {Row(c, 5, "lattice-form Gouraud")}
             {Row(c, 6, "Coons patch")}
             {Row(c, 7, "tensor-product patch")}

             MISSING/PARTIAL = ShadingBuilder returned null, i.e. a silently blank area.
             {(missingExamples.Count == 0 ? "No occurrences of types 1/4/5 in these corpora." : string.Join("\n             ", missingExamples))}
             =========================================

             """;

        Assert.Skip(report);
    }

    private static void ScanDocument(string path, Census c)
    {
        using var doc = PdfDocument.Load(path);
        var seenRes = new HashSet<int>();
        var seenSh = new HashSet<int>();

        for (var i = 0; i < doc.PageCount; i++)
            WalkResources(doc.GetPage(i)?.GetResources(), doc, c, seenRes, seenSh, path, 0);
    }

    private static void WalkResources(PdfResources? res, PdfDocument doc, Census c,
        HashSet<int> seenRes, HashSet<int> seenSh, string path, int depth)
    {
        if (res is null || depth > 12) return;
        if (res.Dictionary.IsIndirect && !seenRes.Add(res.Dictionary.ObjectNumber)) return;

        // Route 1: a /Shading resource, painted by the `sh` operator.
        if (res.GetShadings() is { } shadings)
            foreach (PdfObject sh in shadings.Values)
                RecordShading(sh, doc, c, seenSh, path, asPattern: false);

        // Route 2: a PatternType 2 (shading) pattern, painted by scn — this is the route G-8 showed
        // behaves differently from `sh`, so counting them apart matters.
        if (res.GetPatterns() is { } patterns)
            foreach (PdfObject pat in patterns.Values)
            {
                PdfObject? resolved = Deref(pat, doc);
                PdfDictionary? pd = resolved as PdfDictionary ?? (resolved as PdfStream)?.Dictionary;
                if (pd is null) continue;

                if ((pd.Get("PatternType") as PdfInteger)?.Value == 2)
                    RecordShading(pd.Get("Shading"), doc, c, seenSh, path, asPattern: true);
                else if (resolved is PdfStream tiling &&
                         Deref(tiling.Dictionary.Get("Resources"), doc) is PdfDictionary trd)
                    WalkResources(new PdfResources(trd, doc), doc, c, seenRes, seenSh, path, depth + 1);
            }

        if (res.GetXObjects() is { } xobjs)
            foreach (PdfObject x in xobjs.Values)
                if (Deref(x, doc) is PdfStream { Dictionary: { } sd } &&
                    (sd.Get("Subtype") as PdfName)?.Value == "Form" &&
                    Deref(sd.Get("Resources"), doc) is PdfDictionary rd)
                    WalkResources(new PdfResources(rd, doc), doc, c, seenRes, seenSh, path, depth + 1);
    }

    private static void RecordShading(PdfObject? shObj, PdfDocument doc, Census c,
        HashSet<int> seenSh, string path, bool asPattern)
    {
        if (shObj is PdfIndirectReference r && !seenSh.Add(r.ObjectNumber)) return;
        PdfObject? resolved = Deref(shObj, doc);
        PdfDictionary? dict = resolved as PdfDictionary ?? (resolved as PdfStream)?.Dictionary;
        if (dict is null) return;
        if ((dict.Get("ShadingType") as PdfInteger)?.Value is not { } type) return;

        // Presence is not support: actually build it. This is what turns the census from "which types
        // appear" into "which types we can render", which is the question the issue was really asking.
        var built = false;
        try { built = ShadingBuilder.Build(resolved, doc) is not null; }
        catch { built = false; }

        c.Record(type, asPattern, path, built);
    }

    private static PdfObject? Deref(PdfObject? o, PdfDocument doc) =>
        o is PdfIndirectReference r ? doc.ResolveReference(r) : o;
}
