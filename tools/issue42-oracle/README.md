# issue42-oracle — what readers actually do with a CID beyond /CIDToGIDMap coverage

Fixture builder for tracker issue 42. Kept, not deleted, for the same reason as
`tools/issue51-probe/`: the conclusion here is a *disagreement between oracles*, and anyone who
later reads only the spec will "fix" the renderer back the wrong way.

**Not part of the test suite** — a drop-in file, like the issue-51 probe.

## The question

A `/CIDToGIDMap` stream covers CIDs `0 .. (len/2)-1`. What should a reader do with a CID beyond that?

The spec is thinner than it looks. Both editions define the map only *positionally* — ISO 32000-1
Table 117 / ISO 32000-2 Table 115: "the glyph index for a particular CID value c shall be a 2-byte
value stored in bytes 2 × c and 2 × c + 1" — and say nothing about bytes that do not exist. The rule
that settles it appears in **ISO 32000-2 §9.7.6.3 only**:

> For an embedded TrueType font with a CIDtoGIDMap stream, if a (character) code does not have a
> corresponding GID in the CIDtoGIDMap stream, the glyph for CID 0 shall be substituted.

That sentence is **absent from ISO 32000-1's §9.7.6.3** — it is a PDF 2.0 addition. PDF/A-2 is built
on ISO 32000-1.

## Method

```bash
cp tools/issue42-oracle/Issue42OracleFixture.cs PdfLibrary.Tests/Conformance/
dotnet build PdfLibrary.Tests -c Debug -f net10.0
ISSUE42_OUT=/path/out dotnet test PdfLibrary.Tests -c Debug -f net10.0 --no-build \
  --filter "FullyQualifiedName~Issue42OracleFixture"
rm PdfLibrary.Tests/Conformance/Issue42OracleFixture.cs
```

It scans `local-708` for a document with an embedded CIDFontType2 whose `/CIDToGIDMap` is a stream
with real coverage, rewrites that stream to cover **CID 0 only**, and saves a copy. Every CID the
page draws then falls beyond coverage, so rendering the original against the truncated copy isolates
exactly this behaviour. Render with poppler / mutool / gs (they live in WSL on the dev box — see
`~/PDFs/PdfCompare/Program.cs` for the invocations) and validate with the veraPDF CLI.

Outputs are deliberately not committed: the corpus documents are personal.

## Findings — 2026-08-19

**The render oracles and the conformance oracle disagree, and each is right for its own question.**

| Oracle | Behaviour for a CID beyond coverage |
|---|---|
| poppler 24.x | identity — draws the glyph at GID == CID |
| mutool (MuPDF) | identity — **pixel-identical to poppler** |
| Ghostscript | identity, plus `.notdef` boxes only where the resulting GID exceeds the subset font's glyph count (a separate concern — poppler/mutool draw nothing there) |
| **veraPDF 1.28.1** | **`.notdef`** — truncating the map adds `6.2.11.8`, `6.2.11.5`, `6.2.11.4.1`, `6.2.11.4.2` findings the original does not have |

No renderer in the field substitutes CID 0's glyph. The validator does.

## What that means for the engine

Issue 42 as filed was half right. Its conformance claim held — `FontProgramRule` was under-reporting
against veraPDF. Its **rendering** claim ("Pellucid renders an arbitrary glyph where a conforming
reader would render `.notdef`") is **false**: every reference renderer draws the arbitrary glyph too.
Applying the filed fix to `MapCidToGid` outright would have fixed the validator half and introduced a
render regression against the entire field.

So the resolution is split by consumer — `CidFont.MapCidToGid` (identity, renderer) versus
`CidFont.MapCidToGidStrict` (GID 0, conformance + remediation). Both are pinned adjacently in
`PdfLibrary.Tests/Fonts/CidToGidMapExplicitZeroTests.cs` with the evidence in their doc comments.

**Landing measurement:** zero movement. Full suite 3625/0, and the parity verdict table plus every
6.2.11.x clause row are byte-identical before and after. The shape does not occur in the veraPDF
corpus, so the strict path is currently exercised only by the unit pins and this fixture — a latent
correctness fix, not a measured improvement. Do not claim otherwise.
