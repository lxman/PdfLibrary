# P-4 Font-Presence-Without-Decode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `PdfRenderer`'s per-ShowText embedded-font presence probe stops decoding the font program (currently ~30% of all busy CPU on the ISO 32000-2 cold open), verified by unit tests and a repeat of the exact profiling run.

**Architecture:** One new presence property on `PdfFontDescriptor` (`HasEmbeddedFontProgram`) built on a shared raw-stream helper (the no-decode pattern `GetFontFile2Stream`/`GetFontFile3Stream` already use); `PdfRenderer.HasEmbeddedFontData` becomes a delegation. Strict narrowing of work — no decoded-bytes accessor changes, no memoization (spec non-goal).

**Tech Stack:** C# (engine repo `C:\Users\jorda\RiderProjects\PDF`), xunit; `dotnet-trace` + the session's speedscope analyzer for the acceptance re-profile.

**Spec:** `Docs/superpowers/specs/2026-08-02-p4-font-presence-no-decode-design.md`

## Global Constraints

- Engine repo for the code; the Pellucid repo (`C:\Users\jorda\RiderProjects\Pellucid`) gets only the tracker update in Task 2.
- Zero behavior change for well-formed fonts; the ONE pinned semantic delta is present-but-corrupt streams (presence-only reports true, never decodes — tested).
- Full engine suite green (2790-shape, incl. LocalOnly conformance corpora); conformance agreement counts unchanged.
- Commit after each task; end commit messages with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`. Never push.

---

### Task 1: `HasEmbeddedFontProgram` + renderer delegation

**Files:**
- Modify: `PdfLibrary/Fonts/PdfFontDescriptor.cs` (raw helper + property; dedup `GetFontFile2Stream`:275-282 and `GetFontFile3Stream`:288-295 onto the helper)
- Modify: `PdfLibrary/Rendering/PdfRenderer.cs:2061-2077` (`HasEmbeddedFontData`)
- Test: `PdfLibrary.Tests/Fonts/PdfFontDescriptorPresenceTests.cs` (create)

**Interfaces:**
- Produces: `public bool HasEmbeddedFontProgram` on `PdfFontDescriptor` (internal class — the test project already exercises internal engine types); `private PdfStream? GetFontFileStreamRaw(string key)`.

- [ ] **Step 1: Write the failing tests**

`PdfLibrary.Tests/Fonts/PdfFontDescriptorPresenceTests.cs`:

```csharp
using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

/// <summary>P-4: the embedded-font presence probe must answer from the raw stream object — never
/// by decoding the font program (the decode cost ran once per ShowText operator and was ~30% of
/// all busy CPU on the ISO 32000-2 cold open). The corrupt-stream case doubles as the proof no
/// decode happens: decoding that fixture throws, so a true answer without a throw means the bytes
/// were never touched.</summary>
public class PdfFontDescriptorPresenceTests
{
    private static PdfFontDescriptor Descriptor(params (string key, PdfObject value)[] entries)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("FontDescriptor"),
            [new PdfName("FontName")] = new PdfName("Test"),
        };
        foreach ((string key, PdfObject value) in entries)
            dict[new PdfName(key)] = value;
        return new PdfFontDescriptor(dict);
    }

    private static PdfStream Stream(byte[] data) => new(new PdfDictionary(), data);

    [Theory]
    [InlineData("FontFile")]
    [InlineData("FontFile2")]
    [InlineData("FontFile3")]
    public void PresentStream_UnderAnyKey_IsTrue(string key)
    {
        Assert.True(Descriptor((key, Stream("font-bytes"u8.ToArray()))).HasEmbeddedFontProgram);
    }

    [Fact]
    public void NoFontFileKeys_IsFalse()
    {
        Assert.False(Descriptor().HasEmbeddedFontProgram);
    }

    [Fact]
    public void NonStreamValue_IsFalse()
    {
        Assert.False(Descriptor(("FontFile2", new PdfName("NotAStream"))).HasEmbeddedFontProgram);
    }

    [Fact]
    public void CorruptStream_IsTrue_AndProvesNoDecodeHappens()
    {
        // Garbage under a /FlateDecode filter: decoding THROWS (asserted below, so the fixture is
        // genuinely corrupt, not accidentally valid) — while the presence probe answers true
        // without touching the bytes. This is the spec's pinned semantic delta: presence-only also
        // removes a decode-failure crash path from the per-ShowText probe.
        var dict = new PdfDictionary { [PdfName.Filter] = new PdfName("FlateDecode") };
        var corrupt = new PdfStream(dict, Encoding.ASCII.GetBytes("this is not zlib data"));
        PdfFontDescriptor d = Descriptor(("FontFile2", corrupt));

        Assert.True(d.HasEmbeddedFontProgram);
        Assert.ThrowsAny<Exception>(() => d.GetFontFile2());
    }
}
```

(If `PdfName.Filter` is not a public static — the engine uses it in `PdfStream.GetDecodedData` —
substitute `new PdfName("Filter")`. If the corrupt fixture does NOT throw on decode because the
Flate filter degrades silently, replace the `ThrowsAny` assertion with
`Assert.NotNull(...)`-free proof by another route: STOP and report the actual filter behavior
rather than shipping a vacuous pin.)

- [ ] **Step 2: Run — expect compile failure** (`HasEmbeddedFontProgram` does not exist).

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~PdfFontDescriptorPresenceTests"`

