# Jp2Codec

In-house JPEG 2000 (ISO/IEC 15444-1) decoder for the PDF library. Replaces
the `Melville.CSJ2K` NuGet dependency in `Compressors/Compressors.Jpeg2000`.

## Status

Under construction. Built bottom-up — each layer is provably correct in
isolation before the next is written. See the layer table below for what
ships today versus what's still being assembled.

| # | Layer | Status | Tests |
|---|-------|--------|-------|
| 1 | Codestream byte reader (`Codestream/CodestreamReader.cs`) | ✅ | 22 |
| 2 | Marker segments (SIZ, COD, COC, QCD, QCC, COM, SOT) + main / tile-part headers | ✅ | 43 |
| 3 | JP2 box parser (jP, ftyp, jp2h/ihdr/bpcc/colr, jp2c) | ✅ | 7 |
| 4 | MQ arithmetic decoder + 19-context init (Annex C + Table D.7) | ✅ | 11 |
| 5 | Tag tree decoder + packet-header bit reader (Annex B.10.2) | ✅ | 12 |
| 6 | Packet header parser (Tier-2, Annex B.10) | ✅ | 28 |
| 7 | EBCOT Tier-1 bit-plane decoder (Annex D) — state, contexts, SPP, MRP, CUP, pass driver + every code-block style (SEGSYM / RESTART / TERMALL / LAZY / PTERM / VSC) | ✅ | 149 |
| 8 | Dequantization (Annex E) — reversible mid-point bias + irreversible scaled reconstruction | ✅ | 22 |
| 9 | Inverse DWT — 5/3 reversible + 9/7 irreversible (Annex F) — interleave, symmetric extension, lifting, 1D/2D/multi-level | ✅ | 133 |
| 10 | Multi-component transform — RCT + ICT (Annex G) + inverse DC level shift (F.3.9) | ✅ | included in DWT tests |
| 11 | Top-level orchestrator + tile assembly | ✅ | 59 (geometry + assembler + smoke + differential) |

The public entry point (`Jp2StreamDecoder.Decode`) wires through all
layers: JP2 wrapper parse → main header → tile-part assembly → packet
iteration (LRCP) → Tier-2 packet header parse → byte slicing into
per-code-block buffers → Tier-1 decode → dequantize → multi-level IDWT
→ optional MCT (RCT / ICT) → DC level shift → tile composition. The
smallest conformance images (`test_8x8.jp2`, `test_16x16.jp2`) decode
bit-exactly against the Melville.CSJ2K reference — see
`Jp2Codec.Tests/Integration/ReferenceDifferentialTests.cs`.

## Build

```pwsh
dotnet build Jp2Codec.slnx
dotnet test Jp2Codec.slnx
```

The codec project targets `netstandard2.1` (matches `Jbig2Decoder`).
Tests run on `net10.0` with xUnit.

## Layout

