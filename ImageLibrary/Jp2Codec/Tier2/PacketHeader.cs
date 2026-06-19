using System;
using System.Collections.Generic;

namespace Jp2Codec.Tier2
{
    /// <summary>
    /// Result of parsing a Tier-2 packet header. An empty packet (signalled
    /// by the leading zero-length flag bit in the header) carries no
    /// contributions; a non-empty header may still yield zero contributions
    /// if no code-blocks have reached their first-inclusion threshold yet.
    /// </summary>
    internal sealed class PacketHeader
    {
        public bool IsEmpty { get; }
        public IReadOnlyList<CodeBlockContribution> Contributions { get; }

        public PacketHeader(bool isEmpty, IReadOnlyList<CodeBlockContribution> contributions)
        {
            IsEmpty = isEmpty;
            Contributions = contributions ?? throw new ArgumentNullException(nameof(contributions));
        }

        public static PacketHeader Empty { get; } = new(true, Array.Empty<CodeBlockContribution>());
    }
}
