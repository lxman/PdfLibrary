using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tags;

/// <summary>ICC.1:2010 §10.12 measurementType ('meas').</summary>
public sealed class MeasurementTagElement : TagElement
{
    public uint StandardObserver { get; }    // 0=unknown, 1=CIE 1931, 2=CIE 1964
    public XyzNumber BackingMeasurement { get; }
    public uint MeasurementGeometry { get; } // 0=unknown, 1=0°/45° or 45°/0°, 2=0°/d or d/0°
    public double MeasurementFlare { get; }
    public uint StandardIlluminant { get; }  // 0..8 per spec

    public MeasurementTagElement(uint observer, XyzNumber backing, uint geometry, double flare, uint illuminant)
        : base(TagTypeSignatures.Measurement)
    {
        StandardObserver = observer;
        BackingMeasurement = backing;
        MeasurementGeometry = geometry;
        MeasurementFlare = flare;
        StandardIlluminant = illuminant;
    }

    internal static MeasurementTagElement Parse(IccBinaryReader reader, int payloadBytes)
    {
        if (payloadBytes < 28)
            throw new IccParseException($"measurementType payload {payloadBytes} bytes; need 28.");
        uint observer = reader.ReadUInt32();
        XyzNumber backing = reader.ReadXyz();
        uint geometry = reader.ReadUInt32();
        double flare = reader.ReadU16Fixed16();
        uint illuminant = reader.ReadUInt32();
        return new MeasurementTagElement(observer, backing, geometry, flare, illuminant);
    }
}
