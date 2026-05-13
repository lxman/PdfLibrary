using System;
using System.IO;
using Jp2Codec.Mq;

namespace Jp2Codec.Tier1
{
    /// <summary>
    /// Per-code-block EBCOT Tier-1 driver. Owns the persistent state grid and
    /// the 19-context MQ context array, and sequences SPP / MRP / CUP passes
    /// across the contributions a single code block accumulates over multiple
    /// packets. Supports every code-block style described in Annex D.5–D.7:
    /// SEGSYM (D.5.4), RESTART (D.5.5), LAZY (D.6), and VSC (D.7). TERMALL is
    /// supported through the API by feeding a fresh <see cref="Jp2MqDecoder"/>
    /// per pass (see remarks on <see cref="RunPasses"/>). PTERM (D.4.2 / D.6)
    /// is decoded transparently — it is an encoder-side termination policy and
    /// our MQ byte-length-driven decoder reads PTERM-flushed segments
    /// correctly without special handling; optional consistency verification
    /// (Taubman &amp; Marcellin §12.3.2) is not implemented.
    ///
    /// Pass-to-bit-plane mapping (Annex D.6): pass 0 is the cleanup pass at
    /// the first non-zero bit-plane; passes 1, 2, 3 are SPP, MRP, CUP at
    /// the next bit-plane down; the SPP/MRP/CUP cycle repeats one bit-plane
    /// at a time until the requested pass count is exhausted. The first
    /// non-zero bit-plane index is <c>Mb − 1 − ZeroBitPlanes</c>, where
    /// <c>Mb</c> is the encoder's bit-plane count for the subband and
    /// <c>ZeroBitPlanes</c> is the per-block count signalled by the Tier-2
    /// zero-bit-plane tag tree. The caller computes that and passes it as
    /// <paramref name="firstBitPlane"/>.
    /// </summary>
    internal sealed class Tier1CodeBlockDecoder
    {
        // Segmentation symbol: MSB-first bits {1, 0, 1, 0} appended to every
        // cleanup pass when the SEGSYM style is enabled (Annex D.5.4 / Table
        // A-19). The four bits are coded against the uniform context (18).
        private static readonly int[] SegSymExpectedBits = [1, 0, 1, 0];

        // Index of the first pass that goes raw under LAZY (Annex D.6 / Table
        // D.9): MQ owns the 4 most-significant bit-planes (passes 0–9, i.e.
        // CUP+SPP+MRP+CUP for bp 1, then 3-pass cycles for bp 2..4); from the
        // 5th non-zero bit-plane onward SPP and MRP go raw while CUP stays MQ.
        private const int FirstBypassPassIndex = 10;

        private readonly Tier1State _state;
        private readonly byte[] _contexts;
        private readonly SubbandOrientation _orientation;
        private readonly int _firstBitPlane;
        private readonly bool _segSym;
        private readonly bool _restart;
        private readonly bool _bypass;
        private readonly bool _vsc;
        private int _passesCompleted;

        public Tier1CodeBlockDecoder(
            int width, int height,
            SubbandOrientation orientation,
            int firstBitPlane,
            bool segSym = false,
            bool restart = false,
            bool bypass = false,
            bool vsc = false)
        {
            if (firstBitPlane < 0)
                throw new ArgumentOutOfRangeException(nameof(firstBitPlane), firstBitPlane, null);

            _state = new Tier1State(width, height);
            _contexts = Jp2MqContextSet.CreateInitialised();
            _orientation = orientation;
            _firstBitPlane = firstBitPlane;
            _segSym = segSym;
            _restart = restart;
            _bypass = bypass;
            _vsc = vsc;
            _passesCompleted = 0;
        }

        internal Tier1State State => _state;
        internal int PassesCompleted => _passesCompleted;

