# veraPDF parity report

_PdfLibrary preflighter vs veraPDF (core 1.30.2, validation-model 1.30.2, apps 1.30.0); corpus @ 49de56c. Generated — regenerate with the `Category=Parity` test `ParityReportTests.Generate_parity_report` (set `PARITY_REPORT`), and re-run `tools/verapdf-parity/capture.sh` first if veraPDF or the corpus moved._

Across all **1316** files PdfLibrary produced **0 false positives** — it never rejects a file veraPDF accepts. PdfLibrary is a strict subset of veraPDF, so every disagreement below is a coverage gap (veraPDF flags a clause PdfLibrary does not yet implement), **not a PdfLibrary error**.

## Verdict agreement

| Profile | Files | Both pass | Both fail | PdfLibrary misses (gap) | PdfLibrary FP | Agreement |
|---|--:|--:|--:|--:|--:|--:|
| PDF/A-2b | 986 | 377 | 586 | 23 | 0 | 963/986 (98%) |
| PDF/A-2u | 22 | 12 | 9 | 1 | 0 | 21/22 (95%) |
| PDF/A-3b | 12 | 7 | 5 | 0 | 0 | 12/12 (100%) |
| PDF/UA-1 | 296 | 141 | 155 | 0 | 0 | 296/296 (100%) |

## Clause coverage

Of the files where veraPDF flags a clause, how many does PdfLibrary also flag on that clause.

### PDF/A-2b — 26/40 clauses at full parity

| Clause | veraPDF flags | PdfLibrary matches | Coverage | |
|---|--:|--:|--:|---|
| 6.6.2.3.1 | 283 | 283 | 100% | ✅ full |
| 6.2.4.3 | 87 | 87 | 100% | ✅ full |
| 6.2.10 | 35 | 35 | 100% | ✅ full |
| 6.3.3 | 26 | 25 | 96% | ◐ partial |
| 6.3.2 | 25 | 25 | 100% | ✅ full |
| 6.6.2.3.3 | 18 | 18 | 100% | ✅ full |
| 6.1.13 | 15 | 10 | 67% | ◐ partial |
| 6.5.1 | 15 | 15 | 100% | ✅ full |
| 6.3.1 | 14 | 14 | 100% | ✅ full |
| 6.2.11.5 | 13 | 7 | 54% | ◐ partial |
| 6.2.11.4.1 | 11 | 6 | 55% | ◐ partial |
| 6.1.2 | 9 | 8 | 89% | ◐ partial |
| 6.2.11.8 | 8 | 3 | 38% | ◐ partial |
| 6.1.7.1 | 7 | 7 | 100% | ✅ full |
| 6.1.9 | 7 | 7 | 100% | ✅ full |
| 6.2.4.4 | 7 | 7 | 100% | ✅ full |
| 6.4.1 | 7 | 7 | 100% | ✅ full |
| 6.2.2 | 6 | 3 | 50% | ◐ partial |
| 6.2.5 | 6 | 6 | 100% | ✅ full |
| 6.6.4 | 6 | 5 | 83% | ◐ partial |
| 6.2.11.3.3 | 5 | 5 | 100% | ✅ full |
| 6.2.11.6 | 5 | 5 | 100% | ✅ full |
| 6.2.3 | 5 | 5 | 100% | ✅ full |
| 6.2.8.3 | 5 | 5 | 100% | ✅ full |
| 6.2.9 | 5 | 5 | 100% | ✅ full |
| 6.1.12 | 4 | 4 | 100% | ✅ full |
| 6.1.3 | 4 | 4 | 100% | ✅ full |
| 6.2.11.3.1 | 4 | 3 | 75% | ◐ partial |
| 6.2.6 | 4 | 4 | 100% | ✅ full |
| 6.2.8 | 4 | 3 | 75% | ◐ partial |
| 6.2.11.3.2 | 3 | 3 | 100% | ✅ full |
| 6.2.4.2 | 3 | 3 | 100% | ✅ full |
| 6.6.2.1 | 3 | 1 | 33% | ◐ partial |
| 6.1.10 | 2 | 0 | 0% | — none |
| 6.1.4 | 2 | 0 | 0% | — none |
| 6.1.6 | 2 | 0 | 0% | — none |
| 6.1.8 | 2 | 2 | 100% | ✅ full |
| 6.2.11.4.2 | 2 | 2 | 100% | ✅ full |
| 6.4.2 | 2 | 2 | 100% | ✅ full |
| 6.5.2 | 2 | 2 | 100% | ✅ full |

