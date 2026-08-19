# issue51-probe — sizing the false-empty `UsedCodes` population

Measurement harness for tracker issue 51 (discovery/usage walk asymmetry). Kept rather than deleted:
tracker issue 50 records what it cost to throw the previous measurement harness away and rebuild a
classifier from scratch, so this one stays with its own results next to it.

**These are not part of the test suite.** They are drop-in files — the suite must stay free of
7-minute corpus scans and of tests that assert nothing.

## Running

```bash
cp tools/issue51-probe/Issue51*.cs PdfLibrary.Tests/Conformance/
dotnet build PdfLibrary.Tests -c Debug -f net10.0

ISSUE51_REPORT=/path/report.md dotnet test PdfLibrary.Tests -c Debug -f net10.0 --no-build \
  --filter "FullyQualifiedName~Issue51FalseEmptyProbe"      # ~7 min over 4708 documents

ISSUE51_JOIN=/path/join.txt dotnet test PdfLibrary.Tests -c Debug -f net10.0 --no-build \
  --filter "FullyQualifiedName~Issue51JoinCheck"            # ~1 s, six documents

rm PdfLibrary.Tests/Conformance/Issue51*.cs                 # leave the tree clean
```

Corpora are the two real-world sets (`D:\PdfCorpora\real-world\local-708` and
`…\cc-main-2021-31-sample`), hardcoded in `Corpora` — the same paths
`WidthFalsePositiveCorpusTests` defaults to.

## What each measures

`Issue51FalseEmptyProbe` runs the engine's own usage walk (`ConformanceContext.UsedTextGlyphs` —
page content plus Form XObjects) and, separately, runs the same `ToUnicodeUsageCollector` over every
annotation appearance stream. Diffing the two gives the exact annotation-AP population. The other
three blind paths (tiling pattern, Type3 CharProc, ExtGState `/Font`) are counted **structurally
only** — presence of the shape in the document, an upper bound, not a confirmed draw.

`Issue51JoinCheck` exists because the probe's headline result is a **zero**, and a zero is worthless
without evidence the join that produced it can match at all. It dumps raw `FontInventory` state for
the six documents the probe flagged, so the per-document empty-`UsedCodes` counts can be reconciled
against the probe's own figures by hand.

## Two traps, both hit during the original run

1. **Never key a cross-walk join on `PdfFont` reference identity.** Every `PdfResources` instance
   mints its own `PdfFont` wrapper for the same underlying dictionary, so two walks can never agree
   by reference. The first run reported *more* AP-only fonts than the document had fonts, and forced
   the partial-overlap count to a clean zero that looked like a finding. Key on the same identity the
   real consumers dedup on — `FontProgramRule.DedupKey`, i.e. object number for an indirect
   dictionary, base-font name for a direct one. `Key()` in the probe is that.
2. **A structural presence check is not a draw.** "Document contains a Type3 font" is not "a Type3
   CharProc draws text in another font." The report labels these UPPER BOUND for that reason; do not
   quote them as defect counts.

## Results as of 2026-08-19 (engine `4d624fb`)

`issue51-probe-v4.md` and `issue51-join.txt` are the runs the tracker's issue 51 entry cites.
4,708 documents scanned, 4,659 parsed, 49 unparseable (28 `PdfParseException`, 12
`PdfSecurityException`, 9 `ArgumentException`).

Headline: both corruption routes measured **zero**, and the join check explains why — when a font is
AP-only, its whole program-holder group is AP-only, so no merge group ever mixes a drawn with an
undrawn member. See the tracker for the full disposition.
