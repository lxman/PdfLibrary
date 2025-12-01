using System;
using System.Collections.Generic;
using System.IO;

namespace Compressors.Jpeg2k;

/// <summary>
/// EBCOT Tier-2 coder for JPEG2000.
/// Handles packet formation and layer assembly.
/// Based on ITU-T T.800 Annex B.
/// </summary>
public static class EbcotTier2
{
    /// <summary>
    /// Assembles packets from encoded code-blocks.
    /// </summary>
    /// <param name="subbands">Subbands containing encoded code-blocks</param>
    /// <param name="numLayers">Number of quality layers</param>
    /// <param name="progressionOrder">Progression order</param>
    /// <returns>List of packets in order</returns>
    public static List<Packet> AssemblePackets(
        Subband[] subbands,
        int numLayers,
        byte progressionOrder = Jp2kConstants.ProgressionLRCP)
    {
        var packets = new List<Packet>();

        // For simplicity, use LRCP (Layer-Resolution-Component-Position) order
        // with single component and single tile

        int maxLevel = 0;
        foreach (var subband in subbands)
        {
            maxLevel = Math.Max(maxLevel, subband.Level);
        }

        for (int layer = 0; layer < numLayers; layer++)
        {
            for (int resolution = 0; resolution <= maxLevel; resolution++)
            {
                var packet = new Packet
                {
                    Layer = layer,
                    Resolution = resolution,
                    Component = 0,
                    Precinct = 0
                };

                // Collect code-blocks for this resolution level
                foreach (var subband in subbands)
                {
                    if (subband.Level != resolution &&
                        !(subband.Type == Jp2kConstants.SubbandLL && subband.Level == maxLevel && resolution == maxLevel))
                    {
                        continue;
                    }

                    if (subband.CodeBlocks == null)
                        continue;

                    int blocksY = subband.CodeBlocks.GetLength(0);
                    int blocksX = subband.CodeBlocks.GetLength(1);

                    for (int by = 0; by < blocksY; by++)
                    {
                        for (int bx = 0; bx < blocksX; bx++)
                        {
                            var codeBlock = subband.CodeBlocks[by, bx];
                            if (codeBlock.EncodedData != null && codeBlock.EncodedData.Length > 0)
                            {
                                // Calculate passes to include in this layer
                                int passesThisLayer = CalculatePassesForLayer(
                                    codeBlock, layer, numLayers);

                                if (passesThisLayer > 0)
                                {
                                    packet.CodeBlockContributions.Add(new CodeBlockContribution
                                    {
                                        CodeBlock = codeBlock,
                                        NewPasses = passesThisLayer,
                                        SubbandType = subband.Type
                                    });
                                }
                            }
                        }
                    }
                }

                packets.Add(packet);
            }
        }

        return packets;
    }

    /// <summary>
    /// Calculates how many coding passes to include for a layer.
    /// Uses simple rate allocation (evenly distribute passes across layers).
    /// </summary>
    private static int CalculatePassesForLayer(CodeBlock block, int layer, int numLayers)
    {
        if (block.NumPasses == 0)
            return 0;

        // Simple allocation: distribute passes evenly across layers
        int passesPerLayer = (block.NumPasses + numLayers - 1) / numLayers;
        int startPass = layer * passesPerLayer;
        int endPass = Math.Min((layer + 1) * passesPerLayer, block.NumPasses);

        return Math.Max(0, endPass - startPass);
    }