        /// <summary>
        /// Produce the final signed quantized coefficient grid
        /// (<c>[Height, Width]</c> indexed <c>[y, x]</c>) from the current
        /// state. Call once all packet contributions have been fed through
        /// <see cref="RunPasses"/>. The returned grid is the input to
        /// dequantization (Annex E).
        /// </summary>
        public int[,] ExtractCoefficients() =>
            Tier1CoefficientExtractor.Extract(_state);

        /// <summary>
        /// Run <paramref name="passCount"/> additional passes using bytes
        /// supplied via <paramref name="mq"/>. The MQ decoder is normally a
        /// fresh one carrying the bytes accumulated since the previous call
        /// (under default style every contribution extends the same logical
        /// stream, so the caller will concatenate body bytes from preceding
        /// contributions and hand the whole thing in as one decoder).
        /// </summary>
        /// <remarks>
        /// TERMALL (Table A-19 bit 2) is supported by calling this method
        /// once per pass with a fresh MQ decoder over each pass's own byte
        /// segment; the driver carries pass state (passesCompleted, the flag
        /// grid, the context array) across those calls. RESTART (bit 1) is
        /// enabled via the <c>restart</c> constructor flag and reinitialises
        /// the context array at the start of every pass.
        /// </remarks>
        public void RunPasses(Jp2MqDecoder mq, int passCount)
        {
            if (mq is null) throw new ArgumentNullException(nameof(mq));
            if (passCount < 0)
                throw new ArgumentOutOfRangeException(nameof(passCount), passCount, null);

            for (var i = 0; i < passCount; i++)
            {
                if (IsRawSlot(_passesCompleted))
                    throw new InvalidOperationException(
                        $"Pass {_passesCompleted} is a raw-coded slot under the " +
                        "selective arithmetic coding bypass (LAZY) style; call " +
                        $"{nameof(RunRawPasses)} for this pass instead.");
                RunOnePass(mq);
            }
        }

        /// <summary>
        /// Run <paramref name="passCount"/> raw-coded passes under the LAZY
        /// style, reading bits from <paramref name="data"/> via the bit
        /// reader described in <see cref="Tier1RawBitReader"/>. Per Table D.9
        /// the SPP and MRP of a single bit-plane share one byte segment
        /// terminated after the MRP, so without TERMALL the caller will pass
        /// <c>passCount=2</c>; with TERMALL each pass is its own segment and
        /// <c>passCount=1</c>. Throws if LAZY isn't enabled or if any pass in
        /// the requested range would be a cleanup pass (CUP keeps MQ under
        /// LAZY — see <see cref="RunPasses"/> for those).
        /// </summary>
        public void RunRawPasses(byte[] data, int offset, int length, int passCount)
        {
            if (!_bypass)
                throw new InvalidOperationException(
                    $"{nameof(RunRawPasses)} requires the selective arithmetic " +
                    "coding bypass (LAZY) style to be enabled on the decoder.");
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (passCount < 0)
                throw new ArgumentOutOfRangeException(nameof(passCount), passCount, null);

            var reader = new Tier1RawBitReader(data, offset, length);
            for (var i = 0; i < passCount; i++)
            {
                if (!IsRawSlot(_passesCompleted))
                    throw new InvalidOperationException(
                        $"Pass {_passesCompleted} is an MQ-coded slot under LAZY; " +
                        $"call {nameof(RunPasses)} for this pass instead.");
                RunOneRawPass(reader);
            }
        }

