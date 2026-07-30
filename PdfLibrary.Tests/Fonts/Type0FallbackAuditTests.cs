using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FontParser.Tables.Cff.Type1;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// AUDIT, not a gate. Answers one question with evidence instead of assumption: does the Type0
/// glyph-name fallback in <c>EmbeddedFontExtractor</c> have any real work to do, and would the two
/// candidate extensions to it (issue #10) ever fire?
///
/// <para>
/// The fallback only runs when a Type0 font has NO usable <c>/ToUnicode</c> CMap. Its descriptor is
/// always a CIDFont's, so per ISO 32000-2 §9.7.4.2 the only font files that can appear are
/// <c>/FontFile2</c> (handled) and <c>/FontFile3</c> (<c>/CIDFontType0C</c> or <c>/OpenType</c>).
/// A CID-keyed CFF has no glyph names at all — its charset maps GID→CID — so the only way
/// <c>/FontFile3</c> support could pay off is a NAME-keyed CFF wrongly embedded as
/// <c>/CIDFontType0C</c> by a broken producer. This census counts exactly that.
/// </para>
///
/// <para>
/// LocalOnly: needs the sibling <c>../veraPDF-corpus</c> and/or <c>../gwg-gos</c> checkouts, which are
/// absent on CI. Prints its census through the assertion message so the numbers land in test output
/// without needing a console.
/// </para>
/// </summary>
[Trait("Category", "LocalOnly")]
public class Type0FallbackAuditTests
{
    private sealed class Census
    {
        public int FilesScanned, FilesFailed, Type0Fonts, Type0WithToUnicode, Type0NoToUnicode;
        public int NoTu_FontFile2, NoTu_FontFile3, NoTu_FontFileType1, NoTu_NoFontFile;
        public int FontFile3_CidKeyed, FontFile3_NameKeyed, FontFile3_Unparsable;
        public readonly List<string> NameKeyedExamples = [];
        public readonly List<string> NoToUnicodeExamples = [];
        // Registry-Ordering of the no-/ToUnicode fonts. Decides whether case 3 (registry CID->Unicode
        // via a bundled Adobe CMap) could help: Adobe-Identity-0 has NO Unicode mapping, so a font
        // with Identity ordering and no /ToUnicode is unextractable by any means.
        public readonly Dictionary<string, int> NoTuOrderings = [];
    }

