using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// AUDIT, not a gate. Issue #8 says the top-level ExtGState keys <c>/TR</c>, <c>/BG</c>, <c>/UCR</c>
/// and <c>/HT</c> are logged "not implemented" and dropped, and that a real test PDF is needed before
/// investing. This censuses the local corpora to supply that evidence.
///
/// <para>
/// The load-bearing distinction is on <c>/TR</c> (and <c>/TR2</c>), the only visually-impactful one:
/// a value of <c>/Identity</c> or <c>/Default</c> is a NO-OP, so ignoring it is already correct
/// behaviour. Only a real transfer function — a function dictionary/stream, or an array of four —
/// would change rendered output. The census separates those two populations, because a file count
/// alone would overstate the gap.
/// </para>
///
/// <para>
/// <c>/BG</c> and <c>/UCR</c> govern how a DEVICE synthesises black when converting DeviceGray/RGB to
/// DeviceCMYK, and <c>/HT</c> selects a halftone screen. Neither has a meaningful effect on an
/// anti-aliased display rasteriser, so they are counted for completeness rather than as candidate
/// defects. LocalOnly: needs ../veraPDF-corpus and/or ../gwg-gos.
/// </para>
/// </summary>
[Trait("Category", "LocalOnly")]
public class ExtGStateAuditTests
{
    private sealed class Census
    {
        public int FilesScanned, FilesFailed, FilesWithAnyKey, ExtGStateDicts;
        public int Tr_Identity, Tr_Default, Tr_RealFunction, Tr_Other;
        public int Bg, Ucr, Ht;
        public readonly List<string> RealTrExamples = [];
    }

    [Fact]
    public void Audit_ExtGStateTransferAndHalftone_AcrossLocalCorpora()
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
            try
            {
                int before = c.Tr_Identity + c.Tr_Default + c.Tr_RealFunction + c.Tr_Other + c.Bg + c.Ucr + c.Ht;
                ScanDocument(file, c);
                int after = c.Tr_Identity + c.Tr_Default + c.Tr_RealFunction + c.Tr_Other + c.Bg + c.Ucr + c.Ht;
                if (after > before) c.FilesWithAnyKey++;
            }
            catch { c.FilesFailed++; }
        }

        int trTotal = c.Tr_Identity + c.Tr_Default + c.Tr_RealFunction + c.Tr_Other;
        string report =
            $"""

             ===== ExtGState TR/BG/UCR/HT audit (issue #8) =====
             files scanned .................. {c.FilesScanned}   (unreadable: {c.FilesFailed})
             files using ANY of these keys .. {c.FilesWithAnyKey}
             ExtGState dicts inspected ...... {c.ExtGStateDicts}

             /TR or /TR2 occurrences ........ {trTotal}
               /Identity .................... {c.Tr_Identity}   <- NO-OP; ignoring it is CORRECT
               /Default ..................... {c.Tr_Default}    <- NO-OP; ignoring it is CORRECT
               real function ................ {c.Tr_RealFunction}   <- the ONLY population that renders wrongly
               other/unrecognised ........... {c.Tr_Other}

             /BG or /BG2 .................... {c.Bg}   (device black-generation; no display effect)
             /UCR or /UCR2 .................. {c.Ucr}   (device undercolour removal; no display effect)
             /HT ............................ {c.Ht}   (halftone screen; no display effect)

             real-/TR examples: {(c.RealTrExamples.Count == 0 ? "(none)" : string.Join(", ", c.RealTrExamples.Take(5)))}
             ===================================================

             """;

        Assert.Skip(report);
    }

    private static void ScanDocument(string path, Census c)
    {
        using var doc = PdfDocument.Load(path);
        var seenRes = new HashSet<int>();
        var seenGs = new HashSet<int>();

        for (var i = 0; i < doc.PageCount; i++)
            WalkResources(doc.GetPage(i)?.GetResources(), doc, c, seenRes, seenGs, path, 0);
    }

    private static void WalkResources(PdfResources? res, PdfDocument doc, Census c,
        HashSet<int> seenRes, HashSet<int> seenGs, string path, int depth)
    {
        if (res is null || depth > 12) return;
        if (res.Dictionary.IsIndirect && !seenRes.Add(res.Dictionary.ObjectNumber)) return;

        if (res.GetExtGStates() is { } gsDict)
            foreach (PdfObject gsObj in gsDict.Values)
            {
                if (gsObj is PdfIndirectReference r && !seenGs.Add(r.ObjectNumber)) continue;
                if (Deref(gsObj, doc) is PdfDictionary gs) InspectExtGState(gs, doc, c, path);
            }

        if (res.GetXObjects() is { } xobjs)
            foreach (PdfObject x in xobjs.Values)
                if (Deref(x, doc) is PdfStream { Dictionary: { } sd } &&
                    (sd.Get("Subtype") as PdfName)?.Value == "Form" &&
                    Deref(sd.Get("Resources"), doc) is PdfDictionary rd)
                    WalkResources(new PdfResources(rd, doc), doc, c, seenRes, seenGs, path, depth + 1);
    }

    private static void InspectExtGState(PdfDictionary gs, PdfDocument doc, Census c, string path)
    {
        c.ExtGStateDicts++;

        foreach (string key in new[] { "TR", "TR2" })
        {
            if (gs.Get(key) is null) continue;
            switch (Deref(gs.Get(key), doc))
            {
                case PdfName { Value: "Identity" }: c.Tr_Identity++; break;
                case PdfName { Value: "Default" }: c.Tr_Default++; break;
                // A function dict/stream, or an array of four functions (one per colourant), is the
                // only shape that actually remaps output values.
                case PdfDictionary or PdfStream or PdfArray:
                    c.Tr_RealFunction++;
                    if (c.RealTrExamples.Count < 5) c.RealTrExamples.Add(Path.GetFileName(path));
                    break;
                default: c.Tr_Other++; break;
            }
        }

        if (gs.Get("BG") is not null || gs.Get("BG2") is not null) c.Bg++;
        if (gs.Get("UCR") is not null || gs.Get("UCR2") is not null) c.Ucr++;
        if (gs.Get("HT") is not null) c.Ht++;
    }

    private static PdfObject? Deref(PdfObject? o, PdfDocument doc) =>
        o is PdfIndirectReference r ? doc.ResolveReference(r) : o;
}
