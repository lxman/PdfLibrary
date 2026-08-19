using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfLibrary.Conformance;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Phase-3 reporting: renders the parity report (<see cref="ParityReport"/>) and guards a coarse,
/// ratcheting whole-file agreement floor. Both need the corpus (they run the preflighter via
/// <see cref="ParityComparison"/>), so they are <c>[Trait("Category","Parity")]</c>. The report is a
/// non-gating artifact; the agreement floor is the report's soft companion gate — it catches a broad
/// regression that the clause-exact gate in <see cref="ParityOracleTests"/> might not.
/// </summary>
[Trait("Category", "Parity")]
public class ParityReportTests(ITestOutputHelper output)
{
    private const string Skip = "veraPDF corpus not present at ../veraPDF-corpus (Category=Parity)";

    /// <summary>Whole-file verdict-agreement floor per profile — a ratchet; raise as coverage grows.
    /// A floor may also legitimately FALL when a fix removes detections that were false positives —
    /// lower it deliberately, name the fix in the inline comment, and verify veraPDF agrees the lost
    /// detections were never real (first case: issues 24-26, 2026-08-15) — or an accepted
    /// precision/recall trade, with the lost detections enumerated and the reference's verdict
    /// recorded (first case: issues 27-28 Task 10 fix round, 2026-08-16).</summary>
    private static readonly IReadOnlyDictionary<ConformanceProfile, int> AgreementFloor =
        new Dictionary<ConformanceProfile, int>
        {
            // Re-measured unchanged under issue 40's CID-0 predicate, 2026-08-17.
            [ConformanceProfile.PdfA2b] = 966,   // +3 (963->966), InlineImageRule, 2026-08-19: the inline-image
            // arm of two clauses the object-graph walks structurally cannot see, since an inline image lives in
            // a content stream and is never an indirect object. 6.1.10 t1 (+2: "veraPDF test suite
            // 6-1-10-t01-fail-a/b.pdf", /F /LZW and /F /LZWDecode) and 6.2.8 t3 (+1: "...6-2-8-1-t02-fail-b.pdf",
            // /I true) -- the XObject arm of 6.2.8 t3 was already covered, which is why only fail-b moved. Both
            // ported from the veraPDF 1.30.2 PDFA-2 profile rather than the clause text: 6.1.10 is a WHITELIST
            // (ASCIIHexDecode/ASCII85Decode/FlateDecode/RunLengthDecode/CCITTFaxDecode/DCTDecode + the Table 93
            // abbreviations), so an unknown filter name fails exactly as LZW does, and it applies element-wise
            // to a filter array. 0 FP corpus-wide; "...6-2-8-1-t02-pass-b.pdf" (/I false /F /Fl) is the guard
            // fixture and stays clean. Predicted exactly by the sole-cause analysis (see the report's verdict-
            // leverage section) before the rule was written: these 3 were the only misses blocked solely by
            // those two clauses.
            // -2 (965->963), Task 10 fix round (issues 27-28
            // follow-up review, 2026-08-16): the derived-name provenance fix (PdfFontEncoding.IsDerivedName /
            // FontProgramRule.ResolveSimpleGlyph) that closed the 7-new-FP corpus regression ALSO makes the
            // glyph-present resolver skip (Unknown) every code whose name came from SetUnicode's reverse-AGL
            // fallback -- which is EVERY code in a WinAnsi- or MacRoman-based font (CreateWinAnsiEncoding and
            // CreateMacRomanEncoding both call SetUnicode exclusively, never SetCharacterName), not only the
            // newly-AGL-resolvable subset. Two
            // corpus fixtures built exactly that way (WinAnsi base, no /Differences override on the missing
            // code) lost their genuine 6.2.11.4.1 detection: "veraPDF test suite 6-2-11-4-1-t02-fail-a.pdf"
            // and "...-fail-b.pdf" (root-caused via a git-stash A/B probe against the pre-fix resolver: 21
            // disagreements before, 23 after, and the only 2 new entries are these). Both are CFF fixtures --
            // round 2 of this fix (same commit) removed the analogous TrueType-branch gate as unjustified
            // (the TrueType arm only ever uses a name as a courier for the encoding's own Unicode value, so
            // provenance carries no information there), which is why this floor did NOT move back up: these
            // 2 CFF detections stay withdrawn on purpose. This is the SAME recall-regression class as
            // CC-MAIN's 4000_4000080.pdf. Full record: the tracked spec's Amendment 2
            // (docs/superpowers/specs/2026-08-15-encoding-follow-ups-27-28-design.md, "complete the AGL
            // table") states the completion mandate that exposed this; the mechanism itself is PdfLibrary
            // commit 43ae761 (the GlyphList completion) plus this commit (the derived-name provenance fix)
            // -- a deliberate FP-safety/recall trade-off, not an accident. +3 ICCBased-CMYK overprint (clause 6.2.4.2 test 2 → 3/3 full: OPM must not be 1 when an ICCBased CMYK space is painted with the matching overprint on — /OP for a stroke, /op for a fill; IccCmykOverprintRule tracks the fill/stroke colour space + overprint flags + OPM through gs and q/Q and evaluates at the paint operator, a byte-for-byte port of veraPDF's PDICCBasedCMYK model verified against a disassembly — CMYK keys off /N 4, page content only since forms carry an implicit q/Q), 0 FP. +4 stream object /Length + framing (clause 6.1.7.1 → 7/7 full with StreamExternalFileRule's test 3: /Length == real length t1, and stream/endstream EOL framing t2; StreamObjectRule reads the declared /Length from raw source bytes since the tolerant loader rewrites the parsed value, and ports veraPDF's CosStream parser byte-for-byte incl. the CRLF/CR-as-data realLength disambiguation — verified against a disassembly of veraPDF 1.30.2), 0 FP. +5 JPEG2000 image constraints (clause 6.2.8.3 → 5/5 full: channels ∈{1,3,4} t1, one-APPROX-0x01 among >1 colour specs t2, colr METH ∈{1,2,3} t3, no enumerated CS 19/CIEJab t4, bit depth ∈[1,38] & no bpcc box t5; Jpeg2000Rule reads the JP2 wrapper boxes from the raw JPXDecode stream with a defensive bounds-checked reader — t2/3/4 escaped by an explicit image /ColorSpace), 0 FP. +1 over-long string operand in page content (clause 6.1.13 test 3: a string > 32767 bytes used as a content operator's operand — e.g. a huge Tj literal — which the object-graph walk never reaches; ImplementationLimitsRule sub-check 4, page content only, a strict subset). 6.1.13 stays PARTIAL: the integer (test 1) and q/Q-nesting (test 8) content limits are blocked — the content lexer normalises an out-of-range integer operand to 0.0, so they need byte-level content tokenisation; CID > 65535 (test 10) needs a CMap parser. +7 indirect-object spacing (clause 6.1.9 → 7/7 full: the whitespace/EOL framing of every N G obj … endobj — single-ws objnum↔gennum, single-ws gennum↔obj, objnum & endobj each EOL-preceded, obj & endobj each EOL-followed; byte-level IndirectObjectSpacingRule, self-validated against the xref so a stale/leading-junk offset is skipped not mis-flagged), 0 FP. +3 content-stream operators (clause 6.2.2 t01: a page/Do-reached content stream must use only the 73 ISO 32000-1 operators, even inside BX/EX; ContentStreamOperatorRule, usage-sensitive so a dead form does not false-positive), 0 FP. t04 (run-together operators the lexer recovers, e.g. `ref`→`re`+`f`) deferred to a content-tokenisation change. +4 CMap WMode/UseCMap (6.2.11.3.3 t2/t3 → 5/5 full; font-program slice 3), 0 FP. +1 Type3 font metrics (6.2.11.5 via CharProc d0/d1 → 7/13; font-program slice 2), 0 FP. +5 prohibited-xobject (6.2.9 → 5/5) +3 image-dictionary (6.2.8 → 3/4; the 4th is an inline image needing content-stream parsing) +4 permissions (6.1.12 → 4/4: /Perms keys + signature-reference Digest keys under DocMDP) +2 name-utf8 (6.1.8 → 2/2: every name valid UTF-8 after #-escape), all 0 FP. Ratchets to the current verified agreement (the earlier 899 lagged the 921 baseline). Standing −1 note: the 6.2.11.5 width check stays dropped on CIDFontType0/CFF fonts (CFF advance extraction false-positives on conformant reference files, PDFUA-Ref-2-08 — FP-safety outweighs one corpus detection). +2 simple-font glyph-present (6.2.11.4.1 t2 → 8/11, later 6/11 -- see the Task-10-fix-round note above: 2 of those 8 are CFF fixtures whose detection the derived-name provenance fix correctly withdrew) via the tri-state code→GID resolver (font-program slice 1); simple-font .notdef (6.2.11.8) is also live but adds 0 corpus files — its 5 remaining fail files are out-of-scope font types (classic Type1 / predefined-charset CFF / symbolic / Type0-non-identity → Unknown, FP-safe); 0 FP corpus-wide
            // +2 (19->21), issues 27/28 (branch fix/encoding-follow-ups-27-28, merged f71003a) — attributed
            // by per-commit measurement on 2026-08-19, not inferred from commit messages. The symbolic
            // built-in encoding fix (d3c5e7c, 2026-08-15) is what closes them: clause 6.2.11.7.2 goes 5->7
            // of 8, both-fail 7->9, misses 3->1. That SAME commit transiently introduced 1 A-2u false
            // positive — the only violation of this corpus's zero-FP property on record — because widening
            // the symbolic built-in encoding made the rule consider codes it had been skipping, one of
            // which raised a spurious Unicode-mapping finding for a glyph name the then-incomplete
            // GlyphList could not resolve. Cleared in-branch by the AGL completion (43ae761, 2026-08-16)
            // before the merge, so no released commit ever carried it. Measured sequence: 2fbbfa3 19/22
            // 0FP -> d3c5e7c 20/22 1FP -> 43ae761 21/22 0FP -> stable to HEAD. This floor lagged at 19
            // only because PARITY-REPORT.md was not regenerated between 2026-07-11 and 2026-08-19 — the
            // A-2b and UA-1 floors were maintained in that window and were already at their ceilings.
            [ConformanceProfile.PdfA2u] = 21,    // + 6.2.11.3.1 (embedded-CMap supplement) catches 6-2-11-7-2-t01-fail-f
            [ConformanceProfile.PdfA3b] = 12,
            [ConformanceProfile.PdfUA1] = 296,   // FULL machine-checkable UA-1 parity (296/296). +3 table-header (clause 7.5 t1/t2 → 2/2 full: in a regular table every TD must connect to a header via /Headers→TH /ID or an explicit-Scope TH heading its column/row — PDF/UA-1 has no default scope; UaTableHeaderRule), 0 FP. +3 media-clip (clause 7.18.6.2 t1/t2 → 2/2 full: a Rendition-action media clip data dictionary needs a /CT content-type string and a correct /Alt multi-language text array; UaMediaClipRule), 0 FP. +1 encryption /P (clause 7.16 → 1/1 full: an encrypted file's /Encrypt /P must set bit 512, the accessibility-extraction permission; UaEncryptionRule), 0 FP. +2 role-map (clause 7.1 t6/t7 → 7.1 clause 16/16 full: no circular /RoleMap, no remapped standard type; document-level UaRoleMapRule), 0 FP. +3 pdfuaid-prefix (clause 5 t3/t4/t5 → 5/5 full: part/amd/corr must use the "pdfuaid" prefix; read per-property via XmlReader since XLinq collapses multiple prefixes on one URI), 0 FP. +3 CMap WMode/UseCMap (7.21.3.3 t2/t3 → 4/4 full; font-program slice 3), 0 FP. +6 from embedded-file widened to UA-1 7.11 (non-empty /F,/UF; filespecs from the catalog name tree AND FileAttachment annotation /FS → 6/6). Ratchets to the current verified agreement (the earlier 253 lagged the 275 baseline: slice-21 annotation rules + the incremental-update obj-stream resolution fix)
        };
    // Re-verified 2026-08-15 after the issues 24-26 false-positive fixes (indirect /W array elements,
    // StandardEncoding Annex D.2 names, zero-advance TrueType programs): all four floors held at their
    // pinned values (965 / 19 / 12 / 296), unchanged — no veraPDF-corpus file's whole-file verdict
    // depended on any of them. 21 files in the same PDF_A-2b checkout did lose spurious width findings
    // (see Pellucid.App.Tests/oi-corpus-baseline.txt, 2026-08-15), but each remained non-conforming for
    // other reasons, so the agreement counts were unaffected.

