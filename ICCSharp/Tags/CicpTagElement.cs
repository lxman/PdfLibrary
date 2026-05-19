using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tags;

/// <summary>
/// ICC v4.4 cicpType ('cicp'). Encodes ITU-T H.273 / ISO/IEC 23091-2 code points used by HDR /
/// wide-gamut video signalling. Four uInt8 fields after the 8-byte type header.
/// </summary>
public sealed class CicpTagElement : TagElement
{
    public byte ColourPrimaries { get; }
    public byte TransferCharacteristics { get; }
    public byte MatrixCoefficients { get; }
    public byte VideoFullRangeFlag { get; }

    public CicpTagElement(byte primaries, byte transfer, byte matrix, byte fullRange)
        : base(TagTypeSignatures.Cicp)
    {
        ColourPrimaries = primaries;
        TransferCharacteristics = transfer;
        MatrixCoefficients = matrix;
        VideoFullRangeFlag = fullRange;
    }

    internal static CicpTagElement Parse(IccBinaryReader reader, int payloadBytes)
    {
        if (payloadBytes < 4)
            throw new IccParseException($"cicpType payload {payloadBytes} bytes; need 4.");
        return new CicpTagElement(
            reader.ReadUInt8(), reader.ReadUInt8(), reader.ReadUInt8(), reader.ReadUInt8());
    }
}
