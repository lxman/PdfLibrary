using System;

namespace Jp2Codec.Tiles
{
    /// <summary>
    /// One terminated byte segment of a code-block, captured from the Tier-2
    /// packet body and pending Tier-1 decode. Default style buffers one
    /// segment per contribution; TERMALL one per pass; LAZY alternates MQ
    /// and raw segments. The Tier-1 orchestrator iterates these in order
    /// and routes each to either <see cref="Tier1.Tier1CodeBlockDecoder.RunPasses"/>
    /// or <see cref="Tier1.Tier1CodeBlockDecoder.RunRawPasses"/>.
    /// </summary>
    internal sealed class CodeBlockSegment
    {
        public byte[] Bytes { get; }
        public int PassCount { get; }
        public bool IsRaw { get; }

        public CodeBlockSegment(byte[] bytes, int passCount, bool isRaw)
        {
            Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
            PassCount = passCount;
            IsRaw = isRaw;
        }
    }
}
