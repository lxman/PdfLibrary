using System.Collections.Generic;
using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tags;

/// <summary>
/// ICC.1:2010 §10.2 chromaticityType ('chrm'). Carries chromaticity (xy) coordinates per device channel,
/// plus a phosphor/colorant identifier.
/// </summary>
public sealed class ChromaticityTagElement : TagElement
{
    public int DeviceChannels { get; }
    public ushort PhosphorOrColorantType { get; }
    public IReadOnlyList<(double X, double Y)> Coordinates { get; }

    public ChromaticityTagElement(int deviceChannels, ushort phosphorType, IReadOnlyList<(double, double)> coords)
        : base(TagTypeSignatures.Chromaticity)
    {
        DeviceChannels = deviceChannels;
        PhosphorOrColorantType = phosphorType;
        Coordinates = coords;
    }

    internal static ChromaticityTagElement Parse(IccBinaryReader reader, int payloadBytes)
    {
        if (payloadBytes < 4)
            throw new IccParseException($"chromaticityType payload {payloadBytes} bytes; need at least 4.");

        int channels = reader.ReadUInt16();
        ushort phosphor = reader.ReadUInt16();

        long needed = (long)channels * 8;
        if (needed > payloadBytes - 4)
            throw new IccParseException(
                $"chromaticityType needs {needed} body bytes for {channels} channels; only {payloadBytes - 4} remain.");

        (double, double)[] coords = new (double, double)[channels];
        for (int i = 0; i < channels; i++)
        {
            double x = reader.ReadU16Fixed16();
            double y = reader.ReadU16Fixed16();
            coords[i] = (x, y);
        }
        return new ChromaticityTagElement(channels, phosphor, coords);
    }
}
