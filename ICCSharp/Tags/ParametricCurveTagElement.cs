using System.Collections.Generic;
using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tags;

/// <summary>
/// ICC.1:2010 §10.16 parametricCurveType ('para'). One of five predefined functions.
///
/// Function types (parameter ordering matches the spec):
///   0  y = x^g                                                                   params: g
///   1  y = (a·x + b)^g          for x ≥ -b/a; else y = 0                         params: g, a, b
///   2  y = (a·x + b)^g + c      for x ≥ -b/a; else y = c                         params: g, a, b, c
///   3  y = (a·x + b)^g          for x ≥ d;    else y = c·x                       params: g, a, b, c, d
///   4  y = (a·x + b)^g + e      for x ≥ d;    else y = c·x + f                   params: g, a, b, c, d, e, f
/// </summary>
public sealed class ParametricCurveTagElement : TagElement
{
    public ushort FunctionType { get; }
    public IReadOnlyList<double> Parameters { get; }

    public ParametricCurveTagElement(ushort functionType, IReadOnlyList<double> parameters)
        : base(TagTypeSignatures.ParametricCurve)
    {
        FunctionType = functionType;
        Parameters = parameters;
    }

    /// <summary>Required parameter count for each spec-defined function type (1, 3, 4, 5, 7).</summary>
    public static int RequiredParameterCount(ushort functionType) => functionType switch
    {
        0 => 1,
        1 => 3,
        2 => 4,
        3 => 5,
        4 => 7,
        _ => -1, // unknown
    };

    internal static ParametricCurveTagElement Parse(IccBinaryReader reader, int payloadBytes)
    {
        if (payloadBytes < 4)
            throw new IccParseException($"parametricCurve payload {payloadBytes} bytes; need at least 4.");

        ushort functionType = reader.ReadUInt16();
        reader.Skip(2); // reserved

        int required = RequiredParameterCount(functionType);
        if (required < 0)
            throw new IccParseException($"parametricCurve function type {functionType} not defined by spec.");

        int paramBytes = required * 4;
        if (paramBytes > payloadBytes - 4)
            throw new IccParseException(
                $"parametricCurve type {functionType} needs {paramBytes} parameter bytes but only {payloadBytes - 4} remain.");

        var parameters = new double[required];
        for (var i = 0; i < required; i++) parameters[i] = reader.ReadS15Fixed16();
        return new ParametricCurveTagElement(functionType, parameters);
    }
}
