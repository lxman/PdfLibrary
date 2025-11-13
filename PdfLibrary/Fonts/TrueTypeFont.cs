using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Fonts;

/// <summary>
/// Represents a TrueType font (ISO 32000-1:2008 section 9.6.3)
/// </summary>
public class TrueTypeFont : PdfFont
{
    private double[]? _widths;
    private double _defaultWidth;

    public TrueTypeFont(PdfDictionary dictionary, PdfDocument? document = null)
        : base(dictionary, document)
    {
        LoadEncoding();
        LoadToUnicodeCMap();
        LoadWidths();
    }

    public override PdfFontType FontType => PdfFontType.TrueType;

    public override double GetCharacterWidth(int charCode)
    {
        // Check if character code is in range
        if (_widths != null && charCode >= FirstChar && charCode <= LastChar)
        {
            int index = charCode - FirstChar;
            if (index >= 0 && index < _widths.Length)
                return _widths[index];
        }

        // Try to get from font descriptor
        PdfFontDescriptor? descriptor = GetDescriptor();
        if (descriptor == null) return _defaultWidth > 0
            ? _defaultWidth
            : 500;
        if (descriptor.MissingWidth > 0)
            return descriptor.MissingWidth;
        if (descriptor.AvgWidth > 0)
            return descriptor.AvgWidth;

        // Last resort: use default width
        return _defaultWidth > 0 ? _defaultWidth : 500;
    }

    private void LoadEncoding()
    {
        if (!_dictionary.TryGetValue(new PdfName("Encoding"), out PdfObject? obj))
        {
            // TrueType fonts without encoding use WinAnsiEncoding by default
            Encoding = PdfFontEncoding.GetStandardEncoding("WinAnsiEncoding");
            return;
        }

        // Resolve indirect reference
        if (obj is PdfIndirectReference reference && _document != null)
            obj = _document.ResolveReference(reference);

        Encoding = obj switch
        {
            // Named encoding
            PdfName encodingName => PdfFontEncoding.GetStandardEncoding(encodingName.Value),
            // Custom encoding dictionary
            PdfDictionary encodingDict => PdfFontEncoding.FromDictionary(encodingDict,
                PdfFontEncoding.GetStandardEncoding("WinAnsiEncoding")),
            _ => Encoding
        };
    }

    private void LoadWidths()
    {
        // Get widths array
        if (_dictionary.TryGetValue(new PdfName("Widths"), out PdfObject? obj))
        {
            if (obj is PdfIndirectReference reference && _document != null)
                obj = _document.ResolveReference(reference);

            if (obj is PdfArray widthsArray)
            {
                _widths = new double[widthsArray.Count];
                for (var i = 0; i < widthsArray.Count; i++)
                {
                    _widths[i] = GetNumber(widthsArray[i]);
                }
            }
        }

        // Get default width from font descriptor
        PdfFontDescriptor? descriptor = GetDescriptor();
        if (descriptor != null)
        {
            _defaultWidth = descriptor.MissingWidth > 0 ? descriptor.MissingWidth : descriptor.AvgWidth;
        }
    }

    private static double GetNumber(PdfObject obj)
    {
        return obj switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            _ => 0
        };
    }
}
