using System.Collections.Generic;
using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tags;

/// <summary>ICC.1:2010 §10.18 s15Fixed16ArrayType — array of s15Fixed16 values.</summary>
public sealed class S15Fixed16ArrayTagElement : TagElement
{
    public IReadOnlyList<double> Values { get; }

    public S15Fixed16ArrayTagElement(IReadOnlyList<double> values)
        : base(TagTypeSignatures.S15Fixed16Array)
    {
        Values = values;
    }

    internal static S15Fixed16ArrayTagElement Parse(IccBinaryReader reader, int payloadBytes)
    {
        if (payloadBytes < 0 || payloadBytes % 4 != 0)
            throw new IccParseException($"sf32 payload length {payloadBytes} is not a multiple of 4.");
        int count = payloadBytes / 4;
        var values = new double[count];
        for (var i = 0; i < count; i++) values[i] = reader.ReadS15Fixed16();
        return new S15Fixed16ArrayTagElement(values);
    }
}
