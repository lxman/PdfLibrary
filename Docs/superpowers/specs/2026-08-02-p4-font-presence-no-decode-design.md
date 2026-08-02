# P-4 — Embedded-font presence check must not decode the font program

**Date:** 2026-08-02
**Issue:** Pellucid tracker P-4 (profiling session 2026-08-02): on the ISO 32000-2 cold open,
`Inflater.Inflate` is 30.3% of ALL busy CPU (~10 of 32.8 thread-seconds), reached via
`PdfRenderer.OnShowText` → `HasEmbeddedFontData` → `PdfFontDescriptor.GetFontFile/2/3()` →
`PdfStream.GetDecodedData` → FlateDecode. **Repo:** PDF engine only.

## Root cause (verified 2026-08-02, systematic-debugging Phase 1)

`PdfRenderer.HasEmbeddedFontData` (`PdfRenderer.cs:2064-2076`) answers a presence question — "does
this font's descriptor carry an embedded font program?" — by calling the three decoded-bytes
accessors (`PdfFontDescriptor.GetFontFile()`, `GetFontFile2()`, `GetFontFile3()`,
`PdfFontDescriptor.cs:257-269` etc.) and null-checking the results. Each accessor returns
`stream.GetDecodedData(...)` — the full decrypt + filter chain — and `GetDecodedData`
(`PdfStream.cs:111`) has no memoization. The call happens **once per text-showing operator**
(`PdfRenderer.cs:909`, building the fixup `TextRunContext`), so a 1,020-page text-heavy document
re-inflates its embedded fonts thousands of times and discards the bytes every time. Same disease
as issue #4 (Type3 font-dir scan per glyph).

The correct pattern already exists in the same file: `GetFontFile2Stream()`/`GetFontFile3Stream()`
(raw `PdfStream` accessors, no decode) — added for the subsetter.

## Goals

1. `HasEmbeddedFontData`'s answer costs a dictionary lookup + reference resolution — zero decode,
   zero decompression.
2. Identical answers for well-formed fonts (presence of a stream object under any of
   `/FontFile`, `/FontFile2`, `/FontFile3`).
3. Re-profile the ISO cold open: the `Inflate`-under-`OnShowText` chain gone from the trace
   (expected: total busy time drops by roughly a third; the post-open tail shortens).

## Non-goals

- No `GetDecodedData` memoization in this pass (defense-in-depth candidate, recorded in P-4's
  tracker entry as a follow-up consideration; the root-cause fix stands alone and keeps this
  program behavior-trivial).
- No change to the decoded-bytes accessors or their callers (`EmbeddedFontExtractor` /
  `EmbeddedFontMetrics` construct once per font and cache at their layer).
- No other P-findings (P-5 materialize-all, P-6 tail, P-7 items) — separate programs.

## Design

New presence property on the descriptor, using the raw-object pattern:

```csharp
/// <summary>True when the descriptor carries an embedded font program stream under any of
/// /FontFile, /FontFile2, /FontFile3 (ISO 32000-1 §9.8.2, Table 126). PRESENCE ONLY — resolves
/// the reference but never decodes the stream (P-4: the decoded-bytes accessors cost a full
/// decrypt+inflate per call, which a per-ShowText presence probe must not pay).</summary>
public bool HasEmbeddedFontProgram =>
    GetFontFileStreamRaw("FontFile") is not null
    || GetFontFileStreamRaw("FontFile2") is not null
    || GetFontFileStreamRaw("FontFile3") is not null;

private PdfStream? GetFontFileStreamRaw(string key)
{
    if (!_dictionary.TryGetValue(new PdfName(key), out PdfObject? obj)) return null;
    if (obj is PdfIndirectReference reference && _document is not null)
        obj = _document.ResolveReference(reference);
    return obj as PdfStream;
}
```

(`GetFontFile2Stream`/`GetFontFile3Stream` become one-line calls to the shared raw helper —
same-file dedup, no behavior change to the subsetter.)

`PdfRenderer.HasEmbeddedFontData` keeps its null-guard and delegates:

```csharp
private static bool HasEmbeddedFontData(PdfFont? font) =>
    font?.GetDescriptor()?.HasEmbeddedFontProgram ?? false;
```

**Semantic delta — RETIRED at execution (2026-08-02):** the spec presumed a present-but-corrupt
stream could throw mid-ShowText. Measured false: `FlateDecodeFilter` never throws — on total
failure it returns the raw bytes as-is ("uncompressed fallback"), so the old decoding probe also
answered true for corrupt streams, after paying three failed inflate attempts. The change is
therefore **pure work-elimination with identical answers in every case** — stronger than spec'd.
The corrupt-stream test pins the equivalence (and the filter degrade behavior it rests on)
instead of a crash-path delta; the acceptance re-profile is the authoritative no-decode proof.

## Testing & acceptance

1. Unit tests on `PdfFontDescriptor.HasEmbeddedFontProgram`: true for each of the three keys
   (direct stream and indirect reference); false with none; false when the key holds a non-stream
   object; TRUE for a stream whose filter chain is corrupt (the semantic-delta pin — and the test
   proves no decode happens, since a decode would throw).
2. Existing suites green: full engine suite (2790-shape) including conformance corpus gates —
   agreement counts unchanged (this touches no conformance rule).
3. **Re-profile:** repeat the exact ISO cold-open trace (45 s, Release, same command); confirm the
   `OnShowText → GetFontFile*` decode chain is gone and record the new busy total + tail shape in
   the tracker (P-4 verification + P-6 re-baseline in one run).

## Risks

- Minimal — the change is a strict narrowing of work done. The one behavioral edge (corrupt
  embedded stream) is pinned by test and is an improvement (no decode failure inside ShowText).
