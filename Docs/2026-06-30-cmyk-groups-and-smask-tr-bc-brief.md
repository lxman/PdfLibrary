# Library handoff brief — Transparency groups + soft-mask TR/BC (for the CMYK pipeline)

**Status:** UNCOMMITTED handoff note. Drop-point for the PdfLibrary agent. Not yet a sub-project —
this is the *what* and the cross-repo contract so it can be picked up and turned into a brainstorm →
spec → plan when scheduled.

**Why this exists:** PdfLibrary's dormant CMYK overprint/soft-mask compositor (SP1–SP4, on `phase4`, local,
unpushed) deferred two fidelity items because they are **library-blocked** — the library's render-target
SPI does not surface the data PdfLibrary needs. Both are accepted ceilings today (logged in
`PdfLibrary/docs/superpowers/cmyk-pipeline-deferrals-backlog.md` §A). They should be done as **one dedicated
cross-repo sub-project staged before the SP5 `Lxman.PdfLibrary` 2.2.0 publish** — OR explicitly slipped
to a later 2.3.0 (user has accepted that SP5 can ship without them; see "Sequencing" below).

**Repos / branches (convention):** library work on a `feat/…` branch off `master`; PdfLibrary work on a
`feat/…` branch off `phase4`. Nothing is pushed until the user's gate lifts at SP5.

---

## Item 1 — Surface soft-mask `TR` (transfer function) + `BC` (backdrop color) on the SPI

### Current state (already mostly there)
- `PdfLibrary/Content/PdfSoftMask.cs` **already parses** all of it:
  - `string Subtype` ("Alpha" | "Luminosity")
  - `internal PdfStream? Group` (the mask content)
  - `public double[]? BackdropColor` — the **BC** entry
  - `internal PdfObject? TransferFunction` — the **TR** entry (an `/Identity` name or a PDF function dict)
- `PdfLibrary/Rendering/PdfRenderer.cs::RenderSoftMaskGroup` (≈ line 958) renders the group content into
  a callback target and calls `_target.RenderSoftMask(softMask.Subtype, cb)`.
- **The gap:** `IRenderTarget.RenderSoftMask(string maskSubtype, Action<IRenderTarget> renderMaskContent)`
  (`IRenderTarget.cs:140`) forwards **only `Subtype`**. `BC` and `TR` are dropped at the boundary, so
  every target hardcodes `TR=Identity` and `BC=black`. PdfLibrary's CMYK luminosity path literally seeds a
  hardcoded opaque-black backdrop (`FillBackdrop(0,0,0,1)`) and applies no TR.

### Required library change
Widen the soft-mask SPI to carry BC and an **evaluated** TR. Two constraints:
- **Do not pass a raw `PdfObject` across the SPI.** Evaluate `TransferFunction` inside the library into a
  platform-neutral form — preferred: a sampled 256-entry `byte[]`/`float[]` LUT (or `Func<double,double>`)
  with `/Identity` → `null` (meaning "no remap"). The library owns PDF-function evaluation; targets must
  not.
- **Keep backward compatibility.** Existing WPF/Skia/Avalonia targets must compile unchanged. Add a new
  overload (or a small `SoftMaskParams` record) with a **default no-op** that the existing
  `RenderSoftMask(subtype, cb)` delegates to. Suggested shape:
  ```csharp
  // New, additive. Old signature kept as a thin forwarder (TR=null, BC=null).
  void RenderSoftMask(string maskSubtype, Action<IRenderTarget> renderMaskContent,
                      double[]? backdropColor, float[]? transferLut) { RenderSoftMask(maskSubtype, renderMaskContent); }
  ```
  `RenderSoftMaskGroup` then calls the richer overload, passing `softMask.BackdropColor` and the evaluated
  `softMask.TransferFunction`. (`BackdropColor` is already public; `TransferFunction`/`Group` are
  `internal` — evaluation stays inside the library, so visibility is fine.)

### PdfLibrary consumer-side delta (the other half of the contract)
- `RecordingRenderTarget.RenderSoftMask` records a **widened** `SoftMaskPushCommand(string Subtype,
  PageDrawList Mask, float[]? TransferLut, double[]? BackdropColor)` (today it is `(Subtype, Mask)`).
- `CmykSoftMaskRenderer.RenderLuminosityMask` replaces its hardcoded `FillBackdrop(0,0,0,1)` with the
  group's `BC` (converted via `DeviceCmykConverter`; default stays black when `BC==null`), and applies
  the TR LUT as a post-map remap (`map[p] = lut[(int)(map[p]*255)]/255f`) when `TransferLut != null`.
- The existing SP4 e2e tests already assert `TR=Identity`/`BC=black` behavior; new tests add a non-black
  BC and a non-identity TR.

