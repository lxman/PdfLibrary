using System;
using ICCSharp.IO;

namespace ICCSharp.Tags;

/// <summary>
/// Fallback for tag element types the parser does not recognize. The raw payload
/// (excluding the 8-byte type header) is preserved so callers can still inspect bytes.
/// </summary>
public sealed class UnknownTagElement : TagElement
{
    public ReadOnlyMemory<byte> Payload { get; }

    public UnknownTagElement(IccSignature typeSignature, ReadOnlyMemory<byte> payload)
        : base(typeSignature)
    {
        Payload = payload;
    }
}
