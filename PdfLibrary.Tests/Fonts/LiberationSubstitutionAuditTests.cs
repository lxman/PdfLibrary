using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// AUDIT, not a gate. Answers the question the Liberation-bundling design turns on with numbers
/// instead of reasoning: of the non-embedded SIMPLE fonts in the local corpora, how many are
/// standard-14 aliases, how many carry /Widths, how many are Symbol/ZapfDingbats (which Liberation
/// has no face for), and how many draw a code whose Unicode Liberation does not cover.
///
/// <para>Coverage matters more here than at render time. A glyph missing from a render is transient;
/// a glyph missing from an EMBEDDED program is .notdef baked into the file permanently.</para>
///
/// <para>LocalOnly: needs the sibling corpus checkouts and a Liberation installation. Prints its
/// census through the assertion message, exactly like <see cref="Type0FallbackAuditTests"/>.</para>
/// </summary>
[Trait("Category", "LocalOnly")]
public class LiberationSubstitutionAuditTests
{
    private sealed class Census
    {
        public int FilesScanned, FilesFailed;
        public int SimpleFonts, SimpleEmbedded, SimpleNonEmbedded;
        public int Std14Alias, NamedFace;
        public int SymbolOrDingbats;
        public int WithWidths, WithoutWidths;
        public int CoverageOk, CoverageShort, CoverageUnknown;
        public int ShortAndStd14;
        public readonly Dictionary<string, int> Families = [];
        public readonly Dictionary<string, int> ShortExamples = [];
        public readonly SortedSet<int> UncoveredCodePoints = [];
    }

    /// <summary>The three Liberation text families, loaded once. Sans is the coverage probe: the
    /// three share a Unicode repertoire, and the report asserts that rather than assuming it.</summary>
    private sealed record Faces(EmbeddedFontMetrics Sans, EmbeddedFontMetrics Serif, EmbeddedFontMetrics Mono);

    private static Faces? LoadLiberation()
    {
        string[] dirs =
        [
            @"C:\Windows\Fonts",
            "/usr/share/fonts/truetype/liberation",
            "/usr/share/fonts/liberation",
            "/Library/Fonts",
        ];

        EmbeddedFontMetrics? Load(string leaf)
        {
            foreach (string d in dirs)
            {
                string p = Path.Combine(d, leaf);
                if (!File.Exists(p)) continue;
                var m = new EmbeddedFontMetrics(File.ReadAllBytes(p));
                if (m.IsValid) return m;
            }
            return null;
        }

        EmbeddedFontMetrics? sans = Load("LiberationSans-Regular.ttf");
        EmbeddedFontMetrics? serif = Load("LiberationSerif-Regular.ttf");
        EmbeddedFontMetrics? mono = Load("LiberationMono-Regular.ttf");
        return sans is null || serif is null || mono is null ? null : new Faces(sans, serif, mono);
    }

    private static bool Covers(EmbeddedFontMetrics face, int unicode)
    {
        if (unicode is <= 0 or > 0xFFFF) return false; // BMP probe only; astral is out of scope here
        (ushort gid, _) = face.TestCmapLookup((ushort)unicode);
        return gid != 0;
    }

