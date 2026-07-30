# Final independent review — `colour/release-hooks-2.5.2` (52a68b7..3471a75)

Status: COMPLETE. Read-only review; no working-tree, index, HEAD or branch state was modified.
Verdict at the end of this file.

---

## 1. G-8 correction — white IS a genuine "paints" outcome. STOP was not required.

**Verdict: the correction was legitimate. The pin is sound as a behaviour pin. But its
explanatory comment misdescribes the mechanism, and that misdescription is a real (Minor)
defect of exactly the class this program keeps getting bitten by.**

Traced end to end:

1. `PdfRenderer.OnFill` (`PdfLibrary/Rendering/PdfRenderer.cs:582-592`) — fill space is
   `/Pattern`, not `/None`, so `CurrentState.FillPaintsNothing` is false and
   `FillWithPattern` runs. (The fixture's `/P1 scn` sets `FillPatternName`, so the
   `ResolvedFillColorSpace: "Pattern", FillPatternName: not null` arm is taken — the fill did
   NOT fall through to `_target.FillPath` with a stale colour. That rules out the
   "fill painted with an unset colour" artifact: had that happened we would have seen the
   carried-over RED, per the G-11 pin on the very same carry-over behaviour.)
2. `FillWithPattern` (`:611-630`) resolves object 5, sees `PatternType 2`, delegates to
   `FillWithShadingPattern`. Pattern DID resolve.
3. `FillWithShadingPattern` (`:680-697`) — **contains no `PaintsNothing` check**, unlike
   `OnPaintShading` (`:725-731`) which has one for the `sh` route. This is precisely G-8's
   routing claim, and it is confirmed.
4. `ShadingBuilder.Build` returns non-null: ShadingType 2, `/Coords` 4 numbers, `/Function`
   resolves to one type-2 function. Shading did NOT silently fail.
5. **Where the white actually comes from:** `ShadingBuilder.BuildColorMapper`
   (`PdfLibrary/Rendering/ShadingBuilder.cs:342-346`) calls
   `ColorSpaceResolver.BuildTintToRgb`, which at `ColorSpaceResolver.cs:414` returns **null**
   for a `/None` colourant (`if (PaintsNothing(baseArray, document)) return null;`). The
   `case "Separation"` then falls through the `break` to the fallback `ToArgbByCount`
   (`ShadingBuilder.cs:351, 367-388`). The shading `/Function` emits **one** component with
   constant value 1.0, so `ToArgbByCount`'s `case 1:` treats it as **grey 1.0 → RGB(255,255,255)**.

So white is a real paint through the pattern→shading route, covering the red backdrop. The
gap's shape is exactly as G-8 describes: the pattern route does not honour `PaintsNothing`
and therefore marks the page where §8.6.6.4 requires nothing. The ruled fix (add the
`PaintsNothing` gate to `FillWithShadingPattern`, mirroring `OnPaintShading:725`) leaves the
red backdrop, which fails `c.Green > 235 && c.Blue > 235`. **The pin flips red on the fix.**

### Finding G8-1 (Minor) — the pin's comment states a mechanism that is false

`PdfLibrary.Tests/Rendering/ColourGapBaselineTests.cs:20-24` and `:41-44`.

The comment claims *"The shading's own `[/Separation /None /DeviceRGB]` colour space paints
through ShadingBuilder anyway. Tint transform is CONSTANT black so the current behaviour has
one predictable value"*, and the corrected block says the route *"paints white, not the tint
transform's C0/C1 black."*

The tint transform is **never evaluated**. `BuildTintToRgb` refuses the `/None` space before
ever touching `PdfFunction.Create` on it. Object 8 in the fixture is dead weight — you could
replace it with `/C0 [1 0 1] /C1 [0 1 0]` and the pin would still measure white. The white is
the *component-count fallback* interpreting a 1-component tint as a DeviceGray level.

Why it matters: a future engineer reading this pin will believe the /None Separation flows
through its tint transform in the shading path. It does not — the refusal is already correct
one level down, and the leak is that `ToArgbByCount` is a silently-wrong fallback for a space
whose mapper deliberately declined. That is arguably a *second*, distinct defect worth its own
matrix row: **`BuildColorMapper` cannot distinguish "unrecognised space" from "space that
refused to build a mapper because it paints nothing"**, and conflates the two into a guess.

