using System;

namespace Jp2Codec.Jp2File
{
    /// <summary>
    /// JP2 palette box (ISO/IEC 15444-1 I.5.3.4, "pclr"). Maps an indexed
    /// codestream sample value to a row of <see cref="NumColumns"/> output
    /// component values, with per-column bit depth and signedness.
    /// </summary>
    internal sealed class JpPalette
    {
        /// <summary>Number of palette rows (NE field). Each row is one entry indexed by a codestream sample.</summary>
        public int NumEntries { get; }

        /// <summary>Number of palette columns (NPC field). Each column produces one output component.</summary>
        public int NumColumns { get; }

        /// <summary>Decoded bit depth per column (1..38).</summary>
        public int[] BitDepths { get; }

        /// <summary>Whether each column's entries are interpreted as signed.</summary>
        public bool[] Signed { get; }

        /// <summary>Palette entries indexed <c>[row, column]</c>, sign-extended to <see cref="int"/>.</summary>
        public int[,] Entries { get; }

        public JpPalette(int numEntries, int numColumns, int[] bitDepths, bool[] signed, int[,] entries)
        {
            if (numEntries < 0) throw new ArgumentOutOfRangeException(nameof(numEntries), numEntries, null);
            if (numColumns < 0) throw new ArgumentOutOfRangeException(nameof(numColumns), numColumns, null);
            if (bitDepths is null) throw new ArgumentNullException(nameof(bitDepths));
            if (signed is null) throw new ArgumentNullException(nameof(signed));
            if (entries is null) throw new ArgumentNullException(nameof(entries));
            if (bitDepths.Length != numColumns)
                throw new ArgumentException($"BitDepths length {bitDepths.Length} != NumColumns {numColumns}.", nameof(bitDepths));
            if (signed.Length != numColumns)
                throw new ArgumentException($"Signed length {signed.Length} != NumColumns {numColumns}.", nameof(signed));
            if (entries.GetLength(0) != numEntries || entries.GetLength(1) != numColumns)
                throw new ArgumentException(
                    $"Entries shape ({entries.GetLength(0)}, {entries.GetLength(1)}) != ({numEntries}, {numColumns}).",
                    nameof(entries));

            NumEntries = numEntries;
            NumColumns = numColumns;
            BitDepths = bitDepths;
            Signed = signed;
            Entries = entries;
        }
    }

    /// <summary>
    /// JP2 component-mapping box (ISO/IEC 15444-1 I.5.3.5, "cmap"). Specifies
    /// how each output channel is produced from the codestream components,
    /// either by direct passthrough or by palette lookup against a
    /// <see cref="JpPalette"/>.
    /// </summary>
    internal sealed class JpComponentMapping
    {
        /// <summary>Number of output channels described by this mapping.</summary>
        public int NumChannels { get; }

        /// <summary>Codestream component index (CMP_i) feeding each output channel.</summary>
        public int[] ComponentIndex { get; }

        /// <summary>Mapping type per channel (MTYP_i): 0 = direct, 1 = palette lookup.</summary>
        public byte[] MappingType { get; }

        /// <summary>Palette column to read for each palette-mapped channel (PCOL_i; ignored when MTYP_i = 0).</summary>
        public byte[] PaletteColumn { get; }

        public JpComponentMapping(int[] componentIndex, byte[] mappingType, byte[] paletteColumn)
        {
            if (componentIndex is null) throw new ArgumentNullException(nameof(componentIndex));
            if (mappingType is null) throw new ArgumentNullException(nameof(mappingType));
            if (paletteColumn is null) throw new ArgumentNullException(nameof(paletteColumn));
            if (componentIndex.Length != mappingType.Length || mappingType.Length != paletteColumn.Length)
                throw new ArgumentException("Per-channel arrays must have identical lengths.");

            NumChannels = componentIndex.Length;
            ComponentIndex = componentIndex;
            MappingType = mappingType;
            PaletteColumn = paletteColumn;
        }
    }
}
