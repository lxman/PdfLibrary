using System;

namespace ImageLibrary.Jbig2;

/// <summary>
/// Represents a line in a Huffman table definition (T.88 Annex B).
/// Each line defines how a range of values maps to a prefix code.
/// </summary>
public readonly struct HuffmanLine
{
    /// <summary>
    /// Prefix code length in bits.
    /// A value of 0 indicates a low/high extension line.
    /// </summary>
    public readonly int PrefixLength;

    /// <summary>
    /// Number of additional bits to read for the range offset.
    /// Value of 32 indicates an unbounded (low or high) range.
    /// </summary>
    public readonly int RangeLength;

    /// <summary>
    /// Base value for this range. The final decoded value is
    /// RangeLow + offset (or RangeLow - offset for low ranges).
    /// </summary>
    public readonly int RangeLow;

    /// <summary>
    /// Initializes a new instance of the <see cref="HuffmanLine"/> struct.
    /// </summary>
    /// <param name="prefixLength">Prefix code length in bits.</param>
    /// <param name="rangeLength">Number of additional bits for range offset.</param>
    /// <param name="rangeLow">Base value for the range.</param>
    public HuffmanLine(int prefixLength, int rangeLength, int rangeLow)
    {
        PrefixLength = prefixLength;
        RangeLength = rangeLength;
        RangeLow = rangeLow;
    }
}

/// <summary>
/// Parameters for building a Huffman table.
/// </summary>
internal sealed class HuffmanParams
{
    /// <summary>
    /// Whether this table has an out-of-band (OOB) value.
    /// The OOB is the last line in the table.
    /// </summary>
    public bool HasOOB { get; }

    /// <summary>
    /// The lines defining the Huffman table.
    /// </summary>
    public HuffmanLine[] Lines { get; }

    public HuffmanParams(bool hasOOB, HuffmanLine[] lines)
    {
        HasOOB = hasOOB;
        Lines = lines;
    }
}

/// <summary>
/// Flags for Huffman table entries.
/// </summary>
[Flags]
internal enum HuffmanFlags : byte
{
    None = 0,
    /// <summary>This entry represents the out-of-band value.</summary>
    IsOOB = 1,
    /// <summary>This entry is a "low" range (subtract offset from RangeLow).</summary>
    IsLow = 2,
    /// <summary>This entry points to an extension table.</summary>
    IsExtension = 4
}

/// <summary>
/// A single entry in the compiled Huffman lookup table.
/// </summary>
internal struct HuffmanEntry
{
    /// <summary>
    /// The decoded value (or base value if RangeLen > 0).
    /// </summary>
    public int RangeLow;

    /// <summary>
    /// Total number of bits consumed (prefix + range bits already included).
    /// </summary>
    public byte PrefixLength;

    /// <summary>
    /// Number of additional bits to read (0 if fully decoded).
    /// </summary>
    public byte RangeLength;

    /// <summary>
    /// Entry flags (OOB, Low, Extension).
    /// </summary>
    public HuffmanFlags Flags;
}

/// <summary>
/// A compiled Huffman table for fast lookup-based decoding.
/// T.88 Annex B.3 describes the table building algorithm.
/// </summary>
internal sealed class HuffmanTable
{
    private const int MaxLogTableSize = 16;

    /// <summary>
    /// Log2 of the table size. Table has 2^LogTableSize entries.
    /// </summary>
    public int LogTableSize { get; }

    /// <summary>
    /// The lookup table entries, indexed by the first LogTableSize bits.
    /// </summary>
    public HuffmanEntry[] Entries { get; }

    private HuffmanTable(int logTableSize, HuffmanEntry[] entries)
    {
        LogTableSize = logTableSize;
        Entries = entries;
    }

    /// <summary>
    /// Build a Huffman table from parameters (T.88 Annex B.3).
    /// </summary>
    public static HuffmanTable Build(HuffmanParams parameters)
    {
        HuffmanLine[] lines = parameters.Lines;
        int nLines = lines.Length;

        // B.3 step 1: Find LENMAX and count codes per length
        var lenMax = 0;
        var lenCount = new int[257]; // Index 0-256
        var logTableSize = 0;

        for (var i = 0; i < nLines; i++)
        {
            int prefLen = lines[i].PrefixLength;
            if (prefLen > lenMax)
                lenMax = prefLen;
            lenCount[prefLen]++;

            // Determine table size - use prefix + range if it fits
            int lts = prefLen + lines[i].RangeLength;
            if (lts > MaxLogTableSize)
                lts = prefLen;
            if (lts <= MaxLogTableSize && logTableSize < lts)
                logTableSize = lts;
        }

        // Allocate entries table
        int tableSize = 1 << logTableSize;
        var entries = new HuffmanEntry[tableSize];

        // Initialize entries with invalid marker
        for (var i = 0; i < tableSize; i++)
        {
            entries[i].PrefixLength = 0xFF;
            entries[i].RangeLength = 0xFF;
            entries[i].Flags = (HuffmanFlags)0xFF;
        }

        lenCount[0] = 0;

        // B.3 step 3: Assign codes
        var firstCode = 0;
        for (var curLen = 1; curLen <= lenMax; curLen++)
        {
            int shift = logTableSize - curLen;

            // B.3 3.(a)
            firstCode = (firstCode + lenCount[curLen - 1]) << 1;
            int curCode = firstCode;

            // B.3 3.(b)
            for (var curTemp = 0; curTemp < nLines; curTemp++)
            {
                int prefLen = lines[curTemp].PrefixLength;
                if (prefLen == curLen)
                {
                    int rangeLen = lines[curTemp].RangeLength;
                    int startJ = curCode << shift;
                    int endJ = (curCode + 1) << shift;

                    if (endJ > tableSize)
                        throw new Jbig2DataException($"Huffman table overflow: {endJ} > {tableSize}");

                    // Determine flags
                    var eflags = HuffmanFlags.None;
                    if (parameters.HasOOB && curTemp == nLines - 1)
                        eflags |= HuffmanFlags.IsOOB;
                    if (curTemp == nLines - (parameters.HasOOB ? 3 : 2))
                        eflags |= HuffmanFlags.IsLow;

                    if (prefLen + rangeLen > MaxLogTableSize)
                    {
                        // Can't fully expand - store base entry
                        for (int j = startJ; j < endJ; j++)
                        {
                            entries[j].RangeLow = lines[curTemp].RangeLow;
                            entries[j].PrefixLength = (byte)prefLen;
                            entries[j].RangeLength = (byte)rangeLen;
                            entries[j].Flags = eflags;
                        }
                    }
                    else
                    {
                        // Fully expand the range into the table
                        for (int j = startJ; j < endJ; j++)
                        {
                            int htOffset = (j >> (shift - rangeLen)) & ((1 << rangeLen) - 1);

                            if ((eflags & HuffmanFlags.IsLow) != 0)
                                entries[j].RangeLow = lines[curTemp].RangeLow - htOffset;
                            else
                                entries[j].RangeLow = lines[curTemp].RangeLow + htOffset;

                            entries[j].PrefixLength = (byte)(prefLen + rangeLen);
                            entries[j].RangeLength = 0;
                            entries[j].Flags = eflags;
                        }
                    }

                    curCode++;
                }
            }
        }

        return new HuffmanTable(logTableSize, entries);
    }
}