- [ ] **Step 3: Implement**

(a) In `PdfFontDescriptor.cs`, add (near the existing raw accessors):

```csharp
    /// <summary>True when the descriptor carries an embedded font program stream under any of
    /// /FontFile, /FontFile2, /FontFile3 (ISO 32000-1 §9.8.2, Table 126). PRESENCE ONLY — resolves
    /// the reference but never decodes the stream (P-4: the decoded-bytes accessors cost a full
    /// decrypt+inflate per call, which a per-ShowText presence probe must not pay).</summary>
    public bool HasEmbeddedFontProgram =>
        GetFontFileStreamRaw("FontFile") is not null
        || GetFontFileStreamRaw("FontFile2") is not null
        || GetFontFileStreamRaw("FontFile3") is not null;

    /// <summary>Raw stream object under <paramref name="key"/> (reference resolved, NOT decoded) —
    /// the shared core of the presence probe and the subsetter's raw accessors.</summary>
    private PdfStream? GetFontFileStreamRaw(string key)
    {
        if (!_dictionary.TryGetValue(new PdfName(key), out PdfObject? obj)) return null;
        if (obj is PdfIndirectReference reference && _document is not null)
            obj = _document.ResolveReference(reference);
        return obj as PdfStream;
    }
```

(b) Dedup the existing raw accessors onto it (bodies become one-liners; doc comments stay):

```csharp
    internal PdfStream? GetFontFile2Stream() => GetFontFileStreamRaw("FontFile2");
    internal PdfStream? GetFontFile3Stream() => GetFontFileStreamRaw("FontFile3");
```

(c) In `PdfRenderer.cs:2061-2077`, replace `HasEmbeddedFontData`'s body (keep the method and its
doc comment, updating the comment's second line):

```csharp
    /// <summary>
    /// Checks if a font has embedded font data (FontFile/FontFile2/FontFile3).
    /// Presence only — never decodes the font program (P-4: this runs once per ShowText).
    /// </summary>
    private static bool HasEmbeddedFontData(PdfFont? font) =>
        font?.GetDescriptor()?.HasEmbeddedFontProgram ?? false;
```

- [ ] **Step 4: Run the new tests + the Fonts area — all PASS**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~PdfFontDescriptorPresenceTests|FullyQualifiedName~PdfLibrary.Tests.Fonts"`

