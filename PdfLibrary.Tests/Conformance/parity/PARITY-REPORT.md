# veraPDF parity report

_Focal preflighter vs veraPDF (core 1.30.2, validation-model 1.30.2, apps 1.30.0); corpus @ 49de56c. Generated — regenerate with the `Category=Parity` test `ParityReportTests.Generate_parity_report` (set `PARITY_REPORT`), and re-run `tools/verapdf-parity/capture.sh` first if veraPDF or the corpus moved._

Across all **1316** files Focal produced **0 false positives** — it never rejects a file veraPDF accepts. Focal is a strict subset of veraPDF, so every disagreement below is a coverage gap (veraPDF flags a clause Focal does not yet implement), **not a Focal error**.

## Verdict agreement

| Profile | Files | Both pass | Both fail | Focal misses (gap) | Focal FP | Agreement |
|---|--:|--:|--:|--:|--:|--:|
| PDF/A-2b | 986 | 377 | 522 | 87 | 0 | 899/986 (91%) |
| PDF/A-2u | 22 | 12 | 7 | 3 | 0 | 19/22 (86%) |
| PDF/A-3b | 12 | 7 | 5 | 0 | 0 | 12/12 (100%) |
| PDF/UA-1 | 296 | 141 | 107 | 48 | 0 | 248/296 (84%) |

## Clause coverage

Of the files where veraPDF flags a clause, how many does Focal also flag on that clause.

### PDF/A-2b — 14/40 clauses at full parity

| Clause | veraPDF flags | Focal matches | Coverage | |
|---|--:|--:|--:|---|
| 6.6.2.3.1 | 283 | 283 | 100% | ✅ full |
| 6.2.4.3 | 87 | 87 | 100% | ✅ full |
| 6.2.10 | 35 | 35 | 100% | ✅ full |
| 6.3.3 | 26 | 25 | 96% | ◐ partial |
| 6.3.2 | 25 | 25 | 100% | ✅ full |
| 6.6.2.3.3 | 18 | 18 | 100% | ✅ full |
| 6.1.13 | 15 | 9 | 60% | ◐ partial |
| 6.5.1 | 15 | 15 | 100% | ✅ full |
| 6.3.1 | 14 | 14 | 100% | ✅ full |
| 6.2.11.5 | 13 | 3 | 23% | ◐ partial |
| 6.2.11.4.1 | 11 | 6 | 55% | ◐ partial |
| 6.1.2 | 9 | 8 | 89% | ◐ partial |
| 6.2.11.8 | 8 | 3 | 38% | ◐ partial |
| 6.1.7.1 | 7 | 3 | 43% | ◐ partial |
| 6.1.9 | 7 | 0 | 0% | — none |
| 6.2.4.4 | 7 | 0 | 0% | — none |
| 6.4.1 | 7 | 7 | 100% | ✅ full |
| 6.2.2 | 6 | 0 | 0% | — none |
| 6.2.5 | 6 | 0 | 0% | — none |
| 6.6.4 | 6 | 5 | 83% | ◐ partial |
| 6.2.11.3.3 | 5 | 1 | 20% | ◐ partial |
| 6.2.11.6 | 5 | 5 | 100% | ✅ full |
| 6.2.3 | 5 | 5 | 100% | ✅ full |
| 6.2.8.3 | 5 | 0 | 0% | — none |
| 6.2.9 | 5 | 0 | 0% | — none |
| 6.1.12 | 4 | 0 | 0% | — none |
| 6.1.3 | 4 | 4 | 100% | ✅ full |
| 6.2.11.3.1 | 4 | 3 | 75% | ◐ partial |
| 6.2.6 | 4 | 0 | 0% | — none |
| 6.2.8 | 4 | 0 | 0% | — none |
| 6.2.11.3.2 | 3 | 3 | 100% | ✅ full |
| 6.2.4.2 | 3 | 0 | 0% | — none |
| 6.6.2.1 | 3 | 1 | 33% | ◐ partial |
| 6.1.10 | 2 | 0 | 0% | — none |
| 6.1.4 | 2 | 0 | 0% | — none |
| 6.1.6 | 2 | 0 | 0% | — none |
| 6.1.8 | 2 | 0 | 0% | — none |
| 6.2.11.4.2 | 2 | 0 | 0% | — none |
| 6.4.2 | 2 | 2 | 100% | ✅ full |
| 6.5.2 | 2 | 2 | 100% | ✅ full |

