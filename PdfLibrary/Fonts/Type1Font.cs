using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Fonts;

/// <summary>
/// Represents a Type 1 (PostScript) font (ISO 32000-1:2008 section 9.6.2)
/// </summary>
public class Type1Font : PdfFont
{
    private double[]? _widths;
    private double _defaultWidth;

    public Type1Font(PdfDictionary dictionary, PdfDocument? document = null)
        : base(dictionary, document)
    {
        LoadEncoding();
        LoadToUnicodeCMap();
        LoadWidths();
    }

    public override PdfFontType FontType => PdfFontType.Type1;

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
            : 250; // 250 is PDF default
        if (descriptor.MissingWidth > 0)
            return descriptor.MissingWidth;
        if (descriptor.AvgWidth > 0)
            return descriptor.AvgWidth;

        // Last resort: use default width
        return _defaultWidth > 0
            ? _defaultWidth
            : 250; // 250 is PDF default
    }

    private void LoadEncoding()
    {
        if (!_dictionary.TryGetValue(new PdfName("Encoding"), out PdfObject? obj))
        {
            // Use standard encoding based on font name
            Encoding = GetStandardEncoding(BaseFont);
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
            PdfDictionary encodingDict => PdfFontEncoding.FromDictionary(encodingDict, GetStandardEncoding(BaseFont)),
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

        // Get default width
        PdfFontDescriptor? descriptor = GetDescriptor();
        if (descriptor != null)
        {
            _defaultWidth = descriptor.MissingWidth > 0 ? descriptor.MissingWidth : descriptor.AvgWidth;
        }

        // If still no width info, use standard metrics
        if (_widths == null && _defaultWidth == 0)
        {
            _defaultWidth = GetStandardWidth(BaseFont);
        }
    }

    private static PdfFontEncoding GetStandardEncoding(string baseFontName)
    {
        // Standard 14 fonts use specific encodings
        if (IsStandard14Font(baseFontName))
        {
            if (baseFontName.Contains("Symbol") || baseFontName.Contains("ZapfDingbats"))
                return PdfFontEncoding.GetStandardEncoding("SymbolEncoding");
            return PdfFontEncoding.GetStandardEncoding("WinAnsiEncoding");
        }

        return PdfFontEncoding.GetStandardEncoding("StandardEncoding");
    }

    private static double GetStandardWidth(string baseFontName)
    {
        // Approximate widths for standard 14 fonts
        return baseFontName switch
        {
            var name when name.Contains("Courier") => 600, // Courier is monospace
            var name when name.Contains("Helvetica") => 556,
            var name when name.Contains("Times") => 500,
            var name when name.Contains("Symbol") => 600,
            var name when name.Contains("ZapfDingbats") => 600,
            _ => 500
        };
    }

    private static bool IsStandard14Font(string baseFontName)
    {
        string[] standard14 =
        [
            "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic",
            "Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique",
            "Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique",
            "Symbol", "ZapfDingbats"
        ];

        return Array.Exists(standard14, font => baseFontName.Contains(font));
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
