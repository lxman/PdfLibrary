using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Structure;

namespace PdfLibrary.Fonts;

/// <summary>
/// Represents a PDF font (ISO 32000-1:2008 section 9.2)
/// Base class for all font types
/// </summary>
public abstract class PdfFont(PdfDictionary dictionary, PdfDocument? document = null)
{
    protected readonly PdfDictionary _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
    protected readonly PdfDocument? _document = document;

    /// <summary>
    /// Gets the font type (Type1, TrueType, Type3, Type0, etc.)
    /// </summary>
    public abstract PdfFontType FontType { get; }

    /// <summary>
    /// Gets the font's base name
    /// </summary>
    public string BaseFont
    {
        get
        {
            if (_dictionary.TryGetValue(new PdfName("BaseFont"), out PdfObject obj) && obj is PdfName name)
                return name.Value;
            return "Unknown";
        }
    }

    /// <summary>
    /// Gets the font encoding
    /// </summary>
    public PdfFontEncoding? Encoding { get; protected set; }

    /// <summary>
    /// Gets the ToUnicode CMap if present
    /// </summary>
    public ToUnicodeCMap? ToUnicode { get; protected set; }

    /// <summary>
    /// Gets the first character code in the font
    /// </summary>
    public int FirstChar
    {
        get
        {
            if (_dictionary.TryGetValue(new PdfName("FirstChar"), out PdfObject obj) && obj is PdfInteger i)
                return i.Value;
            return 0;
        }
    }

    /// <summary>
    /// Gets the last character code in the font
    /// </summary>
    public int LastChar
    {
        get
        {
            if (_dictionary.TryGetValue(new PdfName("LastChar"), out PdfObject obj) && obj is PdfInteger i)
                return i.Value;
            return 255;
        }
    }

    /// <summary>
    /// Converts a character code to Unicode
    /// </summary>
    public virtual string DecodeCharacter(int charCode)
    {
        // Try ToUnicode CMap first (most reliable)
        string? unicode = ToUnicode?.Lookup(charCode);
        if (unicode is not null)
            return unicode;

        // Fall back to encoding
        if (Encoding is not null)
        {
            return Encoding.DecodeCharacter(charCode);
        }

        // Last resort: use the character code as-is
        return char.ConvertFromUtf32(charCode);
    }

    /// <summary>
    /// Gets the width of a character in glyph space units
    /// </summary>
    public abstract double GetCharacterWidth(int charCode);

    /// <summary>
    /// Gets the font descriptor
    /// </summary>
    public PdfFontDescriptor? GetDescriptor()
    {
        if (_dictionary.TryGetValue(new PdfName("FontDescriptor"), out PdfObject? obj))
        {
            if (obj is PdfIndirectReference reference && _document is not null)
                obj = _document.ResolveReference(reference);

            if (obj is PdfDictionary descriptorDict)
                return new PdfFontDescriptor(descriptorDict, _document);
        }

        return null;
    }

    /// <summary>
    /// Gets embedded font metrics from TrueType/OpenType font data
    /// Only available for fonts with embedded TrueType data
    /// </summary>
    /// <returns>EmbeddedFontMetrics if font has embedded TrueType data, null otherwise</returns>
    public virtual EmbeddedFontMetrics? GetEmbeddedMetrics()
    {
        // Default implementation: no embedded metrics
        // Overridden in TrueTypeFont and Type0Font
        return null;
    }

    /// <summary>
    /// Creates a font from a dictionary
    /// </summary>
    public static PdfFont? Create(PdfDictionary dictionary, PdfDocument? document = null)
    {
        if (!dictionary.TryGetValue(new PdfName("Subtype"), out PdfObject subtypeObj) || subtypeObj is not PdfName subtype)
            return null;

        return subtype.Value switch
        {
            "Type1" => new Type1Font(dictionary, document),
            "TrueType" => new TrueTypeFont(dictionary, document),
            "Type3" => new Type3Font(dictionary, document),
            "Type0" => new Type0Font(dictionary, document),
            "MMType1" => new Type1Font(dictionary, document), // Treat as Type1
            _ => null
        };
    }

    /// <summary>
    /// Loads ToUnicode CMap if present
    /// </summary>
    protected void LoadToUnicodeCMap()
    {
        if (_dictionary.TryGetValue(new PdfName("ToUnicode"), out PdfObject? obj))
        {
            if (obj is PdfIndirectReference reference && _document is not null)
                obj = _document.ResolveReference(reference);

            if (obj is PdfStream stream)
            {
                byte[] data = stream.GetDecodedData(_document?.Decryptor);
                ToUnicode = ToUnicodeCMap.Parse(data);
            }
        }
    }
}

/// <summary>
/// PDF font types
/// </summary>
public enum PdfFontType
{
    Type1,
    TrueType,
    Type3,
    Type0,
    MMType1
}
