using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tags;

/// <summary>
/// ICC.1:2010 §10.11 lut16Type ('mft2'). Variable-size input and output tables (n, m entries
/// of uint16 each), 16-bit CLUT.
///
/// Layout after the 8-byte type header:
///   uint8  inputChannels  (i)
///   uint8  outputChannels (o)
///   uint8  clutGridPoints (g)
///   uint8  reserved
///   s15Fixed16 × 9        // 3x3 matrix (PCS = XYZ only)
///   uint16 inputTableEntries  (n, ≥ 2)
///   uint16 outputTableEntries (m, ≥ 2)
///   uint16 × n × i        // input tables
///   uint16 × g^i × o      // CLUT
///   uint16 × m × o        // output tables
/// </summary>
public sealed class Lut16TagElement : TagElement
{
    public int InputChannels { get; }
    public int OutputChannels { get; }
    public int ClutGridPoints { get; }

    /// <summary>3×3 matrix in row-major order.</summary>
    public double[] Matrix { get; }

    public int InputTableEntries { get; }
    public int OutputTableEntries { get; }

    public ushort[][] InputTables { get; }
    public ushort[] Clut { get; }
    public ushort[][] OutputTables { get; }

    public Lut16TagElement(
        int inputChannels, int outputChannels, int clutGridPoints,
        double[] matrix,
        int inputTableEntries, int outputTableEntries,
        ushort[][] inputTables, ushort[] clut, ushort[][] outputTables)
        : base(TagTypeSignatures.Lut16)
    {
        InputChannels = inputChannels;
        OutputChannels = outputChannels;
        ClutGridPoints = clutGridPoints;
        Matrix = matrix;
        InputTableEntries = inputTableEntries;
        OutputTableEntries = outputTableEntries;
        InputTables = inputTables;
        Clut = clut;
        OutputTables = outputTables;
    }

    internal static Lut16TagElement Parse(IccBinaryReader reader, int payloadBytes)
    {
        if (payloadBytes < 44)
            throw new IccParseException($"mft2 payload {payloadBytes} bytes; need at least 44 for fixed header.");

        int inputChannels = reader.ReadUInt8();
        int outputChannels = reader.ReadUInt8();
        int clutGridPoints = reader.ReadUInt8();
        reader.Skip(1);

        var matrix = new double[9];
        for (var i = 0; i < 9; i++) matrix[i] = reader.ReadS15Fixed16();

        int n = reader.ReadUInt16();
        int m = reader.ReadUInt16();
        if (n < 2 || m < 2)
            throw new IccParseException($"mft2 requires n≥2 and m≥2; got n={n}, m={m}.");

        long inputTableBytes = 2L * n * inputChannels;
        long clutEntries = Lut8TagElement.LongPow(clutGridPoints, inputChannels);
        long clutBytes = 2L * clutEntries * outputChannels;
        long outputTableBytes = 2L * m * outputChannels;

        long needed = 44 + inputTableBytes + clutBytes + outputTableBytes;
        if (needed > payloadBytes)
            throw new IccParseException(
                $"mft2 declares i={inputChannels} o={outputChannels} g={clutGridPoints} n={n} m={m}; needs {needed} bytes but payload is {payloadBytes}.");

        var inputTables = new ushort[inputChannels][];
        for (var c = 0; c < inputChannels; c++)
        {
            var table = new ushort[n];
            for (var i = 0; i < n; i++) table[i] = reader.ReadUInt16();
            inputTables[c] = table;
        }

        long totalClutValues = clutEntries * outputChannels;
        if (totalClutValues > int.MaxValue)
            throw new IccParseException($"mft2 CLUT size {totalClutValues} exceeds int.MaxValue.");
        var clut = new ushort[(int)totalClutValues];
        for (var i = 0; i < clut.Length; i++) clut[i] = reader.ReadUInt16();

        var outputTables = new ushort[outputChannels][];
        for (var c = 0; c < outputChannels; c++)
        {
            var table = new ushort[m];
            for (var i = 0; i < m; i++) table[i] = reader.ReadUInt16();
            outputTables[c] = table;
        }

        return new Lut16TagElement(inputChannels, outputChannels, clutGridPoints, matrix,
            n, m, inputTables, clut, outputTables);
    }
}
