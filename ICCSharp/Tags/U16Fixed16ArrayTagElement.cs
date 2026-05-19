using System.Collections.Generic;
using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tags;

/// <summary>ICC.1:2010 §10.23 u16Fixed16ArrayType — array of u16Fixed16 values.</summary>
public sealed class U16Fixed16ArrayTagElement : TagElement
{
    public IReadOnlyList<double> Values { get; }

    public U16Fixed16ArrayTagElement(IReadOnlyList<double> values)
        : base(TagTypeSignatures.U16Fixed16Array)
    {
        Values = values;
    }

    internal static U16Fixed16ArrayTagElement Parse(IccBinaryReader reader, int payloadBytes)
    {
        if (payloadBytes < 0 || payloadBytes % 4 != 0)
            throw new IccParseException($"uf32 payload length {payloadBytes} is not a multiple of 4.");
        int count = payloadBytes / 4;
        double[] values = new double[count];
        for (int i = 0; i < count; i++) values[i] = reader.ReadU16Fixed16();
        return new U16Fixed16ArrayTagElement(values);
    }
}