    [Fact]
    public void Audit_LiberationCoverage_ForNonEmbeddedSimpleFonts()
    {
        Faces? faces = LoadLiberation();
        Assert.SkipWhen(faces is null, "Liberation Sans/Serif/Mono not found in any known font directory.");

        // DISCRIMINATION CHECK. If the coverage probe cannot tell a covered code point from an
        // uncovered one, every number below is noise. Latin 'A' and Cyrillic 'А' must be covered;
        // CJK U+4E00 must not be. A probe that answers "covered" to all three is broken, and this
        // audit must fail loudly rather than report a reassuring zero.
        Assert.True(Covers(faces!.Sans, 0x0041), "probe broken: Liberation Sans does not cover 'A'");
        Assert.True(Covers(faces.Sans, 0x0410), "probe broken: Liberation Sans does not cover Cyrillic 'А'");
        Assert.False(Covers(faces.Sans, 0x4E00), "probe broken: Liberation Sans reports coverage of CJK U+4E00");

        // The sibling checkouts are CONFORMANCE corpora: PDF/A and PDF/X both mandate embedding, so
        // their non-embedded population is near-empty by construction and says nothing about real
        // documents. PELLUCID_AUDIT_ROOTS (semicolon-separated) adds real-world pools — kept out of
        // the committed source so no personal path lands in the repo.
        string[] roots = new[] { "../veraPDF-corpus", "../gwg-gos", "../PDFUA-Reference-Files" }
            .Select(r => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..", r)))
            .Concat((Environment.GetEnvironmentVariable("PELLUCID_AUDIT_ROOTS") ?? "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(Directory.Exists)
            .ToArray();

        Assert.SkipWhen(roots.Length == 0, "No local corpus checkout found.");

        var c = new Census();
        foreach (string file in roots.SelectMany(EnumeratePdfs))
        {
            c.FilesScanned++;
            try { ScanDocument(file, c, faces); }
            catch { c.FilesFailed++; }
        }

        // Repertoire agreement across the three families, measured rather than assumed — the audit
        // probes Sans, and this is what licenses reading its result as "Liberation covers it".
        int sansOnly = 0, serifOnly = 0, monoOnly = 0;
        for (var u = 0x20; u <= 0xFFFF; u++)
        {
            bool s = Covers(faces.Sans, u), r = Covers(faces.Serif, u), m = Covers(faces.Mono, u);
            if (s && !r) serifOnly++;
            if (s && !m) monoOnly++;
            if (!s && (r || m)) sansOnly++;
        }

        string report =
            $"""

             ===== Liberation substitution audit =====
             files scanned .......................... {c.FilesScanned}   (unreadable: {c.FilesFailed})
             simple fonts found ..................... {c.SimpleFonts}
               embedded ............................. {c.SimpleEmbedded}   <- out of scope
               NON-embedded ......................... {c.SimpleNonEmbedded}   <- the population
                 standard-14 alias .................. {c.Std14Alias}
                 genuinely named face ............... {c.NamedFace}
                 Symbol / ZapfDingbats .............. {c.SymbolOrDingbats}   <- Liberation has NO face
                 has /Widths ........................ {c.WithWidths}
                 NO /Widths (metrics from program) .. {c.WithoutWidths}
               Liberation coverage of drawn codes:
                 fully covered ...................... {c.CoverageOk}
                 SHORT (would embed .notdef) ........ {c.CoverageShort}
                   ...of which standard-14 alias .... {c.ShortAndStd14}
                 undeterminable (no derivable code) . {c.CoverageUnknown}
             uncovered code points seen: {(c.UncoveredCodePoints.Count == 0 ? "(none)" : string.Join(" ", c.UncoveredCodePoints.Take(24).Select(u => $"U+{u:X4}")) + (c.UncoveredCodePoints.Count > 24 ? $" ... (+{c.UncoveredCodePoints.Count - 24} more)" : ""))}
             coverage-short examples: {(c.ShortExamples.Count == 0 ? "(none)" : string.Join(", ", c.ShortExamples.Take(6).Select(k => $"{k.Key}({k.Value})")))}
             family histogram (non-embedded, top 15):
               {string.Join("\n               ", c.Families.OrderByDescending(k => k.Value).Take(15).Select(k => $"{k.Value,5}  {k.Key}"))}
             Liberation repertoire agreement (BMP, U+0020..U+FFFF):
               in Sans but not Serif ................ {serifOnly}
               in Sans but not Mono ................. {monoOnly}
               in Serif/Mono but not Sans ........... {sansOnly}
             =========================================

             """;

        Assert.Skip(report);
    }

    /// <summary>
    /// The decisive metric question, measured rather than argued. 136 of the 328 non-embedded simple
    /// fonts in the real-world pool carry NO /Widths, so their advance widths come from the PROGRAM.
    /// For those, substituting Liberation moves text unless Liberation's advances match the
    /// standard-14 AFM widths the document was laid out against.
    ///
    /// <para>Liberation is designed metric-compatible with Arial/Times New Roman/Courier New, and
    /// those are in turn width-compatible with Helvetica/Times/Courier over the Latin set — but that
    /// is a claim about intent, and intent is not evidence. This compares Liberation's own hmtx
    /// advance against <see cref="Standard14Metrics"/> for every AGL-derivable code in
    /// WinAnsiEncoding, and reports the disagreement distribution.</para>
    /// </summary>
    [Fact]
    public void Audit_LiberationAdvances_AgainstStandard14Afm()
    {
        Faces? faces = LoadLiberation();
        Assert.SkipWhen(faces is null, "Liberation Sans/Serif/Mono not found in any known font directory.");

        (string std14, EmbeddedFontMetrics face)[] pairs =
        [
            ("Helvetica", faces!.Sans),
            ("Times-Roman", faces.Serif),
            ("Courier", faces.Mono),
        ];

        PdfFontEncoding winAnsi = PdfFontEncoding.GetStandardEncoding("WinAnsiEncoding");
        var lines = new List<string>();
        foreach ((string std14, EmbeddedFontMetrics face) in pairs)
        {
            // Standard14Metrics.WidthByName does NOT return null for a glyph name outside its table
            // — GetHelveticaWidthByName and friends end in `_ => 556 // default`. Probe that default
            // with a name no AFM contains, so a glyph whose real width merely COINCIDES with it is
            // quarantined as ambiguous rather than silently scored as agreement or disagreement.
            double? sentinel = Standard14Metrics.WidthByName(std14, "__notAGlyphName__");

            int compared = 0, exact = 0, within1 = 0, ambiguous = 0;
            var worst = 0.0;
            string worstGlyph = "(none)";
            var offenders = new List<string>();

            for (var code = 32; code <= 255; code++)
            {
                string? glyphName = winAnsi.GetGlyphName(code);
                if (string.IsNullOrEmpty(glyphName) || glyphName == ".notdef") continue;

                double? afm = Standard14Metrics.WidthByName(std14, glyphName);
                if (afm is null) continue;

                // Courier's table is a flat 600 for every name, so its sentinel equals every real
                // answer; exclude it from the quarantine or the whole family reads as ambiguous.
                if (sentinel is not null && afm == sentinel && !std14.StartsWith("Courier", StringComparison.Ordinal))
                {
                    ambiguous++;
                    continue;
                }

                string? uni = GlyphList.GetUnicode(glyphName);
                if (string.IsNullOrEmpty(uni)) continue;
                int cp = char.ConvertToUtf32(uni, 0);
                if (cp is <= 0 or > 0xFFFF) continue;

                // hmtx advance in the face's own units, normalised to the 1000-unit text space the
                // AFM widths are expressed in.
                ushort advance = face.GetUnicodeAdvanceWidth(cp);
                if (advance == 0) continue;
                double scaled = advance * 1000.0 / face.UnitsPerEm;

                compared++;
                double delta = Math.Abs(scaled - afm.Value);
                if (delta < 0.0001) exact++;
                if (delta <= 1.0) within1++;
                else offenders.Add($"{glyphName}:{scaled:F0}vs{afm.Value:F0}");
                if (delta > worst) { worst = delta; worstGlyph = glyphName; }
            }

            lines.Add(
                $"  {std14,-12} compared {compared,3}   within-1-unit {within1,3}   exact {exact,3}   "
                + $"worst {worst:F1} ({worstGlyph})   [ambiguous/AFM-default: {ambiguous}]");
            if (offenders.Count > 0)
                lines.Add($"      off-by->1: {string.Join(" ", offenders.Take(12))}"
                    + (offenders.Count > 12 ? $" (+{offenders.Count - 12})" : ""));
        }

        Assert.Skip(
            $"""

             ===== Liberation advance vs standard-14 AFM (WinAnsi, 1000-unit text space) =====
             {string.Join("\n             ", lines)}
             ================================================================================

             """);
    }

    /// <summary>Depth-first walk that survives an unreadable directory. Real-world pools (OneDrive
    /// placeholders, permission-denied subtrees) make <see cref="SearchOption.AllDirectories"/> throw
    /// mid-enumeration, which would abort the whole census after an arbitrary prefix — silently
    /// under-reporting rather than failing.</summary>
    private static IEnumerable<string> EnumeratePdfs(string root)
    {
        var stack = new Stack<string>([root]);
        while (stack.Count > 0)
        {
            string dir = stack.Pop();

            string[] files;
            try { files = Directory.GetFiles(dir, "*.pdf"); }
            catch { continue; }
            foreach (string f in files) yield return f;

            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch { continue; }
            foreach (string s in subs) stack.Push(s);
        }
    }

    private static void ScanDocument(string path, Census c, Faces faces)
    {
        using var doc = PdfDocument.Load(path);
        var seenFonts = new HashSet<int>();
        var seenRes = new HashSet<int>();

        for (var i = 0; i < doc.PageCount; i++)
        {
            PdfPage? page = doc.GetPage(i);
            WalkResources(page?.GetResources(), doc, c, seenFonts, seenRes, path, faces, 0);
        }
    }

    private static void WalkResources(PdfResources? res, PdfDocument doc, Census c,
        HashSet<int> seenFonts, HashSet<int> seenRes, string path, Faces faces, int depth)
    {
        if (res is null || depth > 12) return;
        if (res.Dictionary.IsIndirect && !seenRes.Add(res.Dictionary.ObjectNumber)) return;

        if (res.GetFonts() is { } fonts)
            foreach (PdfObject f in fonts.Values)
                InspectFont(f, doc, c, seenFonts, path, faces);

        if (res.GetXObjects() is { } xobjs)
            foreach (PdfObject x in xobjs.Values)
                if (Deref(x, doc) is PdfStream { Dictionary: { } sd } &&
                    (sd.Get("Subtype") as PdfName)?.Value == "Form" &&
                    Deref(sd.Get("Resources"), doc) is PdfDictionary rd)
                    WalkResources(new PdfResources(rd, doc), doc, c, seenFonts, seenRes, path, faces, depth + 1);
    }

    private static void InspectFont(PdfObject fontObj, PdfDocument doc, Census c,
        HashSet<int> seenFonts, string path, Faces faces)
    {
        if (fontObj is PdfIndirectReference r && !seenFonts.Add(r.ObjectNumber)) return;
        if (Deref(fontObj, doc) is not PdfDictionary font) return;

        string? subtype = (font.Get("Subtype") as PdfName)?.Value;
        // Simple fonts only. Composites are declined by the planner outright (CIDToGIDMap), and
        // Type3 glyphs are drawing instructions, not a program — neither is substitutable.
        if (subtype is null or "Type0" or "CIDFontType0" or "CIDFontType2" or "Type3") return;

        c.SimpleFonts++;

        PdfDictionary? fd = Deref(font.Get("FontDescriptor"), doc) as PdfDictionary;
        bool embedded = fd is not null &&
            (fd.Get("FontFile") is not null || fd.Get("FontFile2") is not null || fd.Get("FontFile3") is not null);
        if (embedded) { c.SimpleEmbedded++; return; }

        c.SimpleNonEmbedded++;

        string baseFont = (font.Get("BaseFont") as PdfName)?.Value ?? "(no BaseFont)";
        (string family, _, _) = Base35Aliases.Split(baseFont);
        c.Families[family] = c.Families.GetValueOrDefault(family) + 1;

        // A known standard-14 alias is one Base35Aliases actually has a ladder for; an unknown
        // family aliases to itself, which is how FamiliesFor signals "not one of ours".
        IReadOnlyList<string> ladder = Base35Aliases.FamiliesFor(family);
        bool std14 = !(ladder.Count == 1 && string.Equals(ladder[0], family, StringComparison.OrdinalIgnoreCase));
        if (std14) c.Std14Alias++; else c.NamedFace++;

        string key = family.Replace(" ", string.Empty);
        bool symbolic = key.Equals("Symbol", StringComparison.OrdinalIgnoreCase)
                     || key.Equals("ZapfDingbats", StringComparison.OrdinalIgnoreCase);
        if (symbolic) c.SymbolOrDingbats++;

        bool hasWidths = Deref(font.Get("Widths"), doc) is PdfArray { Count: > 0 };
        if (hasWidths) c.WithWidths++; else c.WithoutWidths++;

        // Coverage. Symbol/Dingbats are counted but excluded from the coverage verdict: their codes
        // are symbol-encoded, so an AGL round-trip would answer a question that was never asked.
        if (symbolic) { c.CoverageUnknown++; return; }

        if (PdfFont.Create(font, doc) is not { } parsed) { c.CoverageUnknown++; return; }

        EmbeddedFontMetrics probe = key.Contains("Courier", StringComparison.OrdinalIgnoreCase)
            ? faces.Mono
            : key.Contains("Times", StringComparison.OrdinalIgnoreCase) ? faces.Serif : faces.Sans;

        var derivable = 0;
        var missing = new List<int>();
        int first = (Deref(font.Get("FirstChar"), doc) as PdfInteger)?.Value ?? 0;
        int last = (Deref(font.Get("LastChar"), doc) as PdfInteger)?.Value ?? 255;
        if (last < first || last > 255) last = 255;

        for (int code = Math.Max(0, first); code <= last; code++)
        {
            string? glyphName = parsed.Encoding?.GetGlyphName(code);
            if (string.IsNullOrEmpty(glyphName) || glyphName == ".notdef") continue;

            string? uni = GlyphList.GetUnicode(glyphName);
            if (string.IsNullOrEmpty(uni)) continue;

            derivable++;
            int cp = char.ConvertToUtf32(uni, 0);
            if (!Covers(probe, cp)) missing.Add(cp);
        }

        if (derivable == 0) { c.CoverageUnknown++; return; }

        if (missing.Count == 0) { c.CoverageOk++; return; }

        c.CoverageShort++;
        if (std14) c.ShortAndStd14++;
        foreach (int cp in missing) c.UncoveredCodePoints.Add(cp);
        string f = Path.GetFileName(path);
        c.ShortExamples[f] = c.ShortExamples.GetValueOrDefault(f) + 1;
    }

    private static PdfObject? Deref(PdfObject? o, PdfDocument doc) =>
        o is PdfIndirectReference r ? doc.ResolveReference(r) : o;
}
