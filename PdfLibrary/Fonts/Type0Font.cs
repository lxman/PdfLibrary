using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Fonts;

/// <summary>
/// Represents a Type 0 (composite/CID) font (ISO 32000-1:2008 section 9.7)
/// Used for fonts with large character sets (e.g., CJK fonts)
/// </summary>
public class Type0Font : PdfFont
{
    private PdfFont? _descendantFont;

    public Type0Font(PdfDictionary dictionary, PdfDocument? document = null)
        : base(dictionary, document)
    {
        LoadToUnicodeCMap(); // ToUnicode is critical for Type 0 fonts
        LoadDescendantFont();
    }

    public override PdfFontType FontType => PdfFontType.Type0;

    /// <summary>
    /// Gets the descendant CIDFont
    /// </summary>
    public PdfFont? DescendantFont => _descendantFont;

    public override double GetCharacterWidth(int charCode)
    {
        // Delegate to descendant font
        if (_descendantFont != null)
            return _descendantFont.GetCharacterWidth(charCode);

        return 1000; // CID fonts typically use 1000 as default
    }

    public override string DecodeCharacter(int charCode)
    {
        // Type 0 fonts MUST use ToUnicode CMap or CIDToGIDMap
        if (ToUnicode == null) return char.ConvertFromUtf32(charCode);
        string? unicode = ToUnicode.Lookup(charCode);
        return unicode ??
               // Fall back to character code
               char.ConvertFromUtf32(charCode);
    }

    private void LoadDescendantFont()
    {
        if (!_dictionary.TryGetValue(new PdfName("DescendantFonts"), out PdfObject? obj))
            return;

        // Resolve indirect reference
        if (obj is PdfIndirectReference reference && _document != null)
            obj = _document.ResolveReference(reference);

        // DescendantFonts is an array with a single CIDFont
        if (obj is not PdfArray { Count: > 0 } array) return;
        PdfObject? descendantObj = array[0];

        if (descendantObj is PdfIndirectReference descRef && _document != null)
            descendantObj = _document.ResolveReference(descRef);

        if (descendantObj is PdfDictionary descendantDict)
        {
            _descendantFont = new CidFont(descendantDict, _document);
        }
    }
}

/// <summary>
/// Represents a CIDFont (Character Identifier font)
/// Used as a descendant font of Type 0 fonts
/// </summary>
internal class CidFont : PdfFont
{
    private double _defaultWidth = 1000;
    private Dictionary<int, double>? _widths;

    public CidFont(PdfDictionary dictionary, PdfDocument? document = null)
        : base(dictionary, document)
    {
        LoadWidths();
    }

    public override PdfFontType FontType => PdfFontType.Type0;

    public override double GetCharacterWidth(int charCode)
    {
        if (_widths != null && _widths.TryGetValue(charCode, out double width))
            return width;

        return _defaultWidth;
    }

    private void LoadWidths()
    {
        // Get default width (DW)
        if (_dictionary.TryGetValue(new PdfName("DW"), out PdfObject dwObj))
        {
            _defaultWidth = GetNumber(dwObj);
        }

        // Get width array (W)
        if (_dictionary.TryGetValue(new PdfName("W"), out PdfObject? wObj))
        {
            if (wObj is PdfIndirectReference reference && _document != null)
                wObj = _document.ResolveReference(reference);

            if (wObj is PdfArray widthArray)
            {
                _widths = ParseWidthArray(widthArray);
            }
        }

        // Try to get from descriptor
        if (_widths != null && _widths.Count != 0) return;
        PdfFontDescriptor? descriptor = GetDescriptor();
        if (descriptor is { MissingWidth: > 0 })
            _defaultWidth = descriptor.MissingWidth;
    }

    private Dictionary<int, double> ParseWidthArray(PdfArray array)
    {
        var widths = new Dictionary<int, double>();
        var i = 0;

        while (i < array.Count)
        {
            if (array[i] is not PdfInteger startCid)
                break;

            int start = startCid.Value;
            i++;

            if (i >= array.Count)
                break;

            // Format 1: start_cid [ w1 w2 ... wn ]
            if (array[i] is PdfArray widthList)
            {
                for (var j = 0; j < widthList.Count; j++)
                {
                    widths[start + j] = GetNumber(widthList[j]);
                }
                i++;
            }
            // Format 2: start_cid end_cid width
            else if (array[i] is PdfInteger endCid && i + 1 < array.Count)
            {
                int end = endCid.Value;
                double width = GetNumber(array[i + 1]);

                for (int cid = start; cid <= end; cid++)
                {
                    widths[cid] = width;
                }
                i += 2;
            }
            else
            {
                break;
            }
        }

        return widths;
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
