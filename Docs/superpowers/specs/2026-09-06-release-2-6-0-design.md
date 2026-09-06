# Release 2.6.0 — ship the fixes, keep the repair side out of the public API — design

_2026-09-06. Measured against PdfLibrary master `b73f6c3` (2026-09-02), 441 commits past v2.5.2.
Produces `Lxman.PdfLibrary` 2.6.0 and `Lxman.PdfLibrary.Rendering.Wpf` 2.6.0. This is the last
release before the compliance extraction; the release after that one is 3.0.0._

_Reviewed 2026-09-06: D3, D9 and D13 approved as written. D7 overridden: symbol packages keep
shipping. The text below reflects the reviewed state._

## 1. Why this release exists

Two things changed since the last time a release was considered.

**There is a production consumer.** The nuget.org owner statistics for the last six weeks attribute
15,678 of 2.5.0's 15,948 downloads to the .NET SDK's own restore task (NuGet client 7.6, then 7.9).
That is `dotnet restore` in cache-less environments, between 150 and 700 downloads a day since late
July and about 200 a day on 2026-09-06, and it never followed 2.5.1 or 2.5.2. Someone has pinned 2.5.0
in a pipeline that stands up a fresh environment per run. Since 2.5.0 the engine fixed a
save-corruption class bug (issue 80: every xref entry was written with generation 0 regardless of
the object's real generation), hex strings that did not survive a save (issue 57), real-number
precision loss, and a recursive page-tree traversal. A consumer on 2.5.0 has none of those.

**The public surface grew by 1,411 members without a decision.** Master today exposes 135 new
public types, 111 of which are the PDF/A repair machinery and the font-remediation planner. None of
that has ever been in a package. The standing policy is that PdfLibrary stays a limited subset of
Pellucid, and the follow-on program moves preflight and remediation source into the private
Pellucid repo. A 2.6.0 that publishes the repair API would lock it into the 2.x line under semver
and then break it in 3.0.0.

So: 2.6.0 ships master's fixes and the deliberately-public additions, hides the repair side, and
leaves the engine's documented API exactly "2.5.2 plus what we chose to add".

## 2. Goals and non-goals

Goals:

1. A 2.6.0 that a 2.5.x consumer can upgrade to with no source or binary change on their side.
2. No repair or remediation API in the documented public surface or in the shipped XML docs.
3. The public API becomes an explicit, enforced artifact so the next 1,411 members cannot leak by
   accident.
4. CHANGELOG, README, and package release notes accurate for everything public since 2.5.2.

Non-goals (each has or will have its own spec):

- Moving any code out of the engine repo. This release hides; it does not extract.
- 3.0.0, the XMP type-forwarder removal, or any pre-announcement of the conformance API leaving.
- Unlisting 2.4.0–2.5.2. Their compiled rules are public and stay that way.
- Preventing InternalsVisibleTo name spoofing. The engine is unsigned; that is accepted.

## 3. Facts the design rests on

All measured on 2026-09-06. Re-measure before quoting them elsewhere.

### 3.1 Public API delta, 2.5.2 package vs master Release build

Measured by reflecting over `lib/net8.0/PdfLibrary.dll` from the 2.5.2 package and the `bin/Release/net8.0`
build of master, one process per assembly, exported types only, declared members only.

| | Entries |
|---|---|
| 2.5.2 surface | 1,968 |
| master surface | 3,343 |
| Removed | 36 |
| Added | 1,411 (135 types) |

The 36 removals are 34 XMP entries and 2 `ImageCommand` entries. All 34 XMP entries exist with
identical signatures in `PdfLibrary.Xmp.dll` and are reached through `PdfLibrary/TypeForwards.cs`,
so they are moves, not breaks. The `ImageCommand` pair is a real break; see 3.3.

Of the 1,411 additions, 1,198 members across 111 types are repair-side (Appendix B) and 213 members
across 24 types are library capability (Appendix C).

### 3.2 The widened interface is safe

`ISystemFontProvider` gained `Resolve(FontRequest)` and `EnumerateFaces()`. Both carry default
implementations, so an external implementer compiled against 2.5.2 keeps working. Not a break.

### 3.3 `ImageCommand` is a break as it stands

`PdfLibrary.Rendering.ImageCommand` is a public positional record in the render SPI. Its primary
constructor gained a tenth parameter, `byte[]? ProofCmyk = null`. Source consumers constructing it
still compile because the parameter is optional; consumers deconstructing it with nine targets do
not, and any consumer compiled against 2.5.2 fails at runtime with a missing-method exception on
either the constructor or `Deconstruct`. This release restores both nine-parameter shapes as explicit
overloads delegating to the primary.

### 3.4 The shipped XML documentation already describes internal members

The C# compiler writes doc comments for every documented member regardless of accessibility. The
2.5.2 package's `PdfLibrary.xml` contains 86 `<member>` entries under `PdfLibrary.Conformance.Rules`,
every one of them an internal type, each with its ISO clause reasoning in prose. Making the repair
API internal changes nothing about what the XML file says. This is the one place where "hide" needs
more than a keyword.

### 3.5 What Pellucid uses, and from where

Symbols that this release hides are used outside the engine by exactly these Pellucid assemblies:

| Hidden family | Pellucid consumers (non-test) | Pellucid test consumers |
|---|---|---|
| `PdfDocumentEditor.Preview*/Repair*` and their records | Pellucid.Remediation | Pellucid.Remediation.Tests, Pellucid.App.Tests |
| `XmpConformance.PreviewExtensionSchemaStructureRepairs/RepairExtensionSchemaStructure` | Pellucid.Remediation | Pellucid.Remediation.Tests, Pellucid.App.Tests |
| `Fonts.Remediation.*` (planner and proposals) | Pellucid.Core, Pellucid.App, Pellucid.Remediation | Pellucid.Core.Tests, Pellucid.App.Tests, Pellucid.Remediation.Tests, Pellucid.Rendering.Avalonia.Tests |
| Editor font-program mutations (`EmbedProgram`, `SetToUnicode`, `ReplaceProgramBytes`, `ReplaceCompositeProgram`, `SetCidSet`, `SetCharSet`, `SetCidToGidMapIdentity`, `RemoveSymbolicEncoding`, `HasFont`, and the two `Can*` queries) | Pellucid.Core, Pellucid.Remediation | Pellucid.Core.Tests, Pellucid.App.Tests, Pellucid.Remediation.Tests, Pellucid.Rendering.Avalonia.Tests |

Pellucid.Preflight uses only `Preflighter.Check` and `ConformanceClaim.Read`, both of which stay
public. Pellucid.Automation uses nothing that this release hides. Pellucid.Core's one call into a
repair partial is `PreviewDocumentProof()`, which is read-only classification and stays public.
Nothing in the engine repo outside `PdfLibrary` and `PdfLibrary.Tests` uses any hidden symbol;
`PdfLibrary.Examples/12-PreflightPdf` uses `Preflighter` only.

### 3.6 Release mechanics as they exist

- `publish-nuget.yml` fires on a published GitHub release, rewrites `<Version>` in both shipped csproj
  from the tag, builds Release, runs `PdfLibrary.Tests` with `Category!=LocalOnly`, packs, and pushes
  both `.nupkg` and both `.snupkg`. Parity tests skip cleanly without a corpus (the 2.5.2 run: 2,656
  passed, 5 skipped).
- The core package grew from 3.0 MB (2.5.2) to 5.4 MB on master: a larger assembly plus bundled
  Adobe CMaps, AFM metrics, and the CMYK profile.
- Pellucid consumes the engine by project reference at the SHA in `ci/dependencies.json`
  (currently `b73f6c3`). Its published-package canary job builds against `LxmanPdfLibraryVersion`
  in `Directory.Build.props`, currently 2.5.1, and is non-gating.

## 4. Decisions

Each decision names the alternative it rejects. D7 was overridden at review; nothing else in the
design depended on it.

**D1. The version is 2.6.0.** Nothing in the 2.5.2 surface is removed once 3.3 is fixed; the release
is additive. 3.0.0 is reserved for the extraction.

**D2. The hidden set is the repair side, defined by family, not by file.** Everything in Appendix B
becomes `internal`: the `PdfDocumentEditor` `Preview*`/`Repair*` methods with their candidate,
refusal, preview, report, repair and owner-kind types; `ProhibitedAction*`; the `NameUtf8Repair*`
family; the `Fonts.Remediation` namespace whole; `XmpConformance`'s two structure-repair methods
and their two result types; and the editor's font-program mutation methods listed in 3.5. Rejected
alternative: hiding by file, which would drag `PreviewDocumentProof` and `SetFileId` along.

**D3. Everything else new since 2.5.2 is published, deliberately.** Appendix C: the font
substitution and inventory API (`SystemFontLocator.Resolve`/`EnumerateFaces`, `FontRequest`,
`FontMatch`, `SystemFontFace`, `BundledStandard14Provider`, `FontInventory`, `FontInventoryEntry`,
`FontDescriptorValues`, `FontDescriptorMetrics`, `FontId`, `FontKind`, `FontProgramClassifier`,
`ClassifiedProgram`, `FontProgramFormat`), CID-to-Unicode (`CidCMap`, `AdobeCidToUnicode`,
`ToUnicodeCodespace`, `ToUnicodeCMapWriter`), metadata (`PdfMetadata.Language`, `.Trapped`,
`PdfTrapped`), editing (`SetFileId`, `SetAnnotationFlags`, `ConsolidateOutputIntents`,
`ReplaceOutputIntentProfile`, `PdfFormFields.HasXfa`, `PdfAnnotationInfo.Flags`,
`PdfDocumentEditor.Document`), the fact API (`PreviewDocumentProof`, `DocumentProofPreview`),
conformance reads (`ConformanceClaim`, `OutputIntentProfileValidator`,
`XmpConformance.ClassifyProperties`/`ModernEquivalentOf`, `XmpPropertyVerdict`,
`XmpModernEquivalent`, `Preflighter.Check(doc, profile, sourceBytes)`), and render-SPI state
(`ImageCommand.ProofCmyk`, `PdfGraphicsState.ResolvedFillProofCmyk`/`ResolvedStrokeProofCmyk`,
`DeviceCmykConverter.Naive`, `RecordingRenderTarget.Record(page, scale, fontProvider)`). These are
rendering, extraction and authoring quality; none carries compliance logic. Rejected alternative:
hiding everything new. It would need the same InternalsVisibleTo list and would hamstring the
library for no protection gain.

**D4. `PreviewDocumentProof` stays public.** It classifies signature byte ranges and catalog
permissions without mutating, is what Pellucid.Core needs, and is what stays in the engine after
the extraction. It lives in the Permissions partial next to two methods that go internal; the
boundary is per member.

**D5. InternalsVisibleTo grants to six Pellucid assemblies, and the csproj policy text is rewritten.**
Grants: `Pellucid.Core`, `Pellucid.App`, `Pellucid.Remediation`, `Pellucid.Core.Tests`,
`Pellucid.App.Tests`, `Pellucid.Remediation.Tests`. `Pellucid.Rendering.Avalonia.Tests` already has
one. The existing comment says "prefer widening the public surface; InternalsVisibleTo to
production code means the public API does not say what a real consumer needs". That sentence is
now false for this repo: internal-plus-grant is the mechanism for capability that Pellucid needs
and the library must not publish. The comment is rewritten to say so, to list the grants under
that rule, and to note that the grants are a bridge the extraction removes. Rejected alternative:
a separate unpublished assembly. That is most of the extraction's work and does not serve the
extraction's actual goal (source visibility), so it is not worth doing twice.

**D6. Shipped XML documentation is filtered to public members at pack time.** A build step rewrites
`PdfLibrary.xml` and `PdfLibrary.Xmp.xml` so that every `<member>` entry whose declaring type is not
public is removed, and every `M:`/`P:`/`F:`/`E:` entry on a public type whose member name has no public
declaration on that type is removed. Name-level matching is accepted (an internal overload sharing a
public name keeps its entry); the hidden editor methods in 3.5 share no names with public members,
so the gap is theoretical. This also drops the 86 rule entries that 2.5.x has been shipping.
Rejected alternatives: not shipping XML docs at all (kills IntelliSense for the real API); an
allowlist by regex (drifts).

**D7. Symbol packages keep shipping.** Reviewed decision, 2026-09-06: the PDB stays available.
`IncludeSymbols`, `SymbolPackageFormat`, SourceLink and the two `.snupkg` push lines in
`publish-nuget.yml` are untouched. The trade accepted with it: a portable PDB carries local names
and sequence points for the hidden code, so a decompiler with the PDB reads the repair logic as
source minus comments. The comments, which hold the clause reasoning, are in the source and the
filtered-out XML docs only. The original proposal (drop symbols until the extraction empties the
DLL) was rejected because consumers stepping into library code is a deliberate feature.

**D8. `ImageCommand` gets its 2.5.2 shapes back.** An explicit nine-parameter constructor and a
nine-target `Deconstruct`, both delegating to the ten-parameter primary. Verified by the API diff in
6.1 reporting zero removals outside the XMP forwarders. Override by declaring the break accepted;
that would make the release 3.0.0 under a strict reading, which D1 rejects.

**D9. The public API becomes a checked-in file.** `Microsoft.CodeAnalysis.PublicApiAnalyzers` on
`PdfLibrary`, `Xmp` and `PdfLibrary.Rendering.Wpf`, with `PublicAPI.Shipped.txt` generated from the
2.6.0 surface after D2 and D8 land, `PublicAPI.Unshipped.txt` empty at tag time, and RS0016/RS0017
(symbol not in the file / file names a missing symbol) as build errors. From 2.6.0 on, a new public
member is a diff in a text file that a reviewer sees. Rejected alternative: a reflection test
asserting no public name matches `Repair|Remediation|Proposal`. It catches only what it was told to
look for; 1,411 members got through because nothing was looking.

**D10. The release gate is the measured surface, not a review.** `tools/api-surface/` gains the two
scripts used for 3.1 (`dump.ps1` reflects one assembly to a sorted text file in a child process;
`diff.ps1` compares two dumps). `Docs/Releasing.md` gains a mandatory step: dump the previous
published package from the NuGet cache and the Release build, diff, and confirm zero removals other
than the forwarded XMP entries and that every addition is in the changelog's Added section.

**D11. The tag is the SHA Pellucid pins.** Pellucid's `ci/dependencies.json` moves to the 2.6.0
release commit before the tag is created, so the source build Pellucid CI runs and the package the
workflow publishes are the same bytes. Rejected alternative: tagging first and re-pinning after; it
allows the two to diverge by a commit nobody noticed.

**D12. Post-release, Pellucid bumps `LxmanPdfLibraryVersion` to 2.6.0.** The canary job then proves
two things at once: the published package carries the grants (every Pellucid assembly that uses a
hidden symbol compiles against it) and the published surface matches what Pellucid was built
against. Until that job is green the release is not done.

**D13. 2.6.0 does not pre-announce 3.0.0's conformance removal.** The extraction has no spec yet
and the consumer is pinned; announcing direction before it is designed helps nobody. The changelog
keeps the existing notice that the XMP forwarders leave at 3.0.0. Override by adding a one-line
notice under a "Deprecation notice" heading.

**D14. Only public behaviour goes in the changelog.** Work that produced hidden API is documented
where it changed something a consumer can observe (save fidelity, preflight detection, rendering)
and never as "Added" API. Appendix A is the coverage checklist.

## 5. Design

### 5.1 Visibility changes in `PdfLibrary`

Mechanical, compiler-driven, in one commit:

- Every type in Appendix B: `public` → `internal` on the declaration. Records, enums and sealed
  classes alike. Nested public types inside them follow automatically.
- Every method in the "members on types that stay public" list of Appendix B: `public` → `internal`.
- Build. Each CS0050/CS0051/CS0053 (inconsistent accessibility) names a public signature that leaks
  a now-internal type; the fix is always to make the *signature owner* internal too, never to make
  the type public again. Expected sites: none beyond Appendix B, since the publish set was checked
  for references into the hide set, but the compiler is the authority.
- `PdfLibrary.Tests` already has a grant and needs no change. Its repair tests keep running.

`ConformanceContext`, the rules, `ContentWalk`, and the used-glyph collectors are internal already
and are untouched.

### 5.2 `ImageCommand` compatibility overloads

The record today, in `PdfLibrary/Rendering/PageDrawList.cs`:

```csharp
public sealed record ImageCommand(
    byte[] Rgba, int Width, int Height, AlphaMode Alpha, Matrix3x2 Ctm, PdfGraphicsState State,
    byte[]? Cmyk = null, (bool C, bool M, bool Y, bool K)? OverprintPlates = null,
    SpotImageInk? Spots = null, byte[]? ProofCmyk = null) : DrawCommand;
```

It gains, in the record body, the two 2.5.2 shapes:

```csharp
public ImageCommand(byte[] Rgba, int Width, int Height, AlphaMode Alpha, Matrix3x2 Ctm,
    PdfGraphicsState State, byte[]? Cmyk, (bool C, bool M, bool Y, bool K)? OverprintPlates,
    SpotImageInk? Spots)
    : this(Rgba, Width, Height, Alpha, Ctm, State, Cmyk, OverprintPlates, Spots, ProofCmyk: null) { }

public void Deconstruct(out byte[] Rgba, out int Width, out int Height, out AlphaMode Alpha,
    out Matrix3x2 Ctm, out PdfGraphicsState State, out byte[]? Cmyk,
    out (bool C, bool M, bool Y, bool K)? OverprintPlates, out SpotImageInk? Spots)
{ ... assigns from the properties ... }
```

The nine-parameter constructor has no optional parameters, so a call with nine arguments binds to
it and a call with six to nine positional arguments plus defaults still binds to the primary; the
compiler prefers the candidate with no defaulted parameters when both apply. A unit test constructs
and deconstructs through both shapes and asserts `ProofCmyk` is null on the compat path.

### 5.3 InternalsVisibleTo

In `PdfLibrary/PdfLibrary.csproj`, the existing grant `ItemGroup` gains the six assemblies from D5,
grouped under a rewritten comment with two rules: test grants (existing, each justified by a build
that fails without it) and production grants (new, justified by "Pellucid needs it; the library
must not publish it; removed by the extraction"). `Xmp/Xmp.csproj` needs no change: nothing hidden
lives there, and Pellucid does not touch Xmp internals.

### 5.4 XML documentation filter

Amended at planning (2026-09-06): the inline `RoslynCodeTaskFactory` task proposed here cannot reference
`System.Reflection.Metadata` (simple names fail MSB3755; reference-pack or runtime paths collide with the
factory's own reference set). The filter is instead a dependency-free console tool, `tools/DocFilter`,
whose pure `DocFilter` class is unit-tested from `PdfLibrary.Tests`, and a target `StripNonPublicDocs` in
`PdfLibrary.csproj` that runs before `GenerateNuspec` in Release and shells to the tool for every Release
`PdfLibrary.xml` (bin and obj copies) and `PdfLibrary.Xmp.xml` (bin copy, which is what
`CopyProjectReferencesToPackage` bundles). Doc-id parsing is unchanged from the description below: `T:`
entries match on type; `M:`/`P:`/`F:`/`E:` entries match on declaring type plus the member name before the
parameter list; nested types are resolved through the metadata parent chain and joined with `.`.

Release-only because Pellucid builds Debug locally by project reference and its IntelliSense for
the granted internals is worth keeping. The publish workflow builds Release, so the packed file is
the filtered one.

Verification is two-layered: a unit test in `PdfLibrary.Tests` feeds the filter a fixture XML plus
the test assembly's own metadata and asserts public entries survive and internal ones do not; and
the publish workflow, after `dotnet pack`, unzips each nupkg and fails on any `<member name=` that
contains `Conformance.Rules.`, `Fonts.Remediation.`, `Repair`, `Refusal`, or `Proposal`. The
second layer is the one that matters for a release; the first is what lets a change to the filter
be reviewed.

### 5.5 Symbols

No change. Both csproj keep `IncludeSymbols` true with the `snupkg` format, SourceLink stays, and
the workflow keeps pushing both symbol packages (D7).

### 5.6 Public API files

`Microsoft.CodeAnalysis.PublicApiAnalyzers` (PrivateAssets all) added to `PdfLibrary`, `Xmp` and
`PdfLibrary.Rendering.Wpf`. Files generated with the analyzer's own fixer after 5.1 and 5.2 land,
then reviewed against the Appendix C list: every Appendix C entry present, no Appendix B entry
present. RS0016 and RS0017 are errors; RS0026/RS0027 (overload rules) left at default. The
`Unshipped` file is empty at tag time; the plan's last task moves nothing, because the `Shipped`
file is written directly as the 2.6.0 surface.

### 5.7 Paperwork

**CHANGELOG.** The `[Unreleased]` section becomes `[2.6.0] - <tag date>`. It keeps the XMP
round-trip text already there and adds, under the Keep-a-Changelog headings, everything in Appendix
A that a consumer can observe. Required entries, in the order a reader needs them:

- *Fixed*: xref generation on save (issue 80), hex-string byte fidelity across a save (issue 57),
  real-number precision on save, recursive page-tree traversal, the atomic-write retry race (issue
  55), the CID-to-GID split that kept render identity (issue 42), the width-patch render fix (issue
  36), and the page-tree/transparency cycle guards.
- *Changed*: preflight detection now reaches full veraPDF verdict parity on the four profiles
  (986/986 on the PDF/A-2b corpus, 22/22 on 2u), listing the clauses that moved to full; the
  `XmpProperty` projection note already present; package size 3.0 → 5.4 MB and why; eager
  charstring work removed from font scans (perf).
- *Added*: every Appendix C item, grouped as in D3, with one sentence each.
- *Notice*: the existing forwarder-removal-at-3.0.0 paragraph, unchanged.

**PackageReleaseNotes** in both csproj: rewritten for 2.6.0 in the same shape as the 2.5.2 text
(one paragraph, what a consumer gets, "See CHANGELOG.md"). The Wpf note stays "tracks the core
release".

**README.** A "Font substitution and inventory" recipe under More recipes covering
`SystemFontLocator.Resolve`, `EnumerateFaces` and `FontInventory`; the Supported PDF features table
gains CID-to-Unicode extraction via bundled Adobe CMaps. Nothing about repairs.

**Releasing.md.** Adds the D10 API-surface step, the XML assertion, and the D11/D12 Pellucid
sequencing.

**Version.** `<Version>2.6.0</Version>` in `PdfLibrary.csproj` and `PdfLibrary.Rendering.Wpf.csproj`.
`PdfLibrary.Rendering.SkiaSharp` untouched, per Releasing.md.

### 5.8 Workflows

`publish-nuget.yml`: add, after pack, the XML-content assertion from 5.4. `ci.yml`: no change. The
analyzer runs inside the normal build on both.

### 5.9 Pellucid

Two commits, in this order, on Pellucid `main`:

1. Before the tag: `ci/dependencies.json` → the merged 2.6.0 release commit. Pellucid CI's source
   build is the proof that the six grants are sufficient (a missing grant is a CS0122 naming the
   assembly). Any Pellucid assembly the grep in 3.5 missed shows up here, not after publishing.
2. After the package is live: `LxmanPdfLibraryVersion` → 2.6.0 in `Directory.Build.props`. The
   canary job goes green; that is the release's last acceptance check.

No Pellucid source changes are expected. The grants make the hidden members reachable under the
same names.

## 6. Verification and definition of done

### 6.1 Engine, before the tag

| Check | Passes when |
|---|---|
| API surface diff (D10) | Removed entries = exactly the 34 XMP forwarders; every added entry appears in Appendix C |
| Public API files (D9) | Build is clean with RS0016/RS0017 as errors; `Shipped` contains no Appendix B name |
| `PdfLibrary.Tests`, CI filter | 0 failed (4,194 on master today; count may rise with the two new tests) |
| Full engine CI on the release branch | build-linux, build-windows, parity all green |
| Release pack dry-run, both packages | `PdfLibrary.xml` and `PdfLibrary.Xmp.xml` contain no `Conformance.Rules.`, `Fonts.Remediation.`, `Repair`, `Refusal`, `Proposal`; `PdfLibrary.Xmp.dll` present; both `.snupkg` produced |
| `ImageCommand` compat test | constructs and deconstructs through the nine-parameter shapes |
| Paperwork | CHANGELOG covers every line of Appendix A that is consumer-visible; README recipes compile as written; both `PackageReleaseNotes` say 2.6.0 |

### 6.2 Pellucid, before the tag

| Check | Passes when |
|---|---|
| `ci/dependencies.json` at the release commit | Pellucid CI: build-windows, build-linux, build-macos, app-tests green |
| Local full build by project reference | 0 errors; no CS0122 |
| Pellucid.Core.Tests, Pellucid.Remediation.Tests, App non-LocalOnly | green, run once for the batch per the standing rule |

### 6.3 After publishing

| Check | Passes when |
|---|---|
| nuget.org | 2.6.0 listed for both packages, symbols accepted for both |
| Pellucid canary with `LxmanPdfLibraryVersion` 2.6.0 | package-path job green |
| Memory | `pdflibrary-release-readiness-2026-09` updated with the tag SHA and date |

## 7. Sequence

On the engine, branch `release/2.6.0` from master.

1. 5.1 and 5.2 (visibility, `ImageCommand`), with the compat test. Build clean.
2. 5.3 grants and the csproj comment.
3. 5.4 XML filter with its unit test; 5.8 workflow edit.
4. 5.6 analyzer and the generated API files; `tools/api-surface/`; run the diff and fix anything it
   names.
5. 5.7 paperwork: CHANGELOG, README, release notes, version, Releasing.md.
6. Full engine test run once for the branch; push; engine CI green.
7. Merge to master with `--no-ff`. Record the merge SHA.
8. Pellucid: pin `ci/dependencies.json` to that SHA, push, Pellucid CI green.
9. **Stop.** Publishing the GitHub release is the irreversible step and needs an explicit go.
10. Create the GitHub release `v2.6.0` on the merge SHA with the CHANGELOG section as the body. The
    workflow publishes.
11. Pellucid: bump `LxmanPdfLibraryVersion`, push, canary green. Update memory.

Steps 1 through 5 are five tasks with per-task review; the whole-branch review before step 6 is
where cross-task seams get caught (a type hidden in step 1 that step 5 documents as public, a grant
missing for a test project step 2 did not know about).

## 8. Risks

- **Accessibility cascades.** Hiding a type that a public signature exposes fails the build with an
  error naming the site; the rule in 5.1 (make the owner internal) resolves each one. If a cascade
  reaches something in Appendix C, that is a design finding: stop and decide, do not widen.
- **A Pellucid assembly the grep missed.** Step 8's CI run finds it as CS0122; the fix is one more
  grant, not a public member.
- **The XML filter removes a public entry.** The unit test in 5.4 guards the mechanism; the
  workflow assertion guards the outcome. If both pass and a public doc is still missing, the
  doc-id parser has a gap; fix the parser, do not weaken the assertion.
- **The 2.5.0 consumer upgrades and something breaks.** The only two candidates were 3.2 and 3.3;
  one is safe by construction and the other is restored. The API diff gate is what stops a third
  from appearing before the tag.
- **Analyzer noise.** RS0016 fires on every public member until the files exist; the fixer
  generates them in one pass. Later additions cost one file edit each, which is the point.

## 9. What this release deliberately leaves alone

The extraction, 3.0.0 and any pre-announcement (D13). The `Conformance` namespace's public surface,
which stays exactly as 2.4.0 shipped it plus the reads in D3. The engine's parity job, ratchets and
corpus pin. The 2.4.0–2.5.2 packages on nuget.org.

---

## Appendix A — changelog coverage checklist

Every item below is either documented under a heading in 5.7 or explicitly marked "hidden API, no
consumer-visible change" in the plan. The last documented commit is `9bf2e1b` (2026-08-12).

Program merges, newest first:

- 2026-08-26 action-type remediation (6.5.1 prohibited-action removal)
- 2026-08-26 annotation-appearance remediation (6.3.3)
- 2026-08-25 graphics-state and optional-content (6.2.5, 6.9)
- 2026-08-24 annotation-type classifier guards
- 2026-08-24 annotation-type remediation
- 2026-08-23 stream-filters remediation (LZW re-encode)
- 2026-08-23 issue 80 xref generation
- 2026-08-22 image-dictionary remediation (6.2.8)
- 2026-08-21 font-dictionary and embedded-file remediation
- 2026-08-21 Matterhorn heading re-sum
- 2026-08-21 widen parity full clauses (65 clauses at 100%)
- 2026-08-21 parity 6.2.2 and font glyph (986/986)
- 2026-08-20 parity 6.1.13 t10 max CID
- 2026-08-20 parity 6.1.6 and 6.1.13 byte fidelity
- 2026-08-20 issue 57 hex-string format
- 2026-08-20 parity 6.6.4 prefixes
- 2026-08-20 parity 6.1.4 t2
- 2026-08-20 parity 6.6.2.1 t23 and 6.3.3 t3
- 2026-08-20 issue 55 atomic-write flake
- 2026-08-20 lazy charstrings (perf)
- 2026-08-19 PDF/A-2u full parity (22/22)
- 2026-08-19 inline-image rule
- 2026-08-19 issue 42 CID-to-GID split
- 2026-08-19 parity report refresh
- 2026-08-19 issue 51 measurement harness (docs)
- 2026-08-19 issues 44 and 48
- 2026-08-18 per-holder merge (issues 38, 40)
- 2026-08-17 F-4b Type0 whole-face replacement (issues 34, 35 groundwork)
- 2026-08-17 F-4a width remediation and issue 36 render fix
- 2026-08-16 encoding follow-ups 27 and 28
- 2026-08-15 font width and encoding 24 to 26
- 2026-08-14 font subset coverage
- 2026-08-14 annotation flags
- 2026-08-13 XMP remediation
- 2026-08-12 XMP round-trip (already documented)

Direct-to-master commits after the last merge:

- 2026-09-02 Expose output-safety document facts (`PreviewDocumentProof`, public)
- 2026-08-31 Close veraPDF detector parity gaps
- 2026-08-30 Preserve PDF real-number precision
- 2026-08-30 Fix recursive page-tree traversal
- 2026-08-30 Audit document load failure boundaries
- 2026-08-30 Audit separation consistency save behavior
- 2026-08-30 Add provable DeviceN colorants repair
- 2026-08-30 Add document requirements repair
- 2026-08-29 Add alternate presentations repair
- 2026-08-29 Add catalog and page additional-actions repair
- 2026-08-29 Audit JPEG 2000 save behavior
- 2026-08-29 Audit prohibited XObject save behavior
- 2026-08-29 Audit ICC CMYK overprint save behavior
- 2026-08-29 Audit rendering-intent save behavior
- 2026-08-28 Audit content-stream operator save behavior
- 2026-08-28 Land audited PDF/A remediations
- 2026-08-28 Add binary-safe inline-image repair
- 2026-08-28 Add safe UTF-8 name normalization
- 2026-08-28 Harden form-configuration corpus classification
- 2026-08-28 Add safe form-configuration repair classifier
- 2026-08-27 Add form-field action repair
- 2026-08-27 Guard transparency resource cycles
- 2026-08-27 Add explicit-resource repair

## Appendix B — the hidden set

111 types, 1,198 members. By family:

- `PdfLibrary.Editing`: for each of AdditionalActions, AlternatePresentations, AnnotationAppearance,
  AnnotationType, DocumentRequirements, ExplicitResource, FileSpecName, FormConfiguration,
  FormFieldAction, GraphicsState, ImageDictionary, InlineImage, NChannelColorant(s), NameUtf8,
  OptionalContent, Permissions, ProhibitedAction, StreamExternalFile, StreamFilter: the
  `*Repair`, `*RepairCandidate`, `*RepairPreview`, `*RepairReport`, `*Refusal` / `*RepairRefusal`
  records, plus `AdditionalActionsOwnerKind`, `AlternatePresentationsOwnerKind`,
  `ExplicitResourceOwnerKind`, `FormFieldActionOwnerKind`, `AnnotationAppearanceRepairKind`,
  `ImageDictionaryRepairKind`, `ProhibitedActionKind`, `ProhibitedActionSite`.
- `PdfLibrary.Fonts.Remediation`: `FontRemediationPlanner`, `FontRemediationProposal`,
  `FontProposal`, `EmbedProposal`, `DeclineProposal`, `PatchWidthsProposal`,
  `RegenerateDeclarationProposal`, `ReplaceProgramProposal`, `ReplaceTarget`, `ToUnicodeProposal`,
  `CandidateAssessment`.
- `PdfLibrary.Conformance`: `XmpExtensionSchemaStructureRefusal`,
  `XmpExtensionSchemaStructureRepairReport`.

Members on types that stay public:

- `XmpConformance.PreviewExtensionSchemaStructureRepairs(XmpPacket)`,
  `XmpConformance.RepairExtensionSchemaStructure(XmpPacket)`.
- `PdfDocumentEditor`: the 23 `Preview*Repair*` methods and the 23 `Repair*` methods (one pair per
  family above, plus `PreviewPermissionsRepair`/`RepairPermissions`), and `EmbedProgram`,
  `SetCidSet`, `SetCharSet`, `SetToUnicode`, `ReplaceProgramBytes`, `ReplaceCompositeProgram`,
  `HasFont`, `CanSetCidToGidMapIdentity`, `SetCidToGidMapIdentity`, `CanRemoveSymbolicEncoding`,
  `RemoveSymbolicEncoding`.

Not hidden, on purpose: `PreviewDocumentProof`, `DocumentProofPreview`, `SetFileId`,
`ConsolidateOutputIntents`, `ReplaceOutputIntentProfile`.

## Appendix C — the published additions

24 types, 213 members. Every one of these needs a changelog line and, where it is an entry point, a
README mention.

Types: `Conformance.ConformanceClaim`, `Conformance.OutputIntentProfileValidator`,
`Conformance.XmpConformance` (two read methods), `Conformance.XmpModernEquivalent`,
`Conformance.XmpPropertyVerdict`, `Editing.DocumentProofPreview`, `Editing.PdfTrapped`,
`Fonts.AdobeCidToUnicode`, `Fonts.BundledStandard14Provider`, `Fonts.CidCMap`,
`Fonts.ClassifiedProgram`, `Fonts.FontDescriptorMetrics`, `Fonts.FontDescriptorValues`,
`Fonts.FontId`, `Fonts.FontInventory`, `Fonts.FontInventoryEntry`, `Fonts.FontKind`,
`Fonts.FontMatch`, `Fonts.FontProgramClassifier`, `Fonts.FontProgramFormat`, `Fonts.FontRequest`,
`Fonts.SystemFontFace`, `Fonts.ToUnicodeCMapWriter`, `Fonts.ToUnicodeCodespace`.

Members on pre-existing types: `Preflighter.Check(PdfDocument, ConformanceProfile, byte[])`;
`PdfDocumentEditor.PreviewDocumentProof()`, `.SetFileId(byte[])`, `.ConsolidateOutputIntents(int)`,
`.ReplaceOutputIntentProfile(int, byte[], string, string)`, `.Document`;
`PdfPageCollection.SetAnnotationFlags(int, int, int)`; `PdfFormFields.HasXfa`;
`PdfAnnotationInfo.Flags`; `PdfMetadata.Language`, `.Trapped`; `ISystemFontProvider.Resolve`,
`.EnumerateFaces` (defaulted); `SystemFontLocator.Resolve`, `.EnumerateFaces`;
`RecordingRenderTarget.Record(PdfPage, double, ISystemFontProvider?)`; `ImageCommand.ProofCmyk` and
its widened constructor and `Deconstruct`; `PdfGraphicsState.ResolvedFillProofCmyk`,
`.ResolvedStrokeProofCmyk`; `DeviceCmykConverter.Naive`.

## Appendix D — how the surface was measured

One PowerShell process per assembly (two builds of `PdfLibrary.dll` cannot share a load context):
`Assembly.LoadFrom` the target so sibling assemblies resolve from its directory, `GetExportedTypes`,
then for each public type the `Public | Instance | Static | DeclaredOnly` members, written as one
line per entry: `T` type, `C` constructor, `M` method, `P` property, `F` field, `E` enum member, `V`
event, with parameter type names for constructors and methods. Sort unique, `Compare-Object` the two
files. The scripts land in `tools/api-surface/` under D10; until then the description above is
enough to rebuild them.
