using System;
using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tags;

/// <summary>
/// Reads a single tag's data slice into a strongly-typed <see cref="TagElement"/>.
/// Dispatches on the 4-byte type signature at the head of the slice; unknown types
/// fall through to <see cref="UnknownTagElement"/>.
/// </summary>
public static class TagElementReader
{
    /// <summary>
    /// Parses a tag whose raw bytes are <paramref name="tagData"/>. The slice MUST
    /// start with the 8-byte type header (type signature + 4 reserved bytes).
    /// </summary>
    public static TagElement Parse(ReadOnlyMemory<byte> tagData)
    {
        if (tagData.Length < 8)
            throw new IccParseException($"Tag data is {tagData.Length} bytes; need at least 8 for the type header.");

        IccBinaryReader reader = new(tagData);
        IccSignature typeSig = reader.ReadSignature();
        reader.Skip(4); // reserved
        int payloadBytes = tagData.Length - 8;

        if (typeSig == TagTypeSignatures.Xyz)
            return XyzTagElement.Parse(reader, payloadBytes);
        if (typeSig == TagTypeSignatures.S15Fixed16Array)
            return S15Fixed16ArrayTagElement.Parse(reader, payloadBytes);
        if (typeSig == TagTypeSignatures.U16Fixed16Array)
            return U16Fixed16ArrayTagElement.Parse(reader, payloadBytes);
        if (typeSig == TagTypeSignatures.SignatureType)
            return SignatureTagElement.Parse(reader, payloadBytes);
        if (typeSig == TagTypeSignatures.DateTimeType)
            return DateTimeTagElement.Parse(reader, payloadBytes);
        if (typeSig == TagTypeSignatures.Text)
            return TextTagElement.Parse(reader, payloadBytes);
        if (typeSig == TagTypeSignatures.TextDescription)
            return TextDescriptionTagElement.Parse(reader, payloadBytes);
        if (typeSig == TagTypeSignatures.MultiLocalizedUnicode)
            return MultiLocalizedUnicodeTagElement.Parse(reader, payloadBytes, tagData);
        if (typeSig == TagTypeSignatures.Curve)
            return CurveTagElement.Parse(reader, payloadBytes);
        if (typeSig == TagTypeSignatures.ParametricCurve)
            return ParametricCurveTagElement.Parse(reader, payloadBytes);
        if (typeSig == TagTypeSignatures.Lut8)
            return Lut8TagElement.Parse(reader, payloadBytes);
        if (typeSig == TagTypeSignatures.Lut16)
            return Lut16TagElement.Parse(reader, payloadBytes);
        if (typeSig == TagTypeSignatures.LutAToB)
            return LutAToBTagElement.Parse(tagData);
        if (typeSig == TagTypeSignatures.LutBToA)
            return LutBToATagElement.Parse(tagData);
        if (typeSig == TagTypeSignatures.Chromaticity)
            return ChromaticityTagElement.Parse(reader, payloadBytes);
        if (typeSig == TagTypeSignatures.Cicp)
            return CicpTagElement.Parse(reader, payloadBytes);
        if (typeSig == TagTypeSignatures.Measurement)
            return MeasurementTagElement.Parse(reader, payloadBytes);
        if (typeSig == TagTypeSignatures.ViewingConditions)
            return ViewingConditionsTagElement.Parse(reader, payloadBytes);
        if (typeSig == TagTypeSignatures.ColorantTable)
            return ColorantTableTagElement.Parse(reader, payloadBytes);
        if (typeSig == TagTypeSignatures.ColorantOrder)
            return ColorantOrderTagElement.Parse(reader, payloadBytes);
        if (typeSig == TagTypeSignatures.NamedColor2)
            return NamedColor2TagElement.Parse(reader, payloadBytes);

        return new UnknownTagElement(typeSig, tagData.Slice(8));
    }
}