**Fix (docs only, no behaviour change):** rewrite the comment to say that `BuildTintToRgb`
declines the `/None` space at `ColorSpaceResolver.cs:414`, `BuildColorMapper` falls through to
`ToArgbByCount`, and the single 1.0 tint component is read as grey 1.0 = white; note that the
fixture's element-8 tint transform is not consulted. Recommend also adding a line to the G-8
entry in `Docs/colour/rendering-conformance.md` recording the `BuildColorMapper` fallback
conflation, since it is the actual leak and a fix to `FillWithShadingPattern` alone would leave
it in place for any other caller.

**This does not block release.** The assertion is measured, correct, and flips on the fix.

---

## 2. Can each pin flip red? — one pin CANNOT.

| Pin | Flips on its fix? |
|---|---|
| `NoneShadingPattern_paints_G8Baseline` | YES |
| `Mode4NoneText_establishes_no_clip_G10Baseline` | YES |
| `Pattern_without_scn_carries_over_previous_colour_G11Baseline` | YES |
| `Cs_then_sc_resolves_four_times_G12Baseline` | PARTIAL — see §3 |
| `All_image_gets_no_spot_ink_G9Baseline` | YES |
| `All_stencil_fill_gets_no_ink_G9Baseline` | YES |
| `Indexed_over_reserved_base_still_declines_G14ResidualBaseline` | **NO — dead pin** |
| `Stencil_after_bare_cs_takes_the_initial_tint_G13` | n/a (green observation, not a pin) |

### Finding P-1 (Important) — `Indexed_over_reserved_base_still_declines_G14ResidualBaseline` can never go red

`PdfLibrary.Tests/Rendering/PdfImageToCmykTests.cs:911-916`.

```csharp
PdfArray indexed = new(new PdfName("Indexed"), Separation("Cyan"),
    new PdfInteger(1), new PdfName("Lookup"));
```

`cs[3]` is a **`PdfName`**. `PdfImageToCmyk.ResolveLookup` (`PdfLibrary/Rendering/PdfImageToCmyk.cs:300-309`)
accepts only `PdfString` or `PdfStream` and returns null for everything else. So:

- `TryToCmyk` bails at **`:102`** (`if (lookup is null) return null;`) — **before** `baseObj`
  is ever inspected and before `BuildIndexedEntryToCmyk` is called at `:105`.
- `TryToSpotInk` bails at **`:348`** for the identical reason, before `sepObj` is read at `:349`.

The pin therefore measures *"an Indexed image with a malformed /Lookup declines"* — a
statement about `ResolveLookup`'s input validation that has nothing to do with G-14. The
ruled fix (a reserved-direct arm in `BuildIndexedEntryToCmyk`, mirroring
`ShadingBuilder.BuildCmykMapper:180-184`) lives strictly downstream of the `:102` bail. **After
that fix the assertion still passes, silently, forever.** It is not a hook; it is a pin nailed
to air.

The comment makes this worse by asserting the opposite: *"The /Lookup placeholder is never
consulted on the decline path"*. It is consulted — it is the *first* thing consulted, and it
is the sole cause of the decline.

**Fix (test-only, small).** Give the fixture a real lookup so the route actually reaches the
base:

```csharp
PdfArray indexed = new(new PdfName("Indexed"), Separation("Cyan"),
    new PdfInteger(1), new PdfString([0xFF, 0x00]));   // hival 1, 1 comp/entry
```

Verified this yields the intended semantics:
- **Today:** `ResolveLookup` succeeds → `BuildIndexedEntryToCmyk` (`:271`) takes the
  Separation arm → `ColorSpaceResolver.BuildTintToCmyk` → `PdfFunction.Create` on the
  helper's `/Identity` name returns null → `tint is null` → `return null` → `TryToCmyk` null.
  The pin passes **for the right reason**: the Indexed route has no reserved-direct arm, so it
  falls to the tint transform and dies there.
- **After the G-14-Indexed fix:** the reserved-direct arm short-circuits ahead of the tint
  transform (as `ShadingBuilder` already does), returns a mapper, `TryToCmyk` returns bytes →
  `Assert.Null` fails → **pin flips red**, exactly as designed.

