using System;
using System.Collections.Generic;
using System.Linq;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// Walks a PDF's fonts and collects every swallowed font-program parse fault, keyed for a committed
/// baseline. Split out from the gate test so the diff is testable without a corpus.
/// <para>The resource walk (pages, then Form XObjects, with a depth cap and object-number cycle guards)
/// mirrors <c>Type0FallbackAuditTests</c>, which established the shape.</para>
/// </summary>
internal static class FontFaultCanary
{
    /// <summary>Emitted when GetEmbeddedMetrics returns null despite a FontFile being present. That is
    /// the OTHER swallow — TrueTypeFont.cs's <c>catch { return null; }</c> destroys the metrics object
    /// before any Faults list can be read, so the canary has to name it from outside.</summary>
    public const string MetricsNullValue = "MetricsNull";

    /// <summary>Emitted when a program has an embedded font file, threw nothing anywhere, and STILL came
    /// back unusable. A truncated font does exactly this: the sfnt directory parses, every table offset
    /// lands out of range, GetTableBytes returns null, and no reader is ever entered — so there is
    /// nothing to catch and nothing to record. Failure by absence rather than by exception. Without this
    /// row the canary scores such a program clean, which is the same class of blindness it exists to
    /// close.</summary>
    public const string InvalidNoFaultValue = "InvalidNoFault";

    /// <summary>
    /// The baseline row value for one font's embedded program, or null when there is nothing to report.
    /// Pure apart from one deliberate side effect: it forces the lazy loca/glyf stage (see below), so the
    /// unit tests exercise the exact path production takes.
    /// </summary>
    public static string? Classify(EmbeddedFontMetrics? metrics)
    {
        if (metrics is null) return MetricsNullValue;

        // Force the lazy loca/glyf stage so a GlyfLoca fault can be seen. Faults is append-only, so this
        // must happen BEFORE reading it. The return value is irrelevant — we want the side effect.
        try { metrics.GetGlyphOutline(0); }
        catch { /* an outline throw is not a parse fault; the recorded Faults are the signal */ }

        // Multiple faults on one program join with '+' so a font stays a single, greppable baseline line.
        if (metrics.Faults.Count > 0)
            return string.Join("+", metrics.Faults.Select(f => f.ToString()));

        return metrics.IsValid ? null : InvalidNoFaultValue;
    }

    /// <summary>Diffs freshly collected faults against the committed baseline. Pure — no corpus, no I/O,
    /// no skip semantics (see <c>FontFaultCompareTests</c>).</summary>
    public static List<string> Compare(
        IReadOnlyDictionary<string, string> actual,
        IReadOnlyDictionary<string, string> expected)
    {
        var problems = new List<string>();
        foreach (KeyValuePair<string, string> kv in actual)
        {
            if (!expected.TryGetValue(kv.Key, out string? want))
                problems.Add($"  NEW      {kv.Key} = {kv.Value}");
            else if (want != kv.Value)
                problems.Add($"  CHANGED  {kv.Key}: {want} -> {kv.Value}");
        }
        foreach (string key in expected.Keys.Where(k => !actual.ContainsKey(k)))
            problems.Add($"  MISSING  {key}");
        return problems;
    }

    /// <summary>
    /// Opens one PDF, walks every font reachable from a page's resources, and adds a row for each
    /// embedded program that faulted. Clean programs add nothing.
    /// </summary>
    /// <param name="pdfPath">Absolute path to the PDF.</param>
    /// <param name="fileKey">Corpus-relative key for this file; forms the first half of each row key.</param>
    /// <param name="into">Row sink, keyed <c>"&lt;fileKey&gt;\t&lt;BaseFont&gt;"</c>.</param>
    /// <param name="programsExamined">Incremented once per embedded program actually inspected —
    /// the coverage counter the gate asserts on.</param>
    public static void Scan(string pdfPath, string fileKey, SortedDictionary<string, string> into,
        ref int programsExamined)
    {
        using PdfDocument doc = PdfDocument.Load(pdfPath);
        var seenFonts = new HashSet<int>();
        var seenRes = new HashSet<int>();
        var examined = 0;

        for (var i = 0; i < doc.PageCount; i++)
        {
            PdfPage? page = doc.GetPage(i);
            Walk(page?.GetResources(), doc, into, fileKey, seenFonts, seenRes, 0, ref examined);
        }

        programsExamined += examined;
    }

    private static void Walk(PdfResources? res, PdfDocument doc, SortedDictionary<string, string> into,
        string fileKey, HashSet<int> seenFonts, HashSet<int> seenRes, int depth, ref int examined)
    {
        if (res is null || depth > 12) return;
        if (res.Dictionary.IsIndirect && !seenRes.Add(res.Dictionary.ObjectNumber)) return;

        if (res.GetFonts() is { } fonts)
            foreach (PdfObject f in fonts.Values)
                Inspect(f, doc, into, fileKey, seenFonts, ref examined);

        if (res.GetXObjects() is { } xobjs)
            foreach (PdfObject x in xobjs.Values)
                if (Deref(x, doc) is PdfStream { Dictionary: { } sd } &&
                    (sd.Get("Subtype") as PdfName)?.Value == "Form" &&
                    Deref(sd.Get("Resources"), doc) is PdfDictionary rd)
                    Walk(new PdfResources(rd, doc), doc, into, fileKey, seenFonts, seenRes, depth + 1, ref examined);
    }

    private static void Inspect(PdfObject fontObj, PdfDocument doc, SortedDictionary<string, string> into,
        string fileKey, HashSet<int> seenFonts, ref int examined)
    {
        if (fontObj is PdfIndirectReference r && !seenFonts.Add(r.ObjectNumber)) return;
        if (Deref(fontObj, doc) is not PdfDictionary font) return;
        if (!HasEmbeddedProgram(font, doc)) return; // no font file: nothing to parse, nothing to report

        string baseFont = (font.Get("BaseFont") as PdfName)?.Value ?? "(no BaseFont)";
        string key = $"{fileKey}\t{baseFont}";

        if (PdfFont.Create(font, doc) is not { } pdfFont) return;
        examined++;

        EmbeddedFontMetrics? metrics;
        try { metrics = pdfFont.GetEmbeddedMetrics(); }
        catch (Exception ex) { into[key] = $"Throw:{ex.GetType().Name}"; return; }

        if (Classify(metrics) is { } row)
            into[key] = row;
    }

    /// <summary>True when the font (or a Type0's descendant CIDFont) declares any FontFile stream. The
    /// descriptor is reached by dictionary walk rather than PdfFont.GetDescriptor because a Type0's
    /// descriptor lives on its descendant, which PdfFont does not expose.</summary>
    private static bool HasEmbeddedProgram(PdfDictionary font, PdfDocument doc)
    {
        PdfDictionary? descriptorHolder = font;

        if ((font.Get("Subtype") as PdfName)?.Value == "Type0")
        {
            if (Deref(font.Get("DescendantFonts"), doc) is not PdfArray { Count: > 0 } df) return false;
            if (Deref(df[0], doc) is not PdfDictionary cid) return false;
            descriptorHolder = cid;
        }

        if (Deref(descriptorHolder.Get("FontDescriptor"), doc) is not PdfDictionary fd) return false;

        return fd.Get("FontFile") is not null
               || fd.Get("FontFile2") is not null
               || fd.Get("FontFile3") is not null;
    }

    private static PdfObject? Deref(PdfObject? obj, PdfDocument doc) =>
        obj is PdfIndirectReference r ? doc.ResolveReference(r) : obj;
}
