# Fix wave — `colour/release-hooks-2.5.2` release-readiness review findings

Applied against `3471a75`. All six findings from `final-review-report.md` (P-1, PROD-1, G8-1,
G12-1, REL-1, DOC-1) addressed.

## P-1 (Important) — dead pin, `PdfLibrary.Tests/Rendering/PdfImageToCmykTests.cs:916-931`

Replaced the Indexed array's 4th element `new PdfName("Lookup")` with
`new PdfString([0xFF, 0x00])` (hival 1, 1 component/entry), matching the report's verified
fixture. This makes `ResolveLookup` succeed instead of bailing at `PdfImageToCmyk.cs:102`/`:348`
before the base colour space is ever inspected.

Rewrote the comment block above the test to state the corrected mechanism: the `/Lookup` element
IS consulted first; with a real lookup, `TryToCmyk` reaches `BuildIndexedEntryToCmyk`'s Separation
arm and dies in the uncached tint transform (`BuildTintToCmyk`/`PdfFunction.Create` on the
`/Identity` helper), because the Indexed route has no reserved-direct arm mirroring
`ShadingBuilder.BuildCmykMapper`. `TryToSpotInk`'s half declines separately and permanently
(`Classify("Cyan") != Spot` → `spotNames.Count == 0`), which is decoration, not part of the hook.

**Evidence:** `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~G14ResidualBaseline"` —
Passed 1/1. The pin now exercises `BuildIndexedEntryToCmyk`'s Separation/tint-transform path, so a
future reserved-direct fix in that method will flip it red, unlike before.

## PROD-1 (Important) — `PdfLibrary/Document/PageColorantReader.cs:35-48`

Wrapped the `PdfLogger.Log` call inside its own `try { } catch { }` and switched to the
`Func<string>` lazy overload (`PdfLogger.Log(LogCategory.Graphics, () => $"...")`), per the
report's verbatim fix. Added one comment line explaining why the log call is itself guarded
(`PdfLogger.Log` can throw when logging is enabled and the configured log path is unwritable,
which would defeat the never-throw guarantee the outer catch exists for). Kept the existing
explanatory comment above it unchanged.

**Evidence:** `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~PageColorant"` —
Passed 24/24. `dotnet build PdfLibrary -c Release` — 0 warnings, 0 errors.

## G8-1 (Minor) — `PdfLibrary.Tests/Rendering/ColourGapBaselineTests.cs:13-24`

Rewrote the leading comment on `NoneShadingPattern_paints_G8Baseline`. It previously claimed the
tint transform was "CONSTANT black" and that the shading's `/None` colour space "paints through
ShadingBuilder anyway" — implying the tint transform is evaluated. Corrected to state: the pattern
resolves and `FillWithShadingPattern` has no `PaintsNothing` check (confirming G-8's routing
claim); the tint transform (fixture object 8) is never evaluated because
`ColorSpaceResolver.BuildTintToRgb` declines the `/None` colourant at `ColorSpaceResolver.cs:414`
and returns null before `PdfFunction.Create` ever touches it; `BuildColorMapper` then falls
through to the `ToArgbByCount` fallback, which reads the shading `/Function`'s single 1.0 tint
component as DeviceGray 1.0 = white. Noted the fixture's element-8 tint transform is consequently
dead weight.

Also updated `Docs/colour/rendering-conformance.md`'s G-8 entry (line ~530) to close the
previously-open question ("why the constant-black chain renders white … is not explained") with
the same answer, and flagged the second, distinct defect the report calls out: `BuildColorMapper`
cannot distinguish "unrecognised colour space" from "colour space that refused a mapper because it
paints nothing," and guesses in both cases via the component-count fallback. Also corrected the
top-of-file "Delta 2026-07-29" summary block (line ~203) which repeated the same "not fully
explained" claim, and added a sentence there about the P-1 fixture correction.

**Evidence:** doc-only + test-comment-only change; `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~ColourGapBaseline"` included in the full-suite run below — all pass, no behaviour change intended or observed.

## G12-1 (Minor) — `PdfLibrary.Tests/Rendering/ColorSpaceResolveCountTests.cs:10-16`

Rewrote the comment: it previously claimed the de-duplication design "must LOWER this number" for
either of G-12's two named fix shapes. Corrected to state the pin guards the fill/stroke-split
shape only (4 → 2); a tint-transform-caching fix leaves the method-entry count at 4 and does not
flip this pin, because `ResolveCallCount++` is the first statement of `ResolveColorSpace`, ahead
of the `IsNullOrEmpty` return and device-colour-space skip — with the fixture's `/DeviceRGB`
fill/default `DeviceGray` stroke, all four entries take the device skip and parse zero tint
transforms. Noted what a pin for the caching shape would need to measure instead: a second
counter at the tint-transform parse site, exercised by a `/Separation` fixture with a real type-2
tint transform.

Applied the matching correction to `Docs/colour/rendering-conformance.md`'s G-12 entry (line ~591)
— same overclaim, same fix, plus the counter-semantics explanation already there was kept intact.

**Evidence:** doc/comment-only; full-suite run below confirms no regression.

## REL-1 (Minor) — `CHANGELOG.md`, `README.md`

Both docs said "G-8 … G-13" while also listing the G-14 Indexed-residual item. Changed both range
references to "G-8 … G-14" (`CHANGELOG.md` known-limitations paragraph and Version History Summary
table row; `README.md` Known Limitations section), and added the Indexed-images clause to
`README.md`'s notable-gaps list so it matches `CHANGELOG.md`'s content (it was already present in
CHANGELOG but missing from README).

**Evidence:** grep confirms no remaining "G-8 … G-13" text in either file; both now read
"G-8 … G-14" and list the same four+one gaps.

## DOC-1 (Minor, inherited) — `Docs/colour/rendering-conformance.md`, G-14 residual (a)

Updated the "Pinned 2026-07-29" note (line ~676) from "asserts both CMYK routes decline" (which
inherited P-1's false claim) to describe the corrected fixture and what it actually pins: a real
`/Lookup` string so the route reaches the base colour space; `TryToCmyk` declines in the Separation
arm's uncached tint transform (the Indexed route has no reserved-direct arm); `TryToSpotInk`
declines separately and permanently. Stated explicitly that the reserved-direct fix flips
`TryToCmyk`'s assertion red.

**Evidence:** doc-only; consistent with the P-1 fixture and comment now in
`PdfImageToCmykTests.cs`.

## Verification

1. `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~G14ResidualBaseline" -v minimal`
   → **Passed 1/1**.
2. `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~PageColorant" -v minimal`
   → **Passed 24/24**.
3. `dotnet test PdfLibrary.Tests --filter "Category!=LocalOnly" -v minimal`
   → **Passed 2661/2661, Failed 0**, Duration 15s.
4. `dotnet build PdfLibrary -c Release -v minimal`
   → **Build succeeded, 0 Warning(s), 0 Error(s)**.

No findings were skipped or disputed. All six applied as specified in the report.