- [ ] **Step 5: Full engine suite (incl. LocalOnly conformance corpora; 10-min timeout)**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj`
Expected: 2790-shape green, conformance agreement counts unchanged. Any conformance movement → BLOCKED.

- [ ] **Step 6: Commit**

```powershell
git add PdfLibrary/Fonts/PdfFontDescriptor.cs PdfLibrary/Rendering/PdfRenderer.cs PdfLibrary.Tests/Fonts/PdfFontDescriptorPresenceTests.cs
git commit -m "perf(fonts): presence probe no longer decodes the embedded font program (P-4)"
```

---

### Task 2: Acceptance re-profile + tracker

**Files:**
- Modify (Pellucid repo): `docs/ISSUE-TRACKER.md` (P-4 entry → 🟢 with measured numbers; P-6 re-baseline note)

**Interfaces:**
- Consumes: Task 1; the profiling artifacts from the 2026-08-02 session (analyzer script at the session scratchpad `trace\analyze2.py`; baseline numbers in the tracker's P-4/P-6 entries).

- [ ] **Step 1: Rebuild the app against the fixed engine.** The Pellucid app consumes the engine
via the git-ignored local-feed pin (`pack-local.ps1` loop — see the repo's local-feed docs and
memory notes; Skia/stale-cache gotchas documented there). Run the engine pack + Pellucid re-pin +
`dotnet build Pellucid.App -c Release` in the Pellucid repo. If the pack-local flow reports
anything unexpected, STOP and report rather than improvising the pin.

- [ ] **Step 2: Repeat the EXACT trace run** (same command as the baseline):

```powershell
dotnet-trace collect --duration 00:00:45 -o <scratchpad>\trace\iso-open-after-p4.nettrace -- `
  "C:\Users\jorda\RiderProjects\Pellucid\Pellucid.App\bin\Release\net10.0-windows\Pellucid.App.exe" `
  "C:\Users\jorda\RiderProjects\PDF\PDFs\PDF Standards\ISO_32000-2_sponsored-ec2.pdf"
Stop-Process -Name "Pellucid.App" -Force
dotnet-trace convert <trace> --format Speedscope -o <scratchpad>\trace\iso-open-after-p4
python <scratchpad>\trace\analyze2.py <scratchpad>\trace\iso-open-after-p4.speedscope.json
```

- [ ] **Step 3: Judge against the baseline** (busy 32.8 thread-s; `Inflater.Inflate` 30.3% excl;
tail ~0.8 s/s through 45 s):
- The `OnShowText → HasEmbeddedFontData → GetFontFile* → Inflate` chain must be GONE from the
  inclusive list (a residual `GetFontFile2` from `EmbeddedFontExtractor`'s one-time load is fine).
- Record: new busy total, new top exclusives, and the tail shape (does the app go idle before the
  45 s window ends? at what second?). Expected ≥ ~25% busy-total reduction; if the reduction is
  materially smaller, STOP and report the new profile rather than declaring victory.

- [ ] **Step 4: Tracker (Pellucid repo)** — P-4 → 🟢 Fixed+Verified: root cause, fix shape
(presence property, spec path), unit-test pin, full-suite green, and the before/after trace
numbers. Under P-6, add the re-baseline: the new tail shape and whether P-6 remains open (still
long?) or is absorbed. House style per the neighboring entries.

- [ ] **Step 5: Commits**

Engine repo has no changes in this task. Pellucid:

```powershell
git add docs/ISSUE-TRACKER.md
git commit -m "docs: P-4 closed - presence probe decode eliminated; P-6 re-baselined"
```

(Do NOT push either repo.)

---

## Self-Review

- **Spec coverage:** presence property + raw helper + dedup + renderer delegation (T1 = spec Design), all four test classes of behavior incl. the corrupt-stream pin with a non-vacuity escape hatch (T1 Step 1 = spec Testing 1), full suite + conformance floors (T1 Step 5 = spec Testing 2), re-profile with explicit judgment criteria + tracker (T2 = spec Testing 3 / Goal 3). Non-goals respected: no memoization task, no decoded-accessor changes.
- **Placeholder scan:** clean — the one conditional (corrupt-fixture filter behavior) has a named STOP path, not a TBD; T2's `<scratchpad>` refers to the session scratchpad path used by the baseline artifacts, known to the controller who dispatches it.
- **Type consistency:** `HasEmbeddedFontProgram`/`GetFontFileStreamRaw(string)` consistent between T1 code and tests; `PdfFontDescriptor(dict)` matches the primary ctor; `PdfStream(new PdfDictionary(), bytes)` matches the established test shape; renderer delegation keeps the private static signature its caller at `PdfRenderer.cs:909` expects.