Also correct the comment: the `/Lookup` element IS consulted, first; and note the `TryToSpotInk`
half declines for an unrelated and permanent reason (`Classify("Cyan") != Spot` →
`spotNames.Count == 0` at `:403`), so it will never flip and is decoration, not a hook.

### Notes on the pins that do flip

- **G-10** (`ColourGapBaselineTests.cs:65-79`): the text clip set at `ET` is not wrapped in
  `q/Q`, so it persists to the trailing `0 0 1 rg … re f`. 48pt "NONE" at (110,480) spans
  roughly x∈[110,246], y∈[480,514] — a strict subset of the 5px-inset scan region
  x∈[105,295], y∈[405,595]. When the mode-4 clip is implemented, red survives outside the
  glyph outlines and `ForEachPixelInRect`'s `c.Red < 20` fails. Flips. ✓
- **G-11** (`InitialColorValueTests.cs:169-175`): today `FillPatternName` is null so
  `PdfRenderer.cs:584`'s pattern arm is skipped and the carried-over red is painted. The
  ruled fix (Pattern initial colour paints nothing) leaves white → `c.Green < 20` fails. ✓
- **G-9 image** (`PdfImageToCmykTests.cs:884`): the `null` second argument is the
  `PdfDocument`, not a set of open spot planes, so there is no hidden "no planes registered"
  reason for the null — the decline is genuinely `Classify("All") != Spot` at
  `PdfImageToCmyk.cs:399-401` → `spotNames.Count == 0` at `:403`. A real /All arm returns
  ink → flips. ✓
- **G-9 stencil** (`:897`): `StencilInkFromFill(new ColorantOrigin(["All"], …))` has the same
  gate; an /All arm makes it non-null. ✓
- **G-13** (`ColourGapBaselineTests.cs:88-94`): correctly framed as a green observation with a
  STOP instruction on failure, not a limitation pin. Sound.

---

## 3. G-12 counter — the controller's self-diagnosis is CORRECT (Minor)

### Finding G12-1 (Minor) — the pin's comment claims a guarantee the pin does not provide

`PdfLibrary.Tests/Rendering/ColorSpaceResolveCountTests.cs:9-15`.

**The count of 4 is real and correctly derived.** `PdfRenderer.OnColorChanged`
(`PdfLibrary/Rendering/PdfRenderer.cs:995-1027`) calls `ResolveColorSpace` exactly twice — once
for fill (`:1009`), once for stroke (`:1025`). `cs` reaches `OnColorChanged` via the
initial-colour path (`:992`) and `sc` reaches it again, so one `cs` + one `sc` = 4 entries. Not
disputed.

**But `ResolveCallCount++` is the first statement of the method**
(`PdfLibrary/Rendering/ColorSpaceResolver.cs:37`), ahead of both the
`string.IsNullOrEmpty` return (`:39-40`) and the device-colour-space skip. With the fixture's
`/DeviceRGB` fill space — and the default `DeviceGray` stroke space — **all four entries take
the device skip and perform zero tint-transform parses.** The counter is a
*method-entry* counter, not a *work* counter.

Against G-12's two named fix shapes:

| Fix shape | Effect on the pin |
|---|---|
| Split fill/stroke resolution so `cs`/`sc` resolve only the side they set | 4 → 2. **Flips.** |
| Cache the parsed tint transform per colour-space resource | 4 → **4. Does not move.** |

The controller's assessment is confirmed. The comment at `:13-15` — *"the de-duplication design
the G-12 entry calls for (caching a parsed tint transform per colour-space resource, **or**
splitting fill/stroke resolution) must LOWER this number"* — is **false for the first of the two
alternatives it names**, and caching is the option the G-12 entry emphasises, since the
complaint is redundant re-parsing through the uncached `PdfFunction.Create`.

**What it should have measured.** A second internal counter incremented at the tint-transform
parse site (wherever `ColorSpaceResolver` calls `PdfFunction.Create` on a Separation/DeviceN
tint transform), exercised by a `/Separation` fixture with a *real* type-2 tint transform. That
observable moves under **both** fix shapes: splitting halves it, caching drops it to 1. The
existing entry counter can stay alongside it — it is a legitimate measurement of a different
thing.

