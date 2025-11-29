using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Fonts;

/// <summary>
/// Represents a Type 3 (user-defined) font (ISO 32000-1:2008 section 9.6.5)
/// Type 3 fonts are defined by PDF content streams
/// </summary>
internal class Type3Font : PdfFont
{
    private double[]? _widths;
    private double _defaultWidth;

    public Type3Font(PdfDictionary dictionary, PdfDocument? document = null)
        : base(dictionary, document)
    {
        LoadEncoding();
        LoadToUnicodeCMap();
        LoadWidths();
    }

    internal override PdfFontType FontType => PdfFontType.Type3;

    /// <summary>
    /// Gets the font matrix (transforms from glyph space to text space)
    /// </summary>
    public double[] FontMatrix
    {
        get
        {
            if (_dictionary.TryGetValue(new PdfName("FontMatrix"), out PdfObject obj) && obj is PdfArray { Count: 6 } array)
            {
                return
                [
                    array[0].ToDouble(),
                    array[1].ToDouble(),
                    array[2].ToDouble(),
                    array[3].ToDouble(),
                    array[4].ToDouble(),
                    array[5].ToDouble()
                ];
            }
            return [0.001, 0, 0, 0.001, 0, 0]; // Default font matrix
        }
    }

    /// <summary>
    /// Gets the character procedures dictionary
    /// </summary>
    public PdfDictionary? GetCharProcs()
    {
        if (!_dictionary.TryGetValue(new PdfName("CharProcs"), out PdfObject? obj)) return null;
        if (obj is PdfIndirectReference reference && _document is not null)
            obj = _document.ResolveReference(reference);

        return obj as PdfDictionary;
    }

    public override double GetCharacterWidth(int charCode)
    {
        // Check if character code is in range
        if (_widths is null || charCode < FirstChar || charCode > LastChar)
            return _defaultWidth > 0 ? _defaultWidth : 500;
        int index = charCode - FirstChar;
        if (index >= 0 && index < _widths.Length)
            return _widths[index];

        return _defaultWidth > 0 ? _defaultWidth : 500;
    }

    private void LoadEncoding()
    {
        if (!_dictionary.TryGetValue(new PdfName("Encoding"), out PdfObject? obj))
        {
            Encoding = PdfFontEncoding.GetStandardEncoding("StandardEncoding");
            return;
        }

        // Resolve indirect reference
        if (obj is PdfIndirectReference reference && _document is not null)
            obj = _document.ResolveReference(reference);

        Encoding = obj switch
        {
            // Named encoding
            PdfName encodingName => PdfFontEncoding.GetStandardEncoding(encodingName.Value),
            // Custom encoding dictionary
            PdfDictionary encodingDict => PdfFontEncoding.FromDictionary(encodingDict,
                PdfFontEncoding.GetStandardEncoding("StandardEncoding")),
            _ => Encoding
        };
    }

    private void LoadWidths()
    {
        // Get widths array
        if (_dictionary.TryGetValue(new PdfName("Widths"), out PdfObject? obj))
        {
            if (obj is PdfIndirectReference reference && _document is not null)
                obj = _document.ResolveReference(reference);

            if (obj is PdfArray widthsArray)
            {
                _widths = new double[widthsArray.Count];
                for (var i = 0; i < widthsArray.Count; i++)
                {
                    _widths[i] = widthsArray[i].ToDouble();
                }
            }
        }

        // Calculate default width as average
        if (_widths is { Length: > 0 })
        {
            _defaultWidth = _widths.Average();
        }
        else
        {
            _defaultWidth = 500;
        }
    }

}