    [Fact]
    public void Generate_parity_report()
    {
        Assert.SkipUnless(CorpusHarness.IsAvailable && ParitySnapshot.IsAvailable, Skip);

        string markdown = ParityReport.Render();
        output.WriteLine(markdown);

        // Written to disk only when a destination is supplied (CI artifact path, or a manual refresh
        // of the committed report) — so a normal test run never dirties a tracked file.
        string? dest = Environment.GetEnvironmentVariable("PARITY_REPORT");
        if (!string.IsNullOrWhiteSpace(dest))
        {
            File.WriteAllText(dest, markdown);
            output.WriteLine($"\n(wrote report to {dest})");
        }

        Assert.Contains("# veraPDF parity report", markdown);
    }

    [Fact]
    public void Whole_file_agreement_does_not_regress()
    {
        Assert.SkipUnless(CorpusHarness.IsAvailable && ParitySnapshot.IsAvailable, Skip);

        var regressions = new List<string>();
        foreach (ParityComparison.ProfileComparison pc in ParityComparison.All)
        {
            int agree = pc.Files.Count(f => f.VeraCompliant == f.PdfLibraryConforms);
            int floor = AgreementFloor.GetValueOrDefault(pc.Profile);
            output.WriteLine($"{pc.Profile}: agreement {agree}/{pc.Files.Count} (floor {floor})");
            if (agree < floor)
                regressions.Add($"{pc.Profile}: {agree} < floor {floor}");
        }

        Assert.True(regressions.Count == 0,
            "whole-file agreement regressed vs the reference: " + string.Join(", ", regressions)
            + ". If a rule was intentionally changed, lower the floor; otherwise this is a regression.");
    }
}