**Severity Minor, not blocking.** The pin is honest about what it measures (the test name
`Cs_then_sc_resolves_four_times_G12Baseline` says "resolves", not "parses"), it does guard one
real fix shape, and it is a test-only artifact. What must be corrected is the **comment's
overclaim** and the corresponding sentence in the matrix, so nobody later concludes G-12 is
hooked when the caching half is not. Minimum acceptable action for this release: fix the
comment to say the pin guards the fill/stroke-split shape ONLY, and record in
`Docs/colour/rendering-conformance.md` that the tint-transform-caching shape remains unhooked.

---

## 4. Production changes

Only two, as the plan says. Diff confirms no other `PdfLibrary/` behaviour change.

### 4a. `ColorSpaceResolver.ResolveCallCount` — acceptable (no finding)

`PdfLibrary/Rendering/ColorSpaceResolver.cs:20-26, 37`; surfaced at
`PdfLibrary/Rendering/PdfRenderer.cs:36-37`.

- **Hot-path cost:** one non-atomic `int` increment on an auto-property; JIT inlines it to a
  field increment. Negligible against the ICC/tint work the method does on its non-trivial paths.
- **Thread safety:** the XML comment's claim holds — `_colorSpaceResolver` is a `readonly` field
  built per `PdfRenderer`, and a `PdfRenderer` is not shared across threads. Non-atomic `++` is fine.
- **Visibility:** `internal` genuinely suffices — the only consumer is
  `ColorSpaceResolveCountTests` via `InternalsVisibleTo`. It adds nothing to the public surface,
  so 2.5.2 takes on no API-compatibility obligation for it. Correct call.
- **Caveat worth knowing (not a defect):** pattern content is rendered by a *sub*-`PdfRenderer`
  (`PdfRenderer.cs:665`) with its own resolver, so its resolves are not counted by the parent's
  `ColorSpaceResolveCount`. The property's summary — "Total ResolveColorSpace calls **this
  renderer** has made" — is accurate as written.
- Overflow at 2^31 entries is not a realistic concern.

### Finding PROD-1 (Important) — the new log call can throw out of a documented never-throw catch

`PdfLibrary/Document/PageColorantReader.cs:35-43`.

```csharp
catch (Exception ex)
{
    PdfLogger.Log(LogCategory.Graphics,
        $"GetPageColorants: resource walk faulted ({ex.GetType().Name}: {ex.Message}); returning partial inventory");
}
```

The catch exists to guarantee that `GetPageColorants` — a **public** API — never throws
("spec 'Guards and stability' contract", per the comment two lines up). The new statement is
unguarded inside it, so anything it throws escapes `GetPageColorants` and breaks that guarantee.

Tracing `PdfLogger.Log` (`Logging/PdfLogger.cs`):

1. `IsCategoryEnabled(LogCategory.Graphics)` reads `_config.LogGraphics`, which **defaults to
   `false`** (`PdfLogConfiguration.cs` — plain auto-property, no initializer). On the default
   configuration `Log` returns before doing anything. **The shipped default is safe.**
2. If a consumer has enabled graphics logging **and** `_isInitialized` is false, `Log` calls
   `Initialize(new PdfLogConfiguration())`, which runs `Directory.CreateDirectory(logDirectory)`
   and `new LoggerConfiguration().WriteTo.File(...)`. Both **throw** on an unwritable path —
   `UnauthorizedAccessException`, `IOException`, `PathTooLongException`,
   `NotSupportedException`. The default `LogFilePath` is the *relative* `"logs/pdflibrary.log"`,
   so this resolves against the process CWD: a read-only container, a service running from
   `Program Files`, or an IIS app pool without write rights all hit it.
3. That state is reachable without a bug on the consumer's part, because `GetConfiguration()`
   returns the **live** `_config` instance, not a copy — `PdfLogger.GetConfiguration().LogGraphics = true;`
   flips the category on while leaving `_isInitialized` false. (Aside: that path then has
   `Initialize` overwrite `_config` with a fresh default, silently discarding the very flag that
   got it here. Pre-existing `PdfLogger` defect, out of scope, but it is the mechanism that makes
   step 2 reachable.)