```
Jp2Codec/
├── Jp2StreamDecoder.cs       — public entry point
├── Jp2DecodeResult.cs        — public result type
├── Jp2ColorSpace.cs          — public colorspace enum
├── Codestream/
│   ├── CodestreamReader.cs   — byte/marker primitive
│   ├── MarkerCode.cs         — marker constants + IsValidMarker
│   ├── MainHeader.cs         — main header walker (SOC..SOT)
│   ├── TilePartHeader.cs     — tile-part header walker (SOT..SOD)
│   └── Segments/
│       ├── SizSegment.cs     — image / tile size
│       ├── CodSegment.cs     — coding style default
│       ├── CocSegment.cs     — coding style component override
│       ├── QcdSegment.cs     — quantization default
│       ├── QccSegment.cs     — quantization component override
│       ├── SotSegment.cs     — start of tile-part
│       ├── ComSegment.cs     — comment (Latin-9 text or binary)
│       ├── ProgressionOrder.cs  — enum
│       ├── CodeBlockStyle.cs    — [Flags] enum
│       └── ...
├── Jp2File/
│   ├── BoxReader.cs          — JP2 box iterator (handles XLBox + LBox=0)
│   ├── BoxType.cs            — 4-CC constants
│   ├── Jp2FileParser.cs      — sniff JP2-vs-raw-J2K, walk required boxes
│   └── Jp2FileInfo.cs        — parse result
├── Mq/
│   ├── QeTable.cs            — Annex C Table C.2 (same as JBIG2's T.88 E.1)
│   ├── Jp2MqDecoder.cs       — Annex C arithmetic decoder
│   └── Jp2MqContextSet.cs    — 19-context init per Table D.7
├── Tier2/
│   ├── PacketHeaderBitReader.cs  — bit reader with 0xFF stuff-bit logic
│   ├── TagTreeDecoder.cs         — Annex B.10.2 tag tree
│   ├── CodingPassLengthCode.cs   — Table B.4 variable-length code
│   ├── LblockIncrement.cs        — comma code for Lblock updates
│   ├── CodeBlockState.cs         — per-codeblock packet-spanning state
│   ├── PrecinctSubband.cs        — code-block grid + inclusion / zero-bp trees
│   ├── Precinct.cs               — list of subbands at one resolution
│   ├── CodeBlockContribution.cs  — packet contribution descriptor
│   ├── PacketHeader.cs           — parser result type
│   └── PacketHeaderParser.cs     — Annex B.10 packet header walk
├── Tier1/
│   ├── Tier1State.cs                       — per-coefficient flag/magnitude grid
│   ├── SubbandOrientation.cs               — LL/HL/LH/HH enum
│   ├── Tier1Contexts.cs                    — ZC/SC/MR context lookup tables (D.5)
│   ├── SignificancePropagationPass.cs      — D.3.1 SPP
│   ├── MagnitudeRefinementPass.cs          — D.3.3 MRP
│   ├── CleanupPass.cs                      — D.3.4 CUP with run-length aggregation
│   ├── RawSignificancePropagationPass.cs   — D.6 raw SPP under LAZY
│   ├── RawMagnitudeRefinementPass.cs       — D.6 raw MRP under LAZY
│   ├── Tier1RawBitReader.cs                — MSB-first bit reader w/ 0xFF stuff-bit (D.6)
│   ├── Tier1CoefficientExtractor.cs        — final signed-coefficient extraction
│   └── Tier1CodeBlockDecoder.cs            — pass-sequence driver with SEGSYM / RESTART / LAZY / VSC;
│                                             TERMALL via per-pass MQ feeding; PTERM transparent
├── Quantization/
│   ├── SubbandDescriptor.cs                — (orientation, decomposition level n_b)
│   ├── SubbandLayout.cs                    — QCD-order subband enumeration + Log2Gain table
│   ├── SubbandQuantization.cs              — per-subband (epsilon_b, mu_b, M_b, Δ_b, R_b)
│   ├── QuantizationTable.cs                — QCD/QCC → SubbandQuantization expansion
│   └── SubbandDequantizer.cs               — E.1.1 / E.1.2 reconstruction with mid-point bias
└── Wavelet/
    ├── WaveletConstants.cs                 — α/β/γ/δ/K for 9/7 (Table F.4)
    ├── InverseInterleave.cs                — 2D_INTERLEAVE step (F.3.3 / Figure F.8) per row
    ├── SymmetricExtension.cs               — 1D_EXTR whole-sample symmetric reflection (F.3.7)
    ├── InverseLifting53.cs                 — 1D_FILTR5-3R lifting (F-5 + F-6) on int[]
    ├── InverseLifting97.cs                 — 1D_FILTR9-7I 6-step lifting (F-7) on float[]
    ├── InverseDwt1D.cs                     — 1D_SR (F.3.6): interleave + lifting per parity
    ├── InverseDwt2D.cs                     — 2D_SR (F.3.2): 2D_INTERLEAVE → HOR_SR → VER_SR
    ├── WaveletLevel.cs                     — per-level (HL/LH/HH + parent canvas parities)
    └── MultiLevelInverseDwt.cs             — IDWT procedure (F.3.1): walks levels N_L → 1
```

## Spec references

- **ISO/IEC 15444-1:2019** (free at https://www.itu.int/rec/T-REC-T.800)
  is the normative reference. Annex letters are stable across editions.
- **OpenJPEG** is the canonical open-source decoder reference and uses
  the same context-index convention this codec uses
  (`Mq/Jp2MqContextSet.cs`).
- The MQ probability table is shared with JBIG2 (T.88 Annex E.1) — values
  duplicated in `Mq/QeTable.cs` so the two codecs are independently
  shippable.

## Why a from-scratch decoder

Melville.CSJ2K is a port of an old CSJ2K codebase carrying its own JNI-era
shape and has lived on NuGet without active maintenance. Owning the codec
in-tree lets us:

- Match the codec API to `Jp2DecodeResult` rather than CSJ2K's
  `PortableImage` (smaller surface, no allocation per `GetComponent` call).
- Share the bottom-of-stack arithmetic coder with `Jbig2Decoder` instead
  of having two implementations of the same Annex E/C state machine.
- Be in a position to add JPEG 2000 *encoding* support later without
  taking on a separate dependency that doesn't encode.

## Test strategy

Each layer carries unit tests that pin its behaviour against hand-built
inputs (see `Codestream/HeaderBytes.cs` and `Jp2File/Jp2Bytes.cs` test
helpers). Higher layers will pin against the OpenJPEG/Kakadu test corpora
once the orchestrator (Layer 11) is wired up. The discipline mirrors the
JBIG2 decoder — failures must localise to a stage rather than land as
a whole-image-diff mystery.
