# veraPDF-parity capture tooling

This directory regenerates the **veraPDF verdict snapshot** — the committed "answer key"
(`PdfLibrary.Tests/Conformance/parity/verapdf-verdicts.json`) that the parity gate diffs the
conformance preflighter against. See `Docs/plans/2026-07-10-verapdf-parity-harness.md` for the
full design.

## The one rule: capture is Mac-only; everyone else consumes

veraPDF runs **only on the designated capture machine — the Mac.** It is the sole host with the
veraPDF + Java toolchain. Everyone else consumes the committed snapshot:

| Machine | Needs veraPDF / Java? | What it does |
|---|---|---|
| **Mac** (capture host) | **Yes** | Runs `capture.sh`, commits `verapdf-verdicts.json`. |
| Windows / Linux dev boxes | No | Regenerate the compliance report and run the parity gate with **.NET + a corpus checkout only**. |
| CI | No | Same as a dev box — reads the committed snapshot; no JVM in the pipeline. |

The snapshot is deterministic (veraPDF's and the preflighter's verdicts are byte/structure logic,
not OS-dependent), so **one snapshot serves all platforms**. You only re-run capture when the
**veraPDF version or the corpus changes** — otherwise the committed answer key stands and no machine
needs this toolchain.

> Mental model: veraPDF is the printing press for the answer key — run it once on the Mac. Every
> other machine and CI just needs a photocopy of the key (in git) plus the exam papers (the corpus).

## Capture-host setup (Mac)

1. **veraPDF CLI** — `brew install verapdf` (pins the **1.30.2** bottle from homebrew-core):
   ```
   brew install verapdf
   verapdf --version      # veraPDF 1.30.x
   ```
2. **A Java 11–17 runtime.** veraPDF's JAXB validation-profile loader mutates final fields
   reflectively, which **warns on JDK 21+ and errors on the very newest JDKs** (the IzPack
   `.zip` installer also silently unpacks nothing on JDK 26 — do not use it). `capture.sh`
   auto-uses a pinned Temurin **17** JRE if you drop one at `./jdk17`:
   ```
   curl -sL -o /tmp/jre17.tgz \
     "https://api.adoptium.net/v3/binary/latest/17/ga/mac/aarch64/jre/hotspot/normal/eclipse"
   mkdir -p jdk17 && tar -xzf /tmp/jre17.tgz -C jdk17 --strip-components=1
   ```
   (`jdk17/` is gitignored.) Alternatively `brew install openjdk@17` and export `JAVA_HOME`.
3. **The corpus** — a sibling `../veraPDF-corpus` checkout, or set `VERAPDF_CORPUS`.
4. **.NET SDK 10** — to run the MRR→JSON parser (already required by the repo).

## Regenerate the snapshot

```
./capture.sh
```

This validates each profile folder (`PDF_A-2b` → `2b`, `PDF_A-2u` → `2u`, `PDF_A-3b` → `3b`,
`PDF_UA-1` → `ua1`), writes raw MRR reports to `out/`, then parses them into the snapshot. Review the
diff before committing — it shows exactly which reference verdicts moved:

```
git diff --stat -- PdfLibrary.Tests/Conformance/parity/
```

## What is / isn't tracked

- **Committed:** `capture.sh`, `MrrToVerdicts/` (parser source), this README, and the generated
  `verapdf-verdicts.json` (under `PdfLibrary.Tests/Conformance/parity/`).
- **Gitignored (capture-host-local):** `jdk17/`, `verapdf/`, `out/`, parser `bin/`+`obj/`.

## Scope

The snapshot covers **PDF/A-2b, PDF/A-2u, PDF/A-3b, PDF/UA-1** — the profiles veraPDF validates.
**PDF/X-4 is out of scope: veraPDF does not validate PDF/X**, so X-4 keeps its GWG-GOS pass-oracle
and has no parity number. UA-1 parity is parity on veraPDF's machine-checkable subset (the ~87/136
Matterhorn conditions a machine can decide); the rest are human-judgment by ISO 14289 design.

## Pinned versions

| Component | Version | Notes |
|---|---|---|
| veraPDF | 1.30.2 (bottle) / apps 1.30.0 | recorded in the snapshot header |
| Java (capture) | Temurin 17 | 11–17 fine; **avoid 21+** for capture |
| corpus | recorded per capture | short git hash in the snapshot header |