4. `ex.Message` on a custom exception type that overrides `Message` can also throw, and the
   interpolated string allocates, so an `OutOfMemoryException` fault re-throws OOM from inside
   the handler.

So: three conditions must coincide (graphics logging enabled, log path unwritable, malformed
resource graph). Narrow — hence **Important, not Critical** — but it is exactly the failure the
catch was written to prevent, in a public API, and the fix is three lines.

**Fix:**

```csharp
catch (Exception ex)
{
    // Defensive: a malformed resource graph must never throw out of the public GetPageColorants
    // (spec "Guards and stability" contract). Return whatever was collected before the fault —
    // but say so, or a truncated inventory is indistinguishable from a complete one.
    // The log call is itself guarded: PdfLogger.Log can throw when logging is enabled and the
    // configured log path is unwritable, which would defeat the guarantee this catch exists for.
    try
    {
        PdfLogger.Log(LogCategory.Graphics,
            () => $"GetPageColorants: resource walk faulted ({ex.GetType().Name}: {ex.Message}); returning partial inventory");
    }
    catch { /* logging must never break the never-throw contract */ }
}
```

Using the `Func<string>` overload (`PdfLogger.cs:96-100`) also removes the unconditional string
allocation on every fault when the category is off — the codebase's own documented hot-path
pattern.

---

## 5. Release correctness — sound. The controller's out-of-plan work was necessary, not gold-plating.

**Both CHANGELOG claims about earlier commits are TRUE of the code being released.** Verified,
not taken on faith:

- *Reserved process-name Separation/DeviceN direct application* — `66301bd`
  (Merge `colour/g14-reserved-direct`, 2026-07-29) is an ancestor of `3471a75`. The code is
  present: `ShadingBuilder.PackByReservedName` / the `AllReservedProcessOrNone` arm at
  `PdfLibrary/Rendering/ShadingBuilder.cs:169-184`, plus the image/stencil siblings in
  `PdfImageToCmyk.cs:140-151`. Real.
- *Atomic-save retry on transient Windows locks* — `9a6ae6b` / merge `76cb3f7` (2026-07-29),
  both ancestors of `3471a75`. Code present: `PdfLibrary/Core/AtomicFileWriter.cs:34`
  (`maxMoveAttempts = 5, baseRetryDelayMs = 10`), `:52` `MoveWithRetry`, `:74`. The CHANGELOG's
  "Persistent locks still throw after the retry budget" matches the doc comment at `:63-74`. Real.
- **No double-credit:** the `[2.5.1]` section (2026-07-24) contains only the two XMP fixes.
  Both commits above are dated 2026-07-29, i.e. after 2.5.1 shipped. Correctly attributed to 2.5.2.

**Version coherence — correct, and the out-of-plan work was load-bearing:**

- `.github/workflows/publish-nuget.yml:43-44` sed-overwrites `<Version>` in **both**
  `PdfLibrary.csproj` and `PdfLibrary.Rendering.Wpf.csproj` from the tag. The controller's claim
  is verified. So the manual `<Version>` bumps are belt-and-braces for source-of-truth
  consistency — harmless and right.
- **`PackageReleaseNotes` is NOT sed'd.** Refreshing it was therefore *necessary*, and going
  beyond the plan here prevented a real shipping defect: 2.5.2 packages would otherwise have gone
  to nuget.org carrying release notes describing 2.5.0. Good catch by the controller.
- Bumping `Rendering.Wpf` to 2.5.2 is correct — the workflow packs it (`:69`) and its own notes
  say the packages ship in lockstep.
- **`PdfLibrary.Rendering.SkiaSharp` correctly left alone:** its csproj sets
  `<IsPackable>false</IsPackable>` (line 14) and the workflow builds but never packs it
  (`:59` build, no matching pack step). Its stale `<Version>1.1.0</Version>` ships nowhere. Not a
  miss.
- CHANGELOG has no reference-link definitions section, so there is no missing `[2.5.2]` link.

