# JpegDecoder Test Corpus

Files used by `IdentifyDifferentialTests`, `SequentialDecodeDifferentialTests`,
`ProgressiveDecodeDifferentialTests`, and `App14MetadataTests` to validate
the new decoder against `JpegLibrary` as oracle.

## Layout

| Directory | Source | Count | Coverage |
|---|---|---|---|
| `graduated/` | `PDF/ImageLibrary/TestImages/jpeg_test/` | 5 | Synthesized simple patterns (solid colours, 4:4:4 color) |
| `baseline/` | `PDF/JpegLibrary/tests/Assets/baseline/` | 2 | Natural-scene baselines (cramps, lake) — 4:2:0 sampling |
| `subsampled/` | `PDF/ImageLibrary/TestImages/jpeg_test/level5_color_420/` | 2 | 4:2:0 chroma subsampling |
| `non_aligned/` | `PDF/ImageLibrary/TestImages/jpeg_test/level6_non_aligned/` | 2 | Image dimensions not aligned to MCU boundary |
| `progressive/` | `PDF/JpegLibrary/tests/Assets/huffman_progressive/` | 2 | SOF2 progressive (progress, yellowcat with restart markers) |
| `turbo/` | libjpeg-turbo `testimages/` (BSD) | 2 | Canonical IJG test references (testorig, testimgint) |
| `pdf_extracted/` | Extracted from PDFs in `PDF/PDFs/` | 2 | Real-world JPEGs extracted from PDF DCTDecode streams: APP14 t=1 RGB; grayscale with DRI restart markers + H=V=2 sampling |
| `cmyk_real/` | TwelveMonkeys `imageio-jpeg/.../jpeg/` (BSD-3-Clause) | 6 | Real CMYK + YCCK samples covering all APP14 transforms + edge cases |

## Why these particular files

The corpus targets the cross-product of:

| Axis | Values covered |
|---|---|
| SOF marker | SOF0 (baseline), SOF2 (progressive) |
| Chroma subsampling | 4:4:4, 4:2:2, 4:2:0, 4:4:0, H=V=2 with single component |
| Components | 1 (gray), 3 (YCbCr), 4 (CMYK / YCCK) |
| APP14 transform | absent, 0 (CMYK/none), 1 (YCbCr), 2 (YCCK) |
| Restart interval | absent, present |
| Edge cases | Non-MCU-aligned dimensions; APP14 transform mismatch with component count |

## CMYK corpus details

| File | Width × Height | nc | APP14 transform | Notes |
|---|---|---|---|---|
| `cmyk-sample.jpg` | 160 × 227 | 4 | 0 | Standard Photoshop-style inverted CMYK |
| `cmyk-sample-no-icc.jpg` | 114 × 199 | 4 | 0 | CMYK without embedded ICC profile |
| `cmyk_invalid_icc.jpg` | 493 × 500 | 4 | 0 | CMYK with mislabeled ICC profile — decoder must ignore ICC |
| `cmyk_ycck_transform2.jpg` | 183 × 283 | 4 | 2 | **Adobe YCCK encoding** — the path that was dead code at `DctDecodeFilter.cs:67` |
| `edge_app14_ycck_3channel.jpg` | 310 × 384 | 3 | 2 | Edge case: APP14 says YCCK but only 3 channels |
| `edge_app14_ycbcr_grayscale.jpg` | 8 × 248 | 1 | 1 | Edge case: APP14 says YCbCr but grayscale (1 channel) |

The TwelveMonkeys collection is licensed BSD-3-Clause; redistributable.
The libjpeg-turbo testimages are also BSD-style licensed.

## How tests discover the corpus

`CorpusFiles.EveryJpeg()` walks `Corpus/` recursively for `*.jpg` and
`*.jpeg`. Each file participates in every theory that takes a corpus
file — `IdentifyDifferentialTests`, `SequentialDecodeDifferentialTests`
(skips progressive), `ProgressiveDecodeDifferentialTests` (skips
non-progressive).

`App14MetadataTests` uses `[InlineData]` to pin specific expectations
per file — that's the contract Phase 8's `DctDecodeFilter` rewrite
depends on.

## Adding new files

1. Drop the file under an appropriate subdirectory.
2. If it carries metadata that other tests should pin, add an
   `[InlineData]` row to `App14MetadataTests`.
3. Run `dotnet test`. The corpus-walking tests pick it up automatically.
