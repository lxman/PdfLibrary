using System;
using System.Collections.Generic;
using Jp2Codec.Codestream.Segments;

namespace Jp2Codec.Tier2
{
    /// <summary>
    /// Walks a code-block contribution's new coding passes and yields one
    /// segment per terminated byte run, matching OpenJPEG's
    /// <c>opj_t1_init_seg</c> rules:
    ///
    /// <list type="bullet">
    ///   <item>Default style — one segment per contribution.</item>
    ///   <item>TERMALL — one pass per segment.</item>
    ///   <item>LAZY (no TERMALL) — passes 0..9 are one MQ segment; from
    ///     pass 10 onward SPP+MRP pairs are RAW segments (max 2 passes)
    ///     and the CUP between them is its own one-pass MQ segment.</item>
    ///   <item>LAZY + TERMALL — one pass per segment with the LAZY
    ///     raw-vs-MQ classification applied per pass.</item>
    ///   <item>RESTART / VSC / SEGSYM / PTERM — do not affect segment
    ///     count (per Annex D.5.2 / D.5.3 / D.5.4 / D.5.5).</item>
    /// </list>
    ///
    /// Segments never span code-block contributions: the encoder
    /// terminates the MQ stream at the end of each contribution (A.7.2 /
    /// D.4.2), so each contribution starts a fresh segment.
    /// </summary>
    internal static class SegmentEnumerator
    {
        /// <summary>Pass index at which LAZY switches SPP/MRP to raw (Annex D.6 / Table D.9).</summary>
        public const int FirstBypassPassIndex = 10;

        /// <summary>
        /// Enumerate the segments for a contribution adding
        /// <paramref name="numNewPasses"/> new passes after the block has
        /// already decoded <paramref name="completedPasses"/> passes.
        /// </summary>
        public static IReadOnlyList<(int PassCount, bool IsRaw)> Enumerate(
            int completedPasses, int numNewPasses, CodeBlockStyle style)
        {
            if (completedPasses < 0) throw new ArgumentOutOfRangeException(nameof(completedPasses));
            if (numNewPasses < 0) throw new ArgumentOutOfRangeException(nameof(numNewPasses));
            if (numNewPasses == 0) return Array.Empty<(int, bool)>();

            bool lazy = (style & CodeBlockStyle.SelectiveBypass) != 0;
            bool termAll = (style & CodeBlockStyle.TerminationOnPass) != 0;

            var result = new List<(int, bool)>();
            int remaining = numNewPasses;
            int passIdx = completedPasses;

            while (remaining > 0)
            {
                int maxPasses;
                bool isRaw;

                if (termAll)
                {
                    // TERMALL: every pass is its own segment. LAZY may still
                    // mark this pass as raw.
                    maxPasses = 1;
                    isRaw = lazy && IsRawSlot(passIdx);
                }
                else if (lazy)
                {
                    if (passIdx < FirstBypassPassIndex)
                    {
                        // Still in the initial MQ block — fill up to pass 10.
                        maxPasses = FirstBypassPassIndex - passIdx;
                        isRaw = false;
                    }
                    else
                    {
                        // In the LAZY region. Pass type per Annex D.6:
                        //   (passIdx + 2) % 3 → 0=SPP, 1=MRP, 2=CUP.
                        int passType = (passIdx + 2) % 3;
                        if (passType == 2)
                        {
                            // CUP — single-pass MQ segment.
                            maxPasses = 1;
                            isRaw = false;
                        }
                        else
                        {
                            // SPP or MRP — raw. If we start at SPP we can
                            // pull both SPP and MRP into one segment; if we
                            // start at MRP (because SPP was in the prior
                            // contribution) we only get the one pass.
                            maxPasses = passType == 0 ? 2 : 1;
                            isRaw = true;
                        }
                    }
                }
                else
                {
                    // Default style (or RESTART-only — context reset is
                    // internal and doesn't split segments). All remaining
                    // passes go in one segment.
                    maxPasses = remaining;
                    isRaw = false;
                }

                int passesInSegment = Math.Min(remaining, maxPasses);
                result.Add((passesInSegment, isRaw));
                remaining -= passesInSegment;
                passIdx += passesInSegment;
            }

            return result;
        }

        private static bool IsRawSlot(int passIdx)
        {
            if (passIdx < FirstBypassPassIndex) return false;
            int passType = (passIdx + 2) % 3;
            return passType != 2;
        }
    }
}