    [Fact]
    public void Audit_Type0GlyphNameFallback_AcrossLocalCorpora()
    {
        string[] roots = new[] { "../veraPDF-corpus", "../gwg-gos" }
            .Select(r => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..", r)))
            .Where(Directory.Exists)
            .ToArray();

        Assert.SkipWhen(roots.Length == 0, "No local corpus checkout found (../veraPDF-corpus, ../gwg-gos).");

        var c = new Census();
        foreach (string file in roots.SelectMany(r => Directory.EnumerateFiles(r, "*.pdf", SearchOption.AllDirectories)))
        {
            c.FilesScanned++;
            try { ScanDocument(file, c); }
            catch { c.FilesFailed++; }
        }

        string report =
            $"""

             ===== Type0 glyph-name fallback audit (issue #10) =====
             files scanned .................. {c.FilesScanned}   (unreadable: {c.FilesFailed})
             Type0 fonts found .............. {c.Type0Fonts}
               with /ToUnicode .............. {c.Type0WithToUnicode}   <- fallback never runs
               WITHOUT /ToUnicode ........... {c.Type0NoToUnicode}   <- the only files that matter
                 descriptor /FontFile2 ...... {c.NoTu_FontFile2}   (already handled today)
                 descriptor /FontFile3 ...... {c.NoTu_FontFile3}
                     CID-keyed CFF .......... {c.FontFile3_CidKeyed}   <- no glyph names exist; unfixable
                     NAME-keyed CFF ......... {c.FontFile3_NameKeyed}   <- case 2: the ONLY payoff
                     unparsable ............. {c.FontFile3_Unparsable}
                 descriptor /FontFile ....... {c.NoTu_FontFileType1}   (illegal on a CIDFont)
                 no font file at all ........ {c.NoTu_NoFontFile}   <- case 3 territory (registry CID->Unicode)
             /CIDSystemInfo Registry-Ordering of the no-/ToUnicode fonts:
               {string.Join("\n               ", c.NoTuOrderings.OrderByDescending(k => k.Value).Select(k => $"{k.Key} ... {k.Value}"))}
               (Adobe-Identity-0 has NO Unicode mapping — unextractable by any fallback)
             name-keyed examples: {(c.NameKeyedExamples.Count == 0 ? "(none)" : string.Join(", ", c.NameKeyedExamples.Take(5)))}
             no-ToUnicode examples: {(c.NoToUnicodeExamples.Count == 0 ? "(none)" : string.Join(", ", c.NoToUnicodeExamples.Take(5)))}
             ======================================================

             """;

        // Skip, not Fail: this is a census run on demand, and it asserts nothing about the corpus —
        // the numbers ARE the output. Skipping surfaces the report without leaving a permanently-red
        // test for whoever next runs the LocalOnly suite.
        Assert.Skip(report);
    }

    private static void ScanDocument(string path, Census c)
    {
        using var doc = PdfDocument.Load(path);
        var seenFonts = new HashSet<int>();
        var seenRes = new HashSet<int>();

        for (var i = 0; i < doc.PageCount; i++)
        {
            PdfPage? page = doc.GetPage(i);
            WalkResources(page?.GetResources(), doc, c, seenFonts, seenRes, path, 0);
        }
    }

    private static void WalkResources(PdfResources? res, PdfDocument doc, Census c,
        HashSet<int> seenFonts, HashSet<int> seenRes, string path, int depth)
    {
        if (res is null || depth > 12) return;
        if (res.Dictionary.IsIndirect && !seenRes.Add(res.Dictionary.ObjectNumber)) return;

        if (res.GetFonts() is { } fonts)
            foreach (PdfObject f in fonts.Values)
                InspectFont(f, doc, c, seenFonts, path);

        if (res.GetXObjects() is { } xobjs)
            foreach (PdfObject x in xobjs.Values)
                if (Deref(x, doc) is PdfStream { Dictionary: { } sd } &&
                    (sd.Get("Subtype") as PdfName)?.Value == "Form" &&
                    Deref(sd.Get("Resources"), doc) is PdfDictionary rd)
                    WalkResources(new PdfResources(rd, doc), doc, c, seenFonts, seenRes, path, depth + 1);
    }

    private static void InspectFont(PdfObject fontObj, PdfDocument doc, Census c,
        HashSet<int> seenFonts, string path)
    {
        if (fontObj is PdfIndirectReference r && !seenFonts.Add(r.ObjectNumber)) return;
        if (Deref(fontObj, doc) is not PdfDictionary font) return;
        if ((font.Get("Subtype") as PdfName)?.Value != "Type0") return;

        c.Type0Fonts++;

        if (font.Get("ToUnicode") is not null) { c.Type0WithToUnicode++; return; }
        c.Type0NoToUnicode++;
        if (c.NoToUnicodeExamples.Count < 5) c.NoToUnicodeExamples.Add(Path.GetFileName(path));

        // Descendant CIDFont -> descriptor -> which font file?
        if (Deref(font.Get("DescendantFonts"), doc) is not PdfArray { Count: > 0 } df) { c.NoTu_NoFontFile++; return; }
        if (Deref(df[0], doc) is not PdfDictionary cid) { c.NoTu_NoFontFile++; return; }
        string ordering = "(absent)";
        if (Deref(cid.Get("CIDSystemInfo"), doc) is PdfDictionary csi)
        {
            string reg = (Deref(csi.Get("Registry"), doc) as PdfString)?.Value ?? "?";
            string ord = (Deref(csi.Get("Ordering"), doc) as PdfString)?.Value ?? "?";
            ordering = $"{reg}-{ord}";
        }
        c.NoTuOrderings[ordering] = c.NoTuOrderings.GetValueOrDefault(ordering) + 1;

        if (Deref(cid.Get("FontDescriptor"), doc) is not PdfDictionary fd) { c.NoTu_NoFontFile++; return; }

        if (fd.Get("FontFile2") is not null) { c.NoTu_FontFile2++; return; }
        if (fd.Get("FontFile") is not null) { c.NoTu_FontFileType1++; return; }
        if (fd.Get("FontFile3") is null) { c.NoTu_NoFontFile++; return; }

        c.NoTu_FontFile3++;
        ClassifyCff(Deref(fd.Get("FontFile3"), doc) as PdfStream, doc, c, path);
    }

    /// <summary>The load-bearing distinction: a CID-keyed CFF has no glyph names to extract.</summary>
    private static void ClassifyCff(PdfStream? stream, PdfDocument doc, Census c, string path)
    {
        if (stream is null) { c.FontFile3_Unparsable++; return; }
        try
        {
            byte[] data = stream.GetDecodedData(doc.Decryptor);
            // /OpenType wraps the CFF in an sfnt; a bare /Type1C or /CIDFontType0C does not.
            byte[]? cff = LooksLikeSfnt(data) ? ExtractCffTable(data) : data;
            if (cff is null) { c.FontFile3_Unparsable++; return; }

            var table = new Type1Table(cff);
            if (table.IsCid) c.FontFile3_CidKeyed++;
            else
            {
                c.FontFile3_NameKeyed++;
                if (c.NameKeyedExamples.Count < 5) c.NameKeyedExamples.Add(Path.GetFileName(path));
            }
        }
        catch { c.FontFile3_Unparsable++; }
    }

    private static bool LooksLikeSfnt(byte[] d) =>
        d.Length >= 4 && ((d[0] == 0x00 && d[1] == 0x01) || (d[0] == 'O' && d[1] == 'T' && d[2] == 'T' && d[3] == 'O'));

    private static byte[]? ExtractCffTable(byte[] sfnt)
    {
        if (sfnt.Length < 12) return null;
        int numTables = (sfnt[4] << 8) | sfnt[5];
        for (var i = 0; i < numTables; i++)
        {
            int rec = 12 + i * 16;
            if (rec + 16 > sfnt.Length) return null;
            string tag = System.Text.Encoding.ASCII.GetString(sfnt, rec, 4);
            if (tag != "CFF ") continue;
            int off = (sfnt[rec + 8] << 24) | (sfnt[rec + 9] << 16) | (sfnt[rec + 10] << 8) | sfnt[rec + 11];
            int len = (sfnt[rec + 12] << 24) | (sfnt[rec + 13] << 16) | (sfnt[rec + 14] << 8) | sfnt[rec + 15];
            if (off < 0 || len < 0 || off + len > sfnt.Length) return null;
            return sfnt.AsSpan(off, len).ToArray();
        }
        return null;
    }

    private static PdfObject? Deref(PdfObject? o, PdfDocument doc) =>
        o is PdfIndirectReference r ? doc.ResolveReference(r) : o;
}
