using System;
using System.Collections.Generic;
using Jp2Codec.Codestream.Segments;

namespace Jp2Codec.Tier2
{
    /// <summary>
    /// Parses a Tier-2 packet header (ISO/IEC 15444-1 B.10) given the
    /// precinct's persistent state and a bit reader positioned at the
    /// start of the header. Mutates the precinct's per-codeblock state
    /// (Lblock, included flag, zero-bit-plane count) and the precinct's
    /// inclusion / zero-bit-plane tag trees.
    ///
    /// On return, the bit reader is positioned at the byte after the
    /// packet header — the first byte of the packet body.
    ///
    /// The <paramref name="codeBlockStyle"/> argument controls how a
    /// contribution's new coding passes are split into terminated byte
    /// segments (per B.10.7.1): the parser asks
    /// <see cref="SegmentEnumerator"/> for one (passCount, isRaw) tuple
    /// per segment and reads one Lblock-based length code per segment.
    /// </summary>
    internal static class PacketHeaderParser
    {
        public static PacketHeader Parse(
            Precinct precinct, int layerIndex, PacketHeaderBitReader reader,
            CodeBlockStyle codeBlockStyle)
        {
            if (precinct is null) throw new ArgumentNullException(nameof(precinct));
            if (layerIndex < 0) throw new ArgumentOutOfRangeException(nameof(layerIndex));
            if (reader is null) throw new ArgumentNullException(nameof(reader));

            // Bit 0 — zero-length packet flag. '0' = empty packet.
            if (reader.ReadBit() == 0)
            {
                reader.AlignToByte();
                return PacketHeader.Empty;
            }

            var contributions = new List<CodeBlockContribution>();

            for (var subbandIndex = 0; subbandIndex < precinct.Subbands.Length; subbandIndex++)
            {
                PrecinctSubband subband = precinct.Subbands[subbandIndex];

                for (var y = 0; y < subband.CodeBlockRowCount; y++)
                {
                    for (var x = 0; x < subband.CodeBlockColumnCount; x++)
                    {
                        CodeBlockState block = subband.CodeBlocks[y, x];
                        bool firstInclusion;

                        if (!block.Included)
                        {
                            // The inclusion tag tree stores the layer at which
                            // the block first appears. Asking "leaf < L + 1"
                            // answers "is the block first-included at or
                            // before layer L?". State on the tag tree is
                            // preserved across calls so progressive thresholds
                            // only consume the new bits the encoder added.
                            bool included = subband.InclusionTree
                                .DecodeLessThan(x, y, layerIndex + 1, reader);
                            if (!included) continue;

                            firstInclusion = true;
                            block.Included = true;
                            block.ZeroBitPlanes =
                                subband.ZeroBitPlanesTree.DecodeValue(x, y, reader);
                        }
                        else
                        {
                            // Already first-included; one bit signals whether
                            // the block contributes new coding passes in this
                            // packet.
                            if (reader.ReadBit() == 0) continue;
                            firstInclusion = false;
                        }

                        int newPasses = CodingPassLengthCode.Decode(reader);
                        block.Lblock += LblockIncrement.Read(reader);

                        IReadOnlyList<(int PassCount, bool IsRaw)> segmentDescriptors =
                            SegmentEnumerator.Enumerate(block.CompletedPasses, newPasses, codeBlockStyle);

                        var segments = new ContributionSegment[segmentDescriptors.Count];
                        for (var s = 0; s < segmentDescriptors.Count; s++)
                        {
                            (int passCount, bool isRaw) = segmentDescriptors[s];
                            int lengthBits = block.Lblock + FloorLog2(passCount);
                            if (lengthBits > 31)
                                throw new System.IO.InvalidDataException(
                                    "Packet header length field exceeded 31 bits; codestream is malformed.");
                            int segmentBytes = reader.ReadBits(lengthBits);
                            segments[s] = new ContributionSegment(passCount, segmentBytes, isRaw);
                        }

                        contributions.Add(new CodeBlockContribution(
                            subbandIndex, x, y,
                            firstInclusion,
                            firstInclusion ? block.ZeroBitPlanes : 0,
                            segments));

                        block.CompletedPasses += newPasses;
                    }
                }
            }

            reader.AlignToByte();
            return new PacketHeader(false, contributions);
        }

        // floor(log2(value)) for value >= 1.
        private static int FloorLog2(int value)
        {
            int result = 0;
            while ((value >>= 1) != 0) result++;
            return result;
        }
    }
}
