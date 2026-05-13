namespace Jp2Codec.Mq
{
    /// <summary>
    /// Per-codeblock MQ context state (ISO/IEC 15444-1 Table D.7, "Initial
    /// states for all 18 contexts" in the 1999 CD's Table C-6). Every
    /// codeblock has 19 contexts that are reset to spec-defined initial states
    /// at the start of decoding (and again on coding-pass boundaries when the
    /// RESTART code-block style is set).
    ///
    /// Context indices follow the OpenJPEG/Kakadu convention:
    /// <list type="table">
    ///   <listheader><term>Index</term><term>Purpose</term><term>Init (Qe, MPS)</term></listheader>
    ///   <item><term>0</term><term>Zero-coding — "all zero neighbours" (D-1 row 1)</term><term>(4, 0)</term></item>
    ///   <item><term>1..8</term><term>Zero-coding — remaining 8 contexts</term><term>(0, 0)</term></item>
    ///   <item><term>9..13</term><term>Sign coding</term><term>(0, 0)</term></item>
    ///   <item><term>14..16</term><term>Magnitude refinement</term><term>(0, 0)</term></item>
    ///   <item><term>17</term><term>Run-length (cleanup pass aggregate)</term><term>(3, 0)</term></item>
    ///   <item><term>18</term><term>Uniform (sign + 4-bit runlength)</term><term>(46, 0)</term></item>
    /// </list>
    ///
    /// The "all zero neighbours" case is special-cased per Table C-6 because
    /// it is the by-far most common decision at the start of a code-block
    /// (every coefficient starts with no significant neighbours) and the
    /// non-zero starting Qe-state lets the probability estimator settle
    /// faster than starting from index 0.
    /// </summary>
    internal static class Jp2MqContextSet
    {
        public const int Count = 19;

        // Index offsets (mirror OpenJPEG's T1_CTXNO_* constants).
        public const int ZeroCoding = 0;
        public const int SignCoding = 9;
        public const int MagnitudeRefinement = 14;
        public const int RunLength = 17;
        public const int Uniform = 18;

        /// <summary>
        /// Allocate a fresh 19-entry context state array initialised per Table D.7.
        /// Each byte packs the Qe-table index in bits 0..6 and the MPS sense in bit 7.
        /// Initial MPS is 0 for every context, so the byte equals the initial Qe index.
        /// </summary>
        public static byte[] CreateInitialised()
        {
            var ctx = new byte[Count];
            // "All zero neighbours" zero-coding context starts at Qe-state 4.
            ctx[ZeroCoding + 0] = 4;
            // All other zero-coding (1..8), sign (9..13), magnitude refinement
            // (14..16) start at Qe-state 0 (zero already; left implicit).
            // Run-length context: Qe-state 3.
            ctx[RunLength] = 3;
            // Uniform context: Qe-state 46.
            ctx[Uniform] = 46;
            return ctx;
        }

        /// <summary>Reset an existing context array to the initial state (used by RESTART pass-boundary code-block style).</summary>
        public static void ResetInPlace(byte[] ctx)
        {
            for (var i = 0; i < Count; i++) ctx[i] = 0;
            ctx[ZeroCoding + 0] = 4;
            ctx[RunLength] = 3;
            ctx[Uniform] = 46;
        }
    }
}
