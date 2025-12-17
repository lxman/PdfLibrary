using System;
using System.Collections.Generic;

namespace ImageLibrary.Jbig2;

/// <summary>
/// Represents a JBIG2 symbol dictionary containing glyph bitmaps.
/// T.88 Section 6.5 and 7.4.2.
/// </summary>
internal sealed class SymbolDictionary
{
    private readonly List<Bitmap> _symbols = [];

    /// <summary>
    /// Number of symbols in this dictionary.
    /// </summary>
    public int Count => _symbols.Count;

    /// <summary>
    /// Gets a symbol by index.
    /// </summary>
    public Bitmap this[int index] => _symbols[index];

    /// <summary>
    /// Gets a symbol by index, or null if out of range.
    /// </summary>
    public Bitmap? GetSymbol(int index)
    {
        if (index < 0 || index >= _symbols.Count)
            return null;
        return _symbols[index];
    }

    /// <summary>
    /// Adds a symbol to the dictionary.
    /// </summary>
    public void Add(Bitmap symbol)
    {
        _symbols.Add(symbol ?? throw new ArgumentNullException(nameof(symbol)));
    }

    /// <summary>
    /// Concatenates multiple symbol dictionaries into a new one.
    /// </summary>
    public static SymbolDictionary Concatenate(params SymbolDictionary[] dictionaries)
    {
        var result = new SymbolDictionary();
        foreach (var dict in dictionaries)
        {
            if (dict != null)
            {
                for (var i = 0; i < dict.Count; i++)
                    result.Add(dict[i]);
            }
        }
        return result;
    }

    /// <summary>
    /// Creates a symbol dictionary from exported symbols.
    /// </summary>
    public static SymbolDictionary FromExported(SymbolDictionary source, bool[] exportFlags)
    {
        var result = new SymbolDictionary();
        for (var i = 0; i < Math.Min(source.Count, exportFlags.Length); i++)
        {
            if (exportFlags[i])
                result.Add(source[i]);
        }
        return result;
    }
}

/// <summary>
/// Parameters for symbol dictionary decoding.
/// T.88 Section 7.4.2.1.
/// </summary>
internal sealed class SymbolDictionaryParams
{
    /// <summary>
    /// Use Huffman coding (true) or arithmetic coding (false).
    /// </summary>
    public bool UseHuffman { get; set; }

    /// <summary>
    /// Use refinement/aggregate coding.
    /// </summary>
    public bool UseRefinementAgg { get; set; }

    /// <summary>
    /// Number of symbols from referred-to dictionaries.
    /// </summary>
    public int NumInputSymbols { get; set; }

    /// <summary>
    /// Number of new symbols to decode.
    /// </summary>
    public int NumNewSymbols { get; set; }

    /// <summary>
    /// Number of symbols to export.
    /// </summary>
    public int NumExportedSymbols { get; set; }

    /// <summary>
    /// Generic region template (0-3).
    /// </summary>
    public int Template { get; set; }

    /// <summary>
    /// Refinement template (0 or 1).
    /// </summary>
    public int RefinementTemplate { get; set; }

    /// <summary>
    /// Adaptive template pixels for generic region.
    /// </summary>
    public (int dx, int dy)[] AdaptivePixels { get; set; } = [];

    /// <summary>
    /// Adaptive template pixels for refinement.
    /// </summary>
    public (int dx, int dy)[] RefinementAdaptivePixels { get; set; } = [];

    // Huffman table selections (7.4.2.1.1)
    // Values 0-2 select standard tables, 3 = custom table

    /// <summary>Huffman table selection for DH (delta height). 0=B.4, 1=B.5, 3=custom</summary>
    public int HuffmanDH { get; set; }

    /// <summary>Huffman table selection for DW (delta width). 0=B.2, 1=B.3, 3=custom</summary>
    public int HuffmanDW { get; set; }

    /// <summary>Huffman table selection for BMSIZE. 0=B.1, 1=custom</summary>
    public int HuffmanBMSIZE { get; set; }

    /// <summary>Huffman table selection for REFAGGNINST. 0=B.1, 1=custom</summary>
    public int HuffmanAGGINST { get; set; }
}
