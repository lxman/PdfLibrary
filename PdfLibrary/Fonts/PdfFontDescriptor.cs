using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Fonts;

/// <summary>
/// Represents a PDF font descriptor (ISO 32000-1:2008 section 9.8)
/// Contains font metrics and other font-specific information
/// </summary>
public class PdfFontDescriptor(PdfDictionary dictionary, PdfDocument? document = null)
{
    private readonly PdfDictionary _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
    private readonly PdfDocument? _document = document;

    /// <summary>
    /// Gets the font name
    /// </summary>
    public string FontName
    {
        get
        {
            if (_dictionary.TryGetValue(new PdfName("FontName"), out PdfObject obj) && obj is PdfName name)
                return name.Value;
            return "Unknown";
        }
    }

    /// <summary>
    /// Gets the font family name
    /// </summary>
    public string? FontFamily
    {
        get
        {
            if (_dictionary.TryGetValue(new PdfName("FontFamily"), out PdfObject obj) && obj is PdfString str)
                return str.Value;
            return null;
        }
    }

    /// <summary>
    /// Gets font flags indicating properties
    /// </summary>
    public int Flags
    {
        get
        {
            if (_dictionary.TryGetValue(new PdfName("Flags"), out PdfObject obj) && obj is PdfInteger flags)
                return flags.Value;
            return 0;
        }
    }

    /// <summary>
    /// Checks if font is fixed-pitch (monospace)
    /// </summary>
    public bool IsFixedPitch => (Flags & 0x01) != 0;

    /// <summary>
    /// Checks if font is serif
    /// </summary>
    public bool IsSerif => (Flags & 0x02) != 0;

    /// <summary>
    /// Checks if font is symbolic (uses custom encoding)
    /// </summary>
    public bool IsSymbolic => (Flags & 0x04) != 0;

    /// <summary>
    /// Checks if font is script
    /// </summary>
    public bool IsScript => (Flags & 0x08) != 0;

    /// <summary>
    /// Checks if font is nonsymbolic (uses standard encoding)
    /// </summary>
    public bool IsNonsymbolic => (Flags & 0x20) != 0;

    /// <summary>
    /// Checks if font is italic
    /// </summary>
    public bool IsItalic => (Flags & 0x40) != 0;

    /// <summary>
    /// Checks if font is bold
    /// </summary>
    public bool IsBold => (Flags & 0x40000) != 0;

    /// <summary>
    /// Gets the font bounding box
    /// </summary>
    public (double llx, double lly, double urx, double ury)? FontBBox
    {
        get
        {
            if (_dictionary.TryGetValue(new PdfName("FontBBox"), out PdfObject obj) && obj is PdfArray { Count: 4 } array)
            {
                return (
                    GetNumber(array[0]),
                    GetNumber(array[1]),
                    GetNumber(array[2]),
                    GetNumber(array[3])
                );
            }
            return null;
        }
    }

    /// <summary>
    /// Gets the italic angle in degrees
    /// </summary>
    public double ItalicAngle
    {
        get
        {
            if (_dictionary.TryGetValue(new PdfName("ItalicAngle"), out PdfObject obj))
                return GetNumber(obj);
            return 0;
        }
    }

    /// <summary>
    /// Gets the font ascent (above baseline)
    /// </summary>
    public double Ascent
    {
        get
        {
            if (_dictionary.TryGetValue(new PdfName("Ascent"), out PdfObject obj))
                return GetNumber(obj);
            return 0;
        }
    }

    /// <summary>
    /// Gets the font descent (below baseline)
    /// </summary>
    public double Descent
    {
        get
        {
            if (_dictionary.TryGetValue(new PdfName("Descent"), out PdfObject obj))
                return GetNumber(obj);
            return 0;
        }
    }

    /// <summary>
    /// Gets the font leading (spacing between lines)
    /// </summary>
    public double Leading
    {
        get
        {
            if (_dictionary.TryGetValue(new PdfName("Leading"), out PdfObject obj))
                return GetNumber(obj);
            return 0;
        }
    }

    /// <summary>
    /// Gets the cap height (height of uppercase letters)
    /// </summary>
    public double CapHeight
    {
        get
        {
            if (_dictionary.TryGetValue(new PdfName("CapHeight"), out PdfObject obj))
                return GetNumber(obj);
            return 0;
        }
    }

    /// <summary>
    /// Gets the x-height (height of lowercase x)
    /// </summary>
    public double XHeight
    {
        get
        {
            if (_dictionary.TryGetValue(new PdfName("XHeight"), out PdfObject obj))
                return GetNumber(obj);
            return 0;
        }
    }

    /// <summary>
    /// Gets the stem vertical width
    /// </summary>
    public double StemV
    {
        get
        {
            if (_dictionary.TryGetValue(new PdfName("StemV"), out PdfObject obj))
                return GetNumber(obj);
            return 0;
        }
    }

    /// <summary>
    /// Gets the stem horizontal width
    /// </summary>
    public double StemH
    {
        get
        {
            if (_dictionary.TryGetValue(new PdfName("StemH"), out PdfObject obj))
                return GetNumber(obj);
            return 0;
        }
    }

    /// <summary>
    /// Gets the average character width
    /// </summary>
    public double AvgWidth
    {
        get
        {
            if (_dictionary.TryGetValue(new PdfName("AvgWidth"), out PdfObject obj))
                return GetNumber(obj);
            return 0;
        }
    }

    /// <summary>
    /// Gets the maximum character width
    /// </summary>
    public double MaxWidth
    {
        get
        {
            if (_dictionary.TryGetValue(new PdfName("MaxWidth"), out PdfObject obj))
                return GetNumber(obj);
            return 0;
        }
    }

    /// <summary>
    /// Gets the missing width (default width for undefined characters)
    /// </summary>
    public double MissingWidth
    {
        get
        {
            if (_dictionary.TryGetValue(new PdfName("MissingWidth"), out PdfObject obj))
                return GetNumber(obj);
            return 0;
        }
    }

    /// <summary>
    /// Gets the FontFile2 stream (TrueType font program)
    /// Used for extracting glyph names when ToUnicode CMap is incomplete
    /// </summary>
    public byte[]? GetFontFile2()
    {
        if (_dictionary.TryGetValue(new PdfName("FontFile2"), out PdfObject? obj))
        {
            // Resolve indirect reference if needed
            if (obj is PdfIndirectReference reference && _document != null)
                obj = _document.ResolveReference(reference);

            if (obj is PdfStream stream)
                return stream.GetDecodedData();
        }
        return null;
    }

    /// <summary>
    /// Gets the FontFile3 stream (CFF/OpenType font program)
    /// </summary>
    public byte[]? GetFontFile3()
    {
        if (_dictionary.TryGetValue(new PdfName("FontFile3"), out PdfObject? obj))
        {
            // Resolve indirect reference if needed
            if (obj is PdfIndirectReference reference && _document != null)
                obj = _document.ResolveReference(reference);

            if (obj is PdfStream stream)
                return stream.GetDecodedData();
        }
        return null;
    }

    /// <summary>
    /// Gets the FontFile stream (Type1 font program)
    /// </summary>
    public byte[]? GetFontFile()
    {
        if (_dictionary.TryGetValue(new PdfName("FontFile"), out PdfObject? obj))
        {
            // Resolve indirect reference if needed
            if (obj is PdfIndirectReference reference && _document != null)
                obj = _document.ResolveReference(reference);

            if (obj is PdfStream stream)
                return stream.GetDecodedData();
        }
        return null;
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
