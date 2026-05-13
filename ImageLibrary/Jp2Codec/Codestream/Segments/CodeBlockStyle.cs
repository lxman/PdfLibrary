using System;

namespace Jp2Codec.Codestream.Segments
{
    /// <summary>
    /// Code-block coding-style flags per ISO/IEC 15444-1 Table A-19.
    /// Carried in the Code-block style byte of SPcod/SPcoc.
    /// </summary>
    [Flags]
    internal enum CodeBlockStyle : byte
    {
        None = 0,

        /// <summary>
        /// Selective arithmetic coding bypass (LAZY mode). When set, refinement
        /// and significance passes from bit-plane 5 onward use raw bit decoding
        /// instead of MQ — gives slight throughput at small compression cost.
        /// </summary>
        SelectiveBypass = 0x01,

        /// <summary>
        /// Reset context probabilities on coding pass boundaries (RESTART).
        /// Re-initialises MQ context state at the start of each pass.
        /// </summary>
        ResetContextOnPass = 0x02,

        /// <summary>
        /// Termination on each coding pass (TERMALL). Forces an MQ flush at
        /// the end of every pass, enabling truncation at pass granularity.
        /// </summary>
        TerminationOnPass = 0x04,

        /// <summary>
        /// Vertically causal context (VSC). Used for low-memory line-based
        /// decoders — restricts context formation to the current stripe.
        /// </summary>
        VerticallyCausal = 0x08,

        /// <summary>
        /// Predictable termination (PTERM). Termination state encodes a
        /// checksum the decoder can verify for error detection.
        /// </summary>
        PredictableTermination = 0x10,

        /// <summary>
        /// Segmentation symbols (SEGSYM). Each cleanup pass ends with an
        /// MQ-coded 0xA pattern; if decoded otherwise, the codeblock is
        /// corrupt and the decoder can roll back.
        /// </summary>
        SegmentationSymbols = 0x20,
    }
}
