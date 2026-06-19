using System;
using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tags;

/// <summary>
/// ICC.1:2010 §10.10 lut8Type ('mft1'). Fixed 256-entry input and output tables.
///
/// Layout after the 8-byte type header:
///   uint8  inputChannels  (i)
///   uint8  outputChannels (o)
///   uint8  clutGridPoints (g, same in every dimension)
///   uint8  reserved
///   s15Fixed16 × 9        // 3x3 matrix (only meaningful when PCS = XYZ)
///   uint8 × 256 × i       // input tables
///   uint8 × g^i × o       // CLUT
///   uint8 × 256 × o       // output tables
/// </summary>
public sealed class Lut8TagElement : TagElement
{
    public int InputChannels { get; }
    public int OutputChannels { get; }
    public int ClutGridPoints { get; }

    /// <summary>3×3 matrix in row-major order (e11..e33). Identity unless PCS = XYZ.</summary>
    public double[] Matrix { get; }

    /// <summary>InputTables[c][i] — table for input channel c, 256 entries.</summary>
    public byte[][] InputTables { get; }

    /// <summary>
    /// Flat CLUT of length g^i × o. The first input channel varies slowest; for each grid point
    /// the o output values are stored consecutively.
    /// </summary>
    public byte[] Clut { get; }

    /// <summary>OutputTables[c][i] — table for output channel c, 256 entries.</summary>
    public byte[][] OutputTables { get; }

    public Lut8TagElement(
        int inputChannels, int outputChannels, int clutGridPoints,
        double[] matrix, byte[][] inputTables, byte[] clut, byte[][] outputTables)
        : base(TagTypeSignatures.Lut8)
    {
        InputChannels = inputChannels;
        OutputChannels = outputChannels;
        ClutGridPoints = clutGridPoints;
        Matrix = matrix;
        InputTables = inputTables;
        Clut = clut;
        OutputTables = outputTables;
    }

    internal static Lut8TagElement Parse(IccBinaryReader reader, int payloadBytes)
    {
        // Fixed header portion: 4 channel-count bytes + 36 matrix bytes = 40 bytes.
        if (payloadBytes < 40)
            throw new IccParseException($"mft1 payload {payloadBytes} bytes; need at least 40 for fixed header.");

        int inputChannels = reader.ReadUInt8();
        int outputChannels = reader.ReadUInt8();
        int clutGridPoints = reader.ReadUInt8();
        reader.Skip(1); // reserved

        var matrix = new double[9];
        for (var i = 0; i < 9; i++) matrix[i] = reader.ReadS15Fixed16();

        long inputTableBytes = 256L * inputChannels;
        long clutEntries = LongPow(clutGridPoints, inputChannels);
        long clutBytes = clutEntries * outputChannels;
        long outputTableBytes = 256L * outputChannels;

        long needed = 40 + inputTableBytes + clutBytes + outputTableBytes;
        if (needed > payloadBytes)
            throw new IccParseException(
                $"mft1 declares i={inputChannels} o={outputChannels} g={clutGridPoints}; needs {needed} bytes but payload is {payloadBytes}.");
        if (clutEntries > int.MaxValue / Math.Max(1, outputChannels))
            throw new IccParseException($"mft1 CLUT size {clutEntries}×{outputChannels} exceeds int.MaxValue.");

        var inputTables = new byte[inputChannels][];
        for (var c = 0; c < inputChannels; c++)
        {
            inputTables[c] = reader.ReadBytes(256).ToArray();
        }

        byte[] clut = reader.ReadBytes((int)clutBytes).ToArray();

        var outputTables = new byte[outputChannels][];
        for (var c = 0; c < outputChannels; c++)
        {
            outputTables[c] = reader.ReadBytes(256).ToArray();
        }

        return new Lut8TagElement(inputChannels, outputChannels, clutGridPoints, matrix,
            inputTables, clut, outputTables);
    }

    internal static long LongPow(long b, int e)
    {
        long result = 1;
        for (var i = 0; i < e; i++) result *= b;
        return result;
    }
}