**README** (`README.md:481-488`) is consistent with the CHANGELOG `[2.5.2]` known-limitations
paragraph: same four headline gaps, same G-8…G-13 range, same doc pointer. The added
"General-purpose RGB rendering is unaffected by all of these" is accurate — every pinned gap is
on the CMYK soft-proof path or is a /None/Pattern edge case.

### Finding REL-1 (Minor) — the known-limitations lists say "G-8 … G-13" but include a G-14 item

`CHANGELOG.md` (2.5.2 Known-limitations paragraph, final clause) ends the G-8…G-13 list with
*"Indexed images over all-reserved bases still flatten"* — that is **G-14 residual (a)**, outside
the stated range. `README.md:483` uses the same "G-8 … G-13" range but does not list the Indexed
item, so the two documents disagree about what is in scope.

**Fix:** change both to "G-8 … G-14" (the matrix's G-14 entry has open residuals, so the range is
honest), or drop the Indexed clause from the CHANGELOG. Cosmetic; does not block.

---

## 6. Docs vs code — `Docs/colour/rendering-conformance.md` is accurate. One inherited error.

Spot-checked every specific factual claim in the diff:

- **Line references, the thing this program has been bitten by:** `PdfRenderer.cs:949` and
  `:1307` are both `if (!OcHidden && !CurrentState.TextPaintsNothing)` — **exact**. The doc's
  further claim that the *other* two sites, `:817` and `:1163`, gate the Type 3 routes is also
  **exact** (`RenderType3Text` / `RenderType3TextWithPositioning`). The corrections `:947→:949`
  and `:1266→:1307` are right, and the doc's explanation for why the plan predicted `:1305`
  (the G-12 counter added two lines near the top of the file) is confirmed by the diff — the
  `PdfRenderer.cs` hunk adds exactly 2 lines at `:36-37`. This is the most careful part of the branch.
- **Pin names:** all eight named in the doc exist with exactly those names in
  `ColourGapBaselineTests.cs`, `ColorSpaceResolveCountTests.cs`, `InitialColorValueTests.cs`,
  `PdfImageToCmykTests.cs`. No drift.
- **Counter semantics (G-12 entry):** the doc is *more* accurate than the test comment. It
  explicitly states the increment counts **entries, not resolutions**, that the
  `IsNullOrEmpty` return and device skip both count, and that the pinned 4 depends on the
  `/DeviceRGB` fixture taking the device-skip return. All verified correct. Its one residual
  overclaim is the same as the test's — "the de-dup design must lower it" — false for the
  caching alternative (see §3).
- **Row 5-3 Hook status:** accurately lists three surviving exclusions as unpinned by decision
  and flags the one-channel process space as next-pass candidate. Consistent with the deferral
  the coordinator described.
- **Row 5-10 Hook status:** the reasoning — no per-sample own-alternate colour exists to revert
  through, so there is no observable to pin without building the carrier design doc §8 deferred —
  is sound and matches `PdfImageToCmyk.cs:353-375`'s in-code note.
- **G-14 residual (b):** "deliberately unpinned, needs a Pellucid no-spot-buffer harness" —
  consistent with the engine/Pellucid split; correctly scoped out.
- **G-14 residual (c):** "pinned at engine level only, by design" — honest about what landed
  rather than claiming render-level coverage. Good.
- **G-8 entry:** notably honest — it states outright that *"Why the constant-black chain renders
  white rather than black is **not** explained"* and flags it as an open question about the
  fixture. That is the correct posture and I would not have flagged it. **§1 of this report now
  answers that question** (`BuildTintToRgb` declines the /None space at
  `ColorSpaceResolver.cs:414`; `BuildColorMapper` falls through to `ToArgbByCount`; the single
  1.0 tint component is read as grey 1.0). Recommend folding that answer into the G-8 entry and
  the test comment, and closing the open question.

### Finding DOC-1 (Minor, inherited) — the G-14 residual (a) doc note inherits the dead-pin defect

`Docs/colour/rendering-conformance.md`, G-14 residual (a): *"**Pinned 2026-07-29:**
`Indexed_over_reserved_base_still_declines_G14ResidualBaseline` asserts both CMYK routes
decline."* Per **Finding P-1** the pin declines for an unrelated reason and can never flip, so
the matrix currently claims a hook it does not have. Fix alongside P-1; if P-1's fixture
correction is applied, this note becomes true as written.