### PDF/A-2u — 2/3 clauses at full parity

| Clause | veraPDF flags | Focal matches | Coverage | |
|---|--:|--:|--:|---|
| 6.2.11.7.2 | 8 | 5 | 62% | ◐ partial |
| 6.2.11.3.1 | 1 | 1 | 100% | ✅ full |
| 6.6.4 | 1 | 1 | 100% | ✅ full |

### PDF/A-3b — 1/1 clauses at full parity

| Clause | veraPDF flags | Focal matches | Coverage | |
|---|--:|--:|--:|---|
| 6.8 | 5 | 5 | 100% | ✅ full |

### PDF/UA-1 — 10/30 clauses at full parity

| Clause | veraPDF flags | Focal matches | Coverage | |
|---|--:|--:|--:|---|
| 7.2 | 60 | 31 | 52% | ◐ partial |
| 7.1 | 16 | 13 | 81% | ◐ partial |
| 7.18.1 | 10 | 6 | 60% | ◐ partial |
| 7.11 | 6 | 0 | 0% | — none |
| 5 | 5 | 2 | 40% | ◐ partial |
| 7.21.6 | 5 | 4 | 80% | ◐ partial |
| 7.4.4 | 5 | 0 | 0% | — none |
| 7.21.3.1 | 4 | 3 | 75% | ◐ partial |
| 7.21.3.3 | 4 | 1 | 25% | ◐ partial |
| 7.21.7 | 4 | 0 | 0% | — none |
| 7.10 | 3 | 0 | 0% | — none |
| 7.18.5 | 3 | 3 | 100% | ✅ full |
| 7.18.6.2 | 3 | 0 | 0% | — none |
| 7.21.3.2 | 3 | 3 | 100% | ✅ full |
| 7.21.4.2 | 3 | 0 | 0% | — none |
| 7.5 | 3 | 0 | 0% | — none |
| 7.9 | 3 | 0 | 0% | — none |
| 7.18.3 | 2 | 2 | 100% | ✅ full |
| 7.20 | 2 | 0 | 0% | — none |
| 7.3 | 2 | 2 | 100% | ✅ full |
| 7.4.2 | 2 | 0 | 0% | — none |
| 7.7 | 2 | 0 | 0% | — none |
| 7.15 | 1 | 1 | 100% | ✅ full |
| 7.16 | 1 | 0 | 0% | — none |
| 7.18.2 | 1 | 1 | 100% | ✅ full |
| 7.18.4 | 1 | 1 | 100% | ✅ full |
| 7.18.8 | 1 | 1 | 100% | ✅ full |
| 7.21.4.1 | 1 | 0 | 0% | — none |
| 7.21.5 | 1 | 1 | 100% | ✅ full |
| 7.21.8 | 1 | 1 | 100% | ✅ full |

## Biggest parity gaps (highest-leverage work)

Ranked by number of files Focal misses on a clause it does not fully cover.

1. **PDF/UA-1 clause 7.2** — 29 of 60 files missed (Focal matches 31).
2. **PDF/A-2b clause 6.2.11.5** — 10 of 13 files missed (Focal matches 3).
3. **PDF/A-2b clause 6.1.9** — 7 of 7 files missed (Focal matches 0).
4. **PDF/A-2b clause 6.2.4.4** — 7 of 7 files missed (Focal matches 0).
5. **PDF/A-2b clause 6.1.13** — 6 of 15 files missed (Focal matches 9).
6. **PDF/A-2b clause 6.2.2** — 6 of 6 files missed (Focal matches 0).
7. **PDF/A-2b clause 6.2.5** — 6 of 6 files missed (Focal matches 0).
8. **PDF/UA-1 clause 7.11** — 6 of 6 files missed (Focal matches 0).
9. **PDF/A-2b clause 6.2.11.4.1** — 5 of 11 files missed (Focal matches 6).
10. **PDF/A-2b clause 6.2.11.8** — 5 of 8 files missed (Focal matches 3).

