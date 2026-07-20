# PdfLibrary 2.5.0 Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `Lxman.PdfLibrary` 2.5.0 + `Lxman.PdfLibrary.Rendering.Wpf` 2.5.0 to nuget.org with accurate, complete release documentation.

**Architecture:** No code changes — origin/master (`5e0dfa6`) already contains all 224 commits since v2.4.0. This plan finalizes the docs that gate the release, then cuts a GitHub release whose `release: published` event triggers `.github/workflows/publish-nuget.yml` (CI builds, runs the `Category!=LocalOnly` test gate, packs, and pushes both packages; the package version is taken from the tag name).

**Tech Stack:** .NET 8/9/10, GitHub Actions, NuGet, Keep-a-Changelog Markdown.

## Global Constraints

- **Version = `2.5.0`** (minor bump: new additive public APIs, no breaking changes). Tag = `v2.5.0`.
- **Public scope only.** Docs describe only the two published packages (`Lxman.PdfLibrary` core + `Lxman.PdfLibrary.Rendering.Wpf`). Do **not** document `Lxman.PdfLibrary.Rendering.Skia` APIs (`PdfPage.RenderTo(SKCanvas)`, `RenderToImage`) — that package is not published.
- **Do not advertise the display-list IR** (`PageDrawList`, `DrawCommand`, `RecordingRenderTarget`) as a supported public contract in the README (decision R2 — keep evolution freedom; Pellucid consumes it via version-pin only).
- **CHANGELOG follows Keep-a-Changelog**; README matches the existing section/snippet style.
- **The release cut (Task 5) is user-gated and irreversible.** Do not create the GitHub release without explicit approval. NuGet packages can only be unlisted, never deleted.
- **Ask before pushing** the docs commit and before cutting the release (standing rule).

---

### Task 1: Finalize CHANGELOG.md 2.5.0 section

**Files:**
- Modify: `CHANGELOG.md` (the `## [Unreleased]` section at lines 7–31, and the Version History Summary table at line ~477).

**Interfaces:**
- Consumes: nothing.
- Produces: a dated `## [2.5.0]` section that Task 5's GitHub release notes reference.

- [ ] **Step 1: Rename the section and open a fresh Unreleased**

Change `## [Unreleased]` (line 7) to:

```markdown
## [Unreleased]

## [2.5.0] - 2026-07-20
```

The existing Added/Fixed bullets (embedded-files read API, PDF/A-3 authoring, builder standard-14 fix) stay under `## [2.5.0]`.

- [ ] **Step 2: Add the missing new-public-API entries to the `### Added` block under [2.5.0]**

Append these bullets after the existing Added entries:

```markdown
- **Tagged-PDF structure-tree read API** — `PdfDocument.GetTagTree()` returns a read-only
  `TagTree` (`IsTagged` + `Roots`); each `TagNode` carries `Type`/`RawType`/`IsStandard`,
  `Alt`/`ActualText`/`Expansion`/`Language`/`Title`, `PageIndex`, `Text`, and `Children` —
  the logical-structure hierarchy for accessibility inspection.
- **Output-intent read API** — `PdfDocument.GetOutputIntents()` returns the document's
  `OutputIntentDescriptor`s (ICC destination-profile output intents, e.g. for PDF/A and PDF/X).
- **Page colorant inventory** — `PdfDocument.GetPageColorants(pageIndex)` returns the
  `PageColorant`s used on a page: name, kind (Process/Spot/All/None), alternate space, tint
  ramp, and a solid sRGB preview.
- **Expanded conformance coverage** — substantial additional PDF/A-2b clause coverage (content
  operators, object/token spacing, stream `/Length` framing, JPEG2000, ICC-CMYK overprint,
  XObject & image dictionaries, embedded files, permissions, font-program/dictionary rules,
  ExtGState, rendering intent, Separation/DeviceN), PDF/UA-1 brought to full machine-checkable
  parity, and PDF/X-4 spot-colour rules — validated at zero false positives across the veraPDF
  corpus. The `Preflighter.Check` API is unchanged; detection is strictly improved.
```

- [ ] **Step 3: Add the user-visible fixes to the `### Fixed` block under [2.5.0]**

Append after the existing builder-Helvetica fix:

