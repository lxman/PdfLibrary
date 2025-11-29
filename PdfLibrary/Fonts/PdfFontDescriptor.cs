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
                    array[0].ToDouble(),
                    array[1].ToDouble(),
                    array[2].ToDouble(),
                    array[3].ToDouble()
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
                return obj.ToDouble();
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
                return obj.ToDouble();
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
                return obj.ToDouble();
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
                return obj.ToDouble();
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
                return obj.ToDouble();
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
                return obj.ToDouble();
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
                return obj.ToDouble();
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
                return obj.ToDouble();
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
                return obj.ToDouble();
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
                return obj.ToDouble();
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
                return obj.ToDouble();
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
            if (obj is PdfIndirectReference reference && _document is not null)
                obj = _document.ResolveReference(reference);

            if (obj is PdfStream stream)
                return stream.GetDecodedData(_document?.Decryptor);
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
            if (obj is PdfIndirectReference reference && _document is not null)
                obj = _document.ResolveReference(reference);

            if (obj is PdfStream stream)
                return stream.GetDecodedData(_document?.Decryptor);
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
            if (obj is PdfIndirectReference reference && _document is not null)
                obj = _document.ResolveReference(reference);

            if (obj is PdfStream stream)
                return stream.GetDecodedData(_document?.Decryptor);
        }
        return null;
    }

    /// <summary>
    /// Gets the FontFile stream (Type1 font program) along with Length1/Length2/Length3 parameters
    /// needed for proper parsing of the Type1 font data
    /// </summary>
    /// <returns>Tuple of (data, length1, length2, length3) or null if not found</returns>
    public (byte[] data, int length1, int length2, int length3)? GetFontFileWithLengths()
    {
        if (!_dictionary.TryGetValue(new PdfName("FontFile"), out PdfObject? obj))
            return null;

        // Resolve indirect reference if needed
        if (obj is PdfIndirectReference reference && _document is not null)
            obj = _document.ResolveReference(reference);

        if (obj is not PdfStream stream)
            return null;

        byte[] data = stream.GetDecodedData(_document?.Decryptor);

        // Get Length1, Length2, Length3 from stream dictionary
        int length1 = 0, length2 = 0, length3 = 0;

        if (stream.Dictionary.TryGetValue(new PdfName("Length1"), out PdfObject? l1Obj))
            length1 = l1Obj.ToInt();
        if (stream.Dictionary.TryGetValue(new PdfName("Length2"), out PdfObject? l2Obj))
            length2 = l2Obj.ToInt();
        if (stream.Dictionary.TryGetValue(new PdfName("Length3"), out PdfObject? l3Obj))
            length3 = l3Obj.ToInt();

        return (data, length1, length2, length3);
    }

}
