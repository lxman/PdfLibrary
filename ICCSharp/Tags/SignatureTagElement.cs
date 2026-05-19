using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tags;

/// <summary>
/// ICC.1:2010 §10.20 signatureType — one four-byte signature value.
/// Used for technology ('tech'), perceptual rendering intent gamut ('rig0'), etc.
/// </summary>
public sealed class SignatureTagElement : TagElement
{
    public IccSignature Value { get; }

    public SignatureTagElement(IccSignature value) : base(TagTypeSignatures.SignatureType)
    {
        Value = value;
    }

    internal static SignatureTagElement Parse(IccBinaryReader reader, int payloadBytes)
    {
        if (payloadBytes < 4)
            throw new IccParseException($"signatureType payload requires 4 bytes, got {payloadBytes}.");
        IccSignature sig = reader.ReadSignature();
        // Spec allows exactly one signature; any trailing bytes are non-conformant but tolerated.
        return new SignatureTagElement(sig);
    }
}