```markdown
- **CFF text no longer renders blank** — CFF DICT real operands with `E`/`E-` exponent nibbles
  now decode correctly; symbolic CFF glyphs resolve via the font's built-in Encoding.
- **Rotated/mirrored text advances** — per-glyph advances are no longer reversed under a
  rotated or mirrored text matrix.
- **Glyph advance widths** — Type 3 advances scale by `/FontMatrix` (not a hardcoded 1/1000);
  simple TrueType fonts lacking `/Widths` fall back to Standard-14 AFM widths; simple-CFF and
  CIDFontType0 advance widths resolve via the charset GID / `defaultWidthX`.
- **Colour** — a DeviceGray alternate separates K-only to match the fill path (GWG230); 16-bit
  DeviceCMYK images render through a native CMYK plane.
- **Rendering robustness** — content CTM accumulates in double precision so max-magnitude reals
  no longer blank the page; `CropBox` is clamped to the `MediaBox` intersection (ISO 32000-1
  §14.11.2); Form XObjects inherit the full graphics state (fill colour), not just CTM/alpha;
  shading patterns (PatternType 2 dictionaries) resolve correctly; every pattern cell whose
  BBox overlaps the fill region is tiled.
- **Parser resilience** — recover from a stream `/Length` that overshoots the real `endstream`;
  treat `"0 0 R"` as the null object instead of throwing.
```

- [ ] **Step 4: Add the Version History Summary row**

In the table starting at line ~477, add after the last existing row (2.4.0):

```markdown
| 2.5.0 | 2026-07-20 | Embedded-files read API + PDF/A-3 authoring (`AddEmbeddedFile`/`SetRawXmp`/`AddOutputIntent`); Tagged-PDF tree, output-intent, and page-colorant read APIs; broad PDF/A-2b + full PDF/UA-1 + PDF/X-4 conformance coverage (0 false positives); CFF/text/width and rendering-robustness fixes. |
```

- [ ] **Step 5: Verify the CHANGELOG reads cleanly**

Run: `sed -n '7,60p' CHANGELOG.md`
Expected: a `## [Unreleased]` empty section, then `## [2.5.0] - 2026-07-20` with populated Added/Fixed blocks; no leftover `[Unreleased]` bullets.

(No commit yet — commit happens in Task 4 after the user review gate.)

---

### Task 2: Update README.md with the new public APIs

**Files:**
- Modify: `README.md` (Features section ~line 76; Quick Start section ~line 233).

**Interfaces:**
- Consumes: public signatures — `PdfDocument.Load(string)`, `GetEmbeddedFiles()` → `IReadOnlyList<EmbeddedFileDescriptor>` (`.FileName`, `.MimeType`, `.AfRelationship`, `.HasData`, `.GetDataBytes()`), `GetTagTree()` → `TagTree` (`.IsTagged`, `.Roots`), `doc.Edit()` → editor with `AddEmbeddedFile(PdfEmbeddedFileSpec)`, `AddOutputIntent(byte[], string, string?, string)`, `.Save(string)`.
- Produces: nothing downstream.

- [ ] **Step 1: Add a "Document Inspection" Features subsection**

After the `### PDF Conformance Preflight` block (ends ~line 81), insert:

```markdown
### Document Inspection & Metadata
- Read embedded/attached files (`PdfDocument.GetEmbeddedFiles()`) — name, MIME subtype, `/AFRelationship`, and decoded bytes; never throws on malformed attachments
- Read the Tagged-PDF logical-structure tree (`PdfDocument.GetTagTree()`) for accessibility inspection
- Read ICC output intents (`PdfDocument.GetOutputIntents()`) and per-page colorant inventory (`PdfDocument.GetPageColorants(pageIndex)`)
```

- [ ] **Step 2: Add a "Reading Embedded Files" Quick Start snippet**

After the `### Text Extraction` block (ends ~line 248), insert:

```markdown
### Reading Embedded Files (e.g. Factur-X attachments)

```csharp
using PdfLibrary.Structure;

using var doc = PdfDocument.Load("invoice.pdf");
foreach (var f in doc.GetEmbeddedFiles())
{
    Console.WriteLine($"{f.FileName} ({f.MimeType}) — {f.AfRelationship}");
    if (f.HasData)
        File.WriteAllBytes(f.FileName ?? "attachment.bin", f.GetDataBytes()!);
}
```
```

- [ ] **Step 3: Add a "PDF/A-3 Authoring" Quick Start snippet**

After the `### Editing an Existing PDF` block, insert:

```markdown
### PDF/A-3 Authoring (embedding files + output intent)

```csharp
using PdfLibrary.Structure;
using PdfLibrary.Editing;

using var doc = PdfDocument.Load("input.pdf");
var edit = doc.Edit();

edit.AddEmbeddedFile(new PdfEmbeddedFileSpec
{
    Name = "factur-x.xml",
    Data = File.ReadAllBytes("factur-x.xml"),
    MimeType = "text/xml",
    Description = "Factur-X invoice data"
});
edit.AddOutputIntent(File.ReadAllBytes("sRGB.icc"), "sRGB IEC61966-2.1");

edit.Save("output.pdf");
```
```

- [ ] **Step 4: Verify README renders and snippets match signatures**

Run: `rg -n "GetEmbeddedFiles|GetTagTree|AddEmbeddedFile|AddOutputIntent|Document Inspection" README.md`
Expected: the new Features subsection and both snippets present; method names match Task 2 Interfaces exactly.