### PDF/A-2u — 2/3 clauses at full parity

| Clause | veraPDF flags | PdfLibrary matches | Coverage | |
|---|--:|--:|--:|---|
| 6.2.11.7.2 | 8 | 7 | 88% | ◐ partial |
| 6.2.11.3.1 | 1 | 1 | 100% | ✅ full |
| 6.6.4 | 1 | 1 | 100% | ✅ full |

### PDF/A-3b — 1/1 clauses at full parity

| Clause | veraPDF flags | PdfLibrary matches | Coverage | |
|---|--:|--:|--:|---|
| 6.8 | 5 | 5 | 100% | ✅ full |

### PDF/UA-1 — 26/30 clauses at full parity

| Clause | veraPDF flags | PdfLibrary matches | Coverage | |
|---|--:|--:|--:|---|
| 7.2 | 60 | 31 | 52% | ◐ partial |
| 7.1 | 16 | 16 | 100% | ✅ full |
| 7.18.1 | 10 | 10 | 100% | ✅ full |
| 7.11 | 6 | 6 | 100% | ✅ full |
| 5 | 5 | 5 | 100% | ✅ full |
| 7.21.6 | 5 | 5 | 100% | ✅ full |
| 7.4.4 | 5 | 5 | 100% | ✅ full |
| 7.21.3.1 | 4 | 3 | 75% | ◐ partial |
| 7.21.3.3 | 4 | 4 | 100% | ✅ full |
| 7.21.7 | 4 | 4 | 100% | ✅ full |
| 7.10 | 3 | 3 | 100% | ✅ full |
| 7.18.5 | 3 | 3 | 100% | ✅ full |
| 7.18.6.2 | 3 | 3 | 100% | ✅ full |
| 7.21.3.2 | 3 | 3 | 100% | ✅ full |
| 7.21.4.2 | 3 | 3 | 100% | ✅ full |
| 7.5 | 3 | 3 | 100% | ✅ full |
| 7.9 | 3 | 3 | 100% | ✅ full |
| 7.18.3 | 2 | 2 | 100% | ✅ full |
| 7.20 | 2 | 2 | 100% | ✅ full |
| 7.3 | 2 | 2 | 100% | ✅ full |
| 7.4.2 | 2 | 2 | 100% | ✅ full |
| 7.7 | 2 | 0 | 0% | — none |
| 7.15 | 1 | 1 | 100% | ✅ full |
| 7.16 | 1 | 1 | 100% | ✅ full |
| 7.18.2 | 1 | 1 | 100% | ✅ full |
| 7.18.4 | 1 | 1 | 100% | ✅ full |
| 7.18.8 | 1 | 1 | 100% | ✅ full |
| 7.21.4.1 | 1 | 0 | 0% | — none |
| 7.21.5 | 1 | 1 | 100% | ✅ full |
| 7.21.8 | 1 | 1 | 100% | ✅ full |

## Biggest parity gaps (highest-leverage work)

Ranked by number of files PdfLibrary misses on a clause it does not fully cover.

1. **PDF/UA-1 clause 7.2** — 29 of 60 files missed (PdfLibrary matches 31).
2. **PDF/A-2b clause 6.2.11.5** — 6 of 13 files missed (PdfLibrary matches 7).
3. **PDF/A-2b clause 6.1.13** — 5 of 15 files missed (PdfLibrary matches 10).
4. **PDF/A-2b clause 6.2.11.4.1** — 5 of 11 files missed (PdfLibrary matches 6).
5. **PDF/A-2b clause 6.2.11.8** — 5 of 8 files missed (PdfLibrary matches 3).
6. **PDF/A-2b clause 6.2.2** — 3 of 6 files missed (PdfLibrary matches 3).
7. **PDF/A-2b clause 6.6.2.1** — 2 of 3 files missed (PdfLibrary matches 1).
8. **PDF/A-2b clause 6.1.10** — 2 of 2 files missed (PdfLibrary matches 0).
9. **PDF/A-2b clause 6.1.4** — 2 of 2 files missed (PdfLibrary matches 0).
10. **PDF/A-2b clause 6.1.6** — 2 of 2 files missed (PdfLibrary matches 0).