### Spec
ISO 32000-1 §11.6.5.2 (soft-mask dictionaries: `S`/`G`/`BC`/`TR`); §11.6.5.2 NOTE on luminosity over the
backdrop; §7.10 (PDF functions, for TR evaluation).

---

## Item 2 — Transparency group push/pop callbacks + commands (isolated / knockout)

### Current state
- No SPI exists for transparency groups. When a Form XObject with a `/Group` dictionary is painted (`Do`),
  the library renders its content **inline/flattened** into the current target — isolation and knockout
  are ignored. The **live RGB renderer defers groups too**, so this is not a CMYK-only regression.
- `PdfSoftMask.Group` is a transparency group, but that path is the *mask* group (Item 1), distinct from a
  **painted** transparency group in the content stream.

### Required library change
Add a group bracket to `IRenderTarget`, raised by the renderer around a painted transparency-group form:
```csharp
// Additive, default no-op so existing targets keep flattening (current behavior).
void BeginTransparencyGroup(TransparencyGroupAttributes attrs, PdfGraphicsState state) { }
void EndTransparencyGroup() { }
```
where `TransparencyGroupAttributes` carries at least: `bool Isolated` (`/I`), `bool Knockout` (`/K`),
and the group color space (`/CS`, may be null → inherit). The group's constant alpha (`ca`/`CA`) and
blend mode come from `state` at the `Do`. The renderer must emit `BeginTransparencyGroup` before
rendering the form's content and `EndTransparencyGroup` after — distinct from `SaveState`/`RestoreState`
(groups nest independently of q/Q, like soft masks already do).

### PdfLibrary consumer-side delta
- New `GroupPushCommand(bool Isolated, bool Knockout, string? GroupColorSpace, double Alpha,
  CmykBlendMode Blend)` / `GroupPopCommand` in `PageDrawList.cs`; recorded by `RecordingRenderTarget`.
- `CmykPageRenderer.RenderToBuffer` handles the bracket: push composites the group's content into a
  **temporary plate buffer** (isolated → fresh transparent backdrop; non-isolated → initialized from the
  current buffer), then pop composites that result back with the group's alpha + blend (knockout →
  each element composites against the group's *initial* backdrop, not the running result).
- This is the larger piece; it likely wants its own task breakdown (recorder plumbing, isolated vs
  non-isolated seeding, knockout accumulation, e2e against hand-computed plate values).

### Spec
ISO 32000-1 §11.4.7 (transparency group XObjects), §11.6.6 (knockout & isolated groups), §11.3.7.2
(group compositing formulas / backdrop removal for isolated groups).

---

## Sequencing & risk

- **SP5 does NOT depend on either item.** Display-wiring the CMYK compositor (flip
  `PdfColorToRgb.UseIccForDeviceCmyk`, re-baseline oracles, smoke, publish 2.2.0, push) works without
  these. In-scope content (fills/strokes/blends/clip/soft-masks) is unaffected. The only cost of slipping:
  these ship in a **later library release (2.3.0)** requiring a second publish, and PDFs using
  isolated/knockout groups or non-default TR/BC render at the documented fidelity ceiling — the same
  ceiling the live RGB renderer already has for groups.
- **If bundled before 2.2.0 instead:** do Item 1 first (small, additive, parsing already exists), then
  Item 2 (larger, its own task set). Both are purely additive SPI changes with no-op defaults → no break
  to existing WPF/Skia/Avalonia targets.

## Pointers (exact files)
- Library SPI: `PdfLibrary/Rendering/IRenderTarget.cs` (`RenderSoftMask` line 140; add group bracket).
- Library SMask parse: `PdfLibrary/Content/PdfSoftMask.cs` (BC/TR already present).
- Library caller: `PdfLibrary/Rendering/PdfRenderer.cs::RenderSoftMaskGroup` (≈ 958); group `Do` path is
  where `BeginTransparencyGroup`/`EndTransparencyGroup` must be raised.
- PdfLibrary recorder: `PdfLibrary.Rendering.Avalonia/RecordingRenderTarget.cs` (`RenderSoftMask` ≈ 92).
- PdfLibrary commands: `PdfLibrary.Rendering.Avalonia/PageDrawList.cs` (`SoftMaskPushCommand`, add group cmds).
- PdfLibrary CMYK consumers: `PdfLibrary.Rendering.Avalonia/Cmyk/CmykSoftMaskRenderer.cs` (TR/BC),
  `…/Cmyk/CmykPageRenderer.cs::RenderToBuffer` (group bracket).
- Backlog (the canonical deferral record): `PdfLibrary/docs/superpowers/cmyk-pipeline-deferrals-backlog.md` §A.