        private void RunOnePass(Jp2MqDecoder mq)
        {
            (int bitPlane, PassKind kind) = LookupPass(_passesCompleted);

            if (bitPlane < 0)
                throw new InvalidDataException(
                    $"Pass {_passesCompleted} would decode below bit-plane 0; " +
                    $"too many passes for the {_firstBitPlane + 1} bit-planes available.");

            if (_restart)
                Jp2MqContextSet.ResetInPlace(_contexts);

            switch (kind)
            {
                case PassKind.SignificancePropagation:
                    _state.ResetVisited();
                    SignificancePropagationPass.Run(_state, mq, _contexts, _orientation, bitPlane, _vsc);
                    break;
                case PassKind.MagnitudeRefinement:
                    MagnitudeRefinementPass.Run(_state, mq, _contexts, bitPlane, _vsc);
                    break;
                case PassKind.Cleanup:
                    // First-of-block cleanup gets a visited reset too — the
                    // grid starts zeroed but the call cost is negligible and
                    // makes intent explicit.
                    if (_passesCompleted == 0) _state.ResetVisited();
                    CleanupPass.Run(_state, mq, _contexts, _orientation, bitPlane, _vsc);
                    if (_segSym) ConsumeSegmentationSymbol(mq);
                    break;
            }

            _passesCompleted++;
        }

        private void RunOneRawPass(Tier1RawBitReader reader)
        {
            (int bitPlane, PassKind kind) = LookupPass(_passesCompleted);
            if (bitPlane < 0)
                throw new InvalidDataException(
                    $"Pass {_passesCompleted} would decode below bit-plane 0; " +
                    $"too many passes for the {_firstBitPlane + 1} bit-planes available.");

            switch (kind)
            {
                case PassKind.SignificancePropagation:
                    _state.ResetVisited();
                    RawSignificancePropagationPass.Run(_state, reader, bitPlane, _vsc);
                    break;
                case PassKind.MagnitudeRefinement:
                    RawMagnitudeRefinementPass.Run(_state, reader, bitPlane);
                    break;
                default:
                    // Filtered by IsRawSlot; unreachable.
                    throw new InvalidOperationException(
                        "Cleanup passes are MQ-coded under LAZY and cannot run raw.");
            }
            _passesCompleted++;
        }

        // A pass index lies in a raw-coded slot iff LAZY is on, the pass is at
        // or past the 5th non-zero bit-plane (index >= 10), and it is not a
        // cleanup pass — Annex D.6 / Table D.9.
        private bool IsRawSlot(int passIndex)
        {
            if (!_bypass || passIndex < FirstBypassPassIndex) return false;
            var kind = (PassKind)((passIndex + 2) % 3);
            return kind != PassKind.Cleanup;
        }

        private void ConsumeSegmentationSymbol(Jp2MqDecoder mq)
        {
            // Annex D.5.4: four bits coded against the uniform context (18),
            // MSB-first, value 0xA (binary 1010). Mismatch means the stream
            // is corrupt — bail loudly so callers can roll back rather than
            // continue with a desynced MQ.
            for (var i = 0; i < SegSymExpectedBits.Length; i++)
            {
                int bit = mq.Decode(ref _contexts[Jp2MqContextSet.Uniform]);
                if (bit != SegSymExpectedBits[i])
                    throw new InvalidDataException(
                        $"SEGSYM mismatch at bit {i}: expected " +
                        $"{SegSymExpectedBits[i]}, got {bit}. " +
                        "Code-block stream is corrupt or SEGSYM was not " +
                        "actually enabled on this block.");
            }
        }

        /// <summary>Maps pass index (0-based, monotonically increasing across
        /// contributions) to (bit-plane, pass kind) per Annex D.6.</summary>
        private (int BitPlane, PassKind Kind) LookupPass(int passIndex)
        {
            // Pass 0: CUP at firstBitPlane.
            // Pass 1: SPP at firstBitPlane - 1.
            // Pass 2: MRP at firstBitPlane - 1.
            // Pass 3: CUP at firstBitPlane - 1.
            // Pass 4: SPP at firstBitPlane - 2.
            // …
            int planeOffset = (passIndex + 2) / 3;
            int bitPlane = _firstBitPlane - planeOffset;
            var kind = (PassKind)((passIndex + 2) % 3);
            return (bitPlane, kind);
        }

        internal enum PassKind
        {
            SignificancePropagation = 0,
            MagnitudeRefinement = 1,
            Cleanup = 2,
        }
    }
}