---

## Triage of the known deferred items

| Item | Blocks release? |
|---|---|
| 3 xUnit analyzer warnings, `PdfLibrary.Tests/Core/AtomicFileWriterTests.cs:123,127` (xUnit1031, xUnit1051 ×2) | **No.** Test-project only; `PdfLibrary` builds clean in Release, and the test project is not packed. Warnings, not errors — the publish workflow's build and test steps both pass. Genuinely pre-existing (not in this diff). Fix in a housekeeping pass. |
| No `2.5.1` row in the CHANGELOG Version History Summary table | **No.** Cosmetic, pre-existing. Worth noting that adding the 2.5.2 row makes the gap more conspicuous (the table now reads 2.5.0 → 2.5.2), so this is a good moment to add the missing row — but it is a documentation nicety, not a release gate. |
| Row 5-3's one-channel process space residual, unpinned | **No.** Correctly deferred, and — importantly — *recorded as deferred in the matrix itself* with a named next-pass owner. That is the discipline working, not a lapse. |

None of the three blocks release.

---

## VERDICT

**Merge and release as 2.5.2 — after two small, test-and-comment-only fixes. The shipped library
code is sound.**

The production surface of this branch is two changes totalling ten lines, and neither alters
default behaviour: an `internal` diagnostic counter that is invisible outside the assembly, and a
log statement that is a no-op under the default configuration. The version, CHANGELOG, README and
`PackageReleaseNotes` work is correct and the controller's out-of-plan `PackageReleaseNotes`
refresh prevented a real defect. Both CHANGELOG claims about earlier commits verify against the
actual code. The matrix's line references — historically this program's weak point — are exact.
Given that Tasks 3-9 had no independent review, this holds up considerably better than it had any
right to.

**Required before merge (both test/comment-only, no production change, ~20 lines total):**

1. **P-1 (Important)** — `PdfImageToCmykTests.cs:911-913`: replace `new PdfName("Lookup")` with
   `new PdfString([0xFF, 0x00])`. Without this, one of the seven pins this branch exists to
   create is nailed to air and will never fire. Correct its comment (the `/Lookup` element IS
   consulted, first) and the matching G-14 residual (a) note in the matrix (**DOC-1**).
2. **PROD-1 (Important)** — `PageColorantReader.cs:35-43`: wrap the `PdfLogger.Log` call in its
   own `try { } catch { }` and switch to the `Func<string>` overload. Three lines. Restores the
   never-throw guarantee unconditionally rather than only under the default log configuration.

**Recommended in the same pass (Minor, all comment/doc):**

3. **G8-1** — correct the `NoneShadingPattern_paints_G8Baseline` comment: the tint transform is
   never evaluated; `BuildTintToRgb` declines the /None space at `ColorSpaceResolver.cs:414` and
   `BuildColorMapper` falls through to `ToArgbByCount`, which reads the single 1.0 tint component
   as grey 1.0 = white. Close the matrix's stated open question with this answer, and consider a
   new matrix row for the real underlying leak: `BuildColorMapper` cannot distinguish
   "unrecognised colour space" from "colour space that refused a mapper because it paints
   nothing", and guesses in both cases.
4. **G12-1** — correct the `ColorSpaceResolveCountTests.cs:13-15` comment and the matching
   sentence in the G-12 matrix entry: the pin guards the fill/stroke-split fix shape **only**;
   the tint-transform-caching shape leaves the count at 4 and remains unhooked. Optionally add a
   tint-transform parse counter with a `/Separation` fixture, which would guard both shapes.
5. **REL-1** — reconcile "G-8 … G-13" with the G-14 Indexed item listed under it, and make
   CHANGELOG and README agree.

**If you would rather ship today:** items 1 and 2 are the only ones I would insist on, and only
item 2 touches shipped code. Item 1 does not affect the package at all — it affects whether the
next colour fix gets caught — so a defensible alternative is to ship 2.5.2 as-is and land P-1,
DOC-1, G8-1, G12-1 and REL-1 as a follow-up before any further colour work begins. I would not
recommend deferring PROD-1 past this release, since it is three lines and restores a documented
public-API guarantee.

