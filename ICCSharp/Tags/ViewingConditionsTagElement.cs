using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tags;

/// <summary>ICC.1:2010 §10.26 viewingConditionsType ('view').</summary>
public sealed class ViewingConditionsTagElement : TagElement
{
    public XyzNumber Illuminant { get; }
    public XyzNumber Surround { get; }
    public uint IlluminantType { get; } // same enum as measurementType StandardIlluminant

    public ViewingConditionsTagElement(XyzNumber illuminant, XyzNumber surround, uint type)
        : base(TagTypeSignatures.ViewingConditions)
    {
        Illuminant = illuminant;
        Surround = surround;
        IlluminantType = type;
    }

    internal static ViewingConditionsTagElement Parse(IccBinaryReader reader, int payloadBytes)
    {
        if (payloadBytes < 28)
            throw new IccParseException($"viewingConditionsType payload {payloadBytes} bytes; need 28.");
        XyzNumber illum = reader.ReadXyz();
        XyzNumber surr = reader.ReadXyz();
        uint type = reader.ReadUInt32();
        return new ViewingConditionsTagElement(illum, surr, type);
    }
}