(No commit yet.)

---

### Task 3: Align the Rendering.Wpf on-disk version

**Files:**
- Modify: `PdfLibrary.Rendering.Wpf/PdfLibrary.Rendering.Wpf.csproj:27`.

**Interfaces:** none.

- [ ] **Step 1: Bump the version for consistency**

Change line 27 from `<Version>2.4.0</Version>` to `<Version>2.5.0</Version>`.
(Cosmetic: CI seds this from the tag at publish, but on-disk should match reality.)

- [ ] **Step 2: Verify**

Run: `rg -n "<Version>" PdfLibrary/PdfLibrary.csproj PdfLibrary.Rendering.Wpf/PdfLibrary.Rendering.Wpf.csproj`
Expected: both report `2.5.0`.

---

### Task 4: User review gate → commit docs → verify CI

**Files:** none new — commits Tasks 1–3.

- [ ] **Step 1: Present the drafts to the user (decision R1)**

Show the finalized CHANGELOG `[2.5.0]` section and the README additions. Wait for approval. If changes are requested, apply them and re-present.

- [ ] **Step 2: Build to confirm the tree is releasable**

Run: `dotnet build PdfLibrary/PdfLibrary.csproj -c Release`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit the docs (ask before pushing)**

```bash
git add CHANGELOG.md README.md PdfLibrary.Rendering.Wpf/PdfLibrary.Rendering.Wpf.csproj
git commit -m "docs(release): 2.5.0 changelog + README public-API updates; align Wpf version

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016jS9peQ4oGzDzdbTLYF18P"
```

- [ ] **Step 4: Push after explicit approval, then confirm CI is green**

Run (after approval): `git push origin master`
Then: `gh run list --branch master --limit 3`
Expected: the `ci.yml` run for the docs commit completes successfully before proceeding to Task 5.

---

### Task 5: Cut the v2.5.0 release (USER-GATED, IRREVERSIBLE)

**Files:** none — creates a GitHub release/tag.

**Interfaces:**
- Consumes: the docs commit on `origin/master`; `.github/workflows/publish-nuget.yml` (`release: published` trigger; version from tag).

- [ ] **Step 1: Confirm explicit go-ahead to publish**

Re-confirm with the user that publishing 2.5.0 to nuget.org (irreversible) is approved. Do not proceed otherwise.

- [ ] **Step 2: Create the release with the changelog notes**

```bash
gh release create v2.5.0 --repo lxman/PdfLibrary --target master \
  --title "v2.5.0" \
  --notes-file <(sed -n '/## \[2.5.0\]/,/## \[2.4.0\]/{/## \[2.4.0\]/!p;}' CHANGELOG.md)
```

- [ ] **Step 3: Watch the publish workflow**

Run: `gh run watch $(gh run list --workflow=publish-nuget.yml --limit 1 --json databaseId -q '.[0].databaseId')`
Expected: the workflow's test gate passes, then both packages push. If the test gate fails, nothing publishes — fix and re-run rather than force-publishing.

- [ ] **Step 4: Verify both packages are live on nuget.org**

```bash
curl -s "https://api.nuget.org/v3-flatcontainer/lxman.pdflibrary/index.json" | tr ',' '\n' | grep 2.5.0
curl -s "https://api.nuget.org/v3-flatcontainer/lxman.pdflibrary.rendering.wpf/index.json" | tr ',' '\n' | grep 2.5.0
```
Expected: `2.5.0` listed for both (allow a few minutes for indexing).

---

## Self-Review

**Spec coverage:**
- Finalize CHANGELOG → Task 1. ✓
- README public APIs (GetEmbeddedFiles, GetTagTree, GetOutputIntents, GetPageColorants, PDF/A-3 authoring) → Task 2. ✓
- Wpf version alignment (R4) → Task 3. ✓
- Docs review gate (R1) → Task 4 Step 1. ✓
- Version 2.5.0, no 3.0.0 (R3) → Global Constraints. ✓
- Exclude Rendering.Skia APIs + don't advertise PageDrawList (R2) → Global Constraints. ✓
- Cut release, user-gated + irreversible → Task 5. ✓

**Placeholder scan:** All CHANGELOG bullets and README snippets contain literal final text; commands are concrete. No TBD/TODO. ✓

**Type consistency:** `GetEmbeddedFiles`/`EmbeddedFileDescriptor.HasData`/`GetDataBytes()`, `GetTagTree`/`TagTree`, `AddEmbeddedFile(PdfEmbeddedFileSpec{Name,Data,MimeType,Description})`, `AddOutputIntent(byte[],string,…)`, `PdfDocument.Load`/`doc.Edit()`/`Save` — all match the signatures verified in the engine. ✓