    /// <summary>
    /// Writes a packet to the output stream.
    /// Note: This implementation writes headers for contributions only,
    /// and the decoder reads only the contributions present.
    /// </summary>
    public static void WritePacket(BinaryWriter writer, Packet packet, bool includeSop = false)
    {
        // Optional SOP marker
        if (includeSop)
        {
            writer.Write((byte)(Jp2kConstants.SOP >> 8));
            writer.Write((byte)(Jp2kConstants.SOP & 0xFF));
            writer.Write((ushort)4);  // Length
            writer.Write((ushort)0);  // Packet sequence number
        }

        // Packet header
        if (packet.CodeBlockContributions.Count == 0)
        {
            // Empty packet
            writer.Write((byte)0);
        }
        else
        {
            // Non-empty packet
            writer.Write((byte)1);

            // Write number of contributions so decoder knows how many to read
            writer.Write((ushort)packet.CodeBlockContributions.Count);

            // Write contribution headers
            foreach (var contrib in packet.CodeBlockContributions)
            {
                var block = contrib.CodeBlock;
                int passes = Math.Min(contrib.NewPasses, 255);
                writer.Write((byte)passes);

                if (block.EncodedData != null)
                {
                    int len = Math.Min(block.EncodedData.Length, ushort.MaxValue);
                    writer.Write((ushort)len);
                }
                else
                {
                    writer.Write((ushort)0);
                }
            }
        }

        // Packet body - code-block data
        foreach (var contrib in packet.CodeBlockContributions)
        {
            if (contrib.CodeBlock.EncodedData != null)
            {
                writer.Write(contrib.CodeBlock.EncodedData);
            }
        }
    }

    /// <summary>
    /// Reads a packet from the input stream.
    /// </summary>
    public static Packet ReadPacket(BinaryReader reader, Subband[] subbands, int resolution)
    {
        var packet = new Packet
        {
            Resolution = resolution
        };

        // Check for SOP marker
        int peek1 = reader.ReadByte();
        int peek2 = reader.ReadByte();

        if (peek1 == (Jp2kConstants.SOP >> 8) && peek2 == (Jp2kConstants.SOP & 0xFF))
        {
            // Skip SOP marker
            reader.ReadUInt16();  // Length
            reader.ReadUInt16();  // Sequence
            peek1 = reader.ReadByte();
        }
        else
        {
            // Put back the second byte by adjusting stream
            reader.BaseStream.Position -= 1;
        }

        // Packet header - empty flag
        if (peek1 == 0)
        {
            return packet;  // Empty packet
        }

        // Read number of contributions
        int numContribs = reader.ReadUInt16();

        // Build list of codeblocks at this resolution for assignment
        var codeBlocks = new List<CodeBlock>();
        foreach (var subband in subbands)
        {
            if (subband.Level != resolution)
                continue;

            if (subband.CodeBlocks == null)
                continue;

            int blocksY = subband.CodeBlocks.GetLength(0);
            int blocksX = subband.CodeBlocks.GetLength(1);

            for (int by = 0; by < blocksY; by++)
            {
                for (int bx = 0; bx < blocksX; bx++)
                {
                    codeBlocks.Add(subband.CodeBlocks[by, bx]);
                }
            }
        }

        // Read contribution headers
        for (int i = 0; i < numContribs && i < codeBlocks.Count; i++)
        {
            var codeBlock = codeBlocks[i];

            int passes = reader.ReadByte();
            int length = reader.ReadUInt16();

            if (passes > 0 && length > 0)
            {
                packet.CodeBlockContributions.Add(new CodeBlockContribution
                {
                    CodeBlock = codeBlock,
                    NewPasses = passes,
                    DataLength = length,
                    SubbandType = codeBlock.Subband.Type
                });
            }
        }

        // Read packet body
        foreach (var contrib in packet.CodeBlockContributions)
        {
            contrib.Data = reader.ReadBytes(contrib.DataLength);
        }

        return packet;
    }
}

/// <summary>
/// Represents a packet in the JPEG2000 codestream.
/// </summary>
public class Packet
{
    public int Layer { get; set; }
    public int Resolution { get; set; }
    public int Component { get; set; }
    public int Precinct { get; set; }

    public List<CodeBlockContribution> CodeBlockContributions { get; } = new();
}

/// <summary>
/// Represents a code-block's contribution to a packet.
/// </summary>
public class CodeBlockContribution
{
    public CodeBlock CodeBlock { get; set; } = null!;
    public int NewPasses { get; set; }
    public int SubbandType { get; set; }
    public int DataLength { get; set; }
    public byte[]? Data { get; set; }
}
