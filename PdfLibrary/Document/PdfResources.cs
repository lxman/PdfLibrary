using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Structure;

namespace PdfLibrary.Document;

/// <summary>
/// Represents a PDF resource dictionary (ISO 32000-1:2008 section 7.8.3)
/// Resources define objects used by content streams (fonts, images, etc.)
/// </summary>
public class PdfResources
{
    private readonly PdfDictionary _dictionary;
    private readonly PdfDocument? _document;
    private readonly Dictionary<string, PdfFont?> _fontCache = new();

    /// <summary>
    /// Creates a resources object from a dictionary
    /// </summary>
    public PdfResources(PdfDictionary dictionary, PdfDocument? document = null)
    {
        _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        _document = document;
    }

    /// <summary>
    /// Gets the underlying dictionary
    /// </summary>
    public PdfDictionary Dictionary => _dictionary;

    /// <summary>
    /// Gets the Font resources dictionary
    /// Maps font names to font dictionaries
    /// </summary>
    public PdfDictionary? GetFonts()
    {
        if (!_dictionary.TryGetValue(new PdfName("Font"), out PdfObject? obj))
            return null;

        if (obj is PdfIndirectReference reference && _document is not null)
            obj = _document.ResolveReference(reference);

        return obj as PdfDictionary;
    }

    /// <summary>
    /// Gets a specific font by name
    /// </summary>
    public PdfDictionary? GetFont(string name)
    {
        PdfDictionary? fonts = GetFonts();
        if (fonts is null)
            return null;

        if (!fonts.TryGetValue(new PdfName(name), out PdfObject? obj))
            return null;

        if (obj is PdfIndirectReference reference && _document is not null)
            obj = _document.ResolveReference(reference);

        return obj as PdfDictionary;
    }

    /// <summary>
    /// Gets a specific font object by name (with caching)
    /// Returns a PdfFont object that can be used for text decoding and width calculation
    /// </summary>
    public PdfFont? GetFontObject(string name)
    {
        // Check cache first
        if (_fontCache.TryGetValue(name, out PdfFont? cachedFont))
            return cachedFont;

        // Get font dictionary
        PdfDictionary? fontDict = GetFont(name);
        if (fontDict is null)
        {
            _fontCache[name] = null;
            return null;
        }

        // Create font object using factory
        var font = PdfFont.Create(fontDict, _document);

        // Cache for future use
        _fontCache[name] = font;

        return font;
    }

    /// <summary>
    /// Gets the XObject resources dictionary
    /// Maps XObject names to XObject streams (images, forms)
    /// </summary>
    public PdfDictionary? GetXObjects()
    {
        if (!_dictionary.TryGetValue(new PdfName("XObject"), out PdfObject? obj))
            return null;

        if (obj is PdfIndirectReference reference && _document is not null)
            obj = _document.ResolveReference(reference);

        return obj as PdfDictionary;
    }

    /// <summary>
    /// Gets a specific XObject by name
    /// </summary>
    public PdfStream? GetXObject(string name)
    {
        PdfDictionary? xobjects = GetXObjects();
        if (xobjects is null)
            return null;

        if (!xobjects.TryGetValue(new PdfName(name), out PdfObject? obj))
            return null;

        if (obj is PdfIndirectReference reference && _document is not null)
            obj = _document.ResolveReference(reference);

        return obj as PdfStream;
    }

    /// <summary>
    /// Gets the ExtGState resources dictionary
    /// Maps graphics state names to graphics state parameter dictionaries
    /// </summary>
    public PdfDictionary? GetExtGStates()
    {
        if (!_dictionary.TryGetValue(new PdfName("ExtGState"), out PdfObject? obj))
            return null;

        if (obj is PdfIndirectReference reference && _document is not null)
            obj = _document.ResolveReference(reference);

        return obj as PdfDictionary;
    }

    /// <summary>
    /// Gets the ColorSpace resources dictionary
    /// Maps color space names to color space arrays or names
    /// </summary>
    public PdfDictionary? GetColorSpaces()
    {
        if (!_dictionary.TryGetValue(new PdfName("ColorSpace"), out PdfObject? obj))
            return null;

        if (obj is PdfIndirectReference reference && _document is not null)
            obj = _document.ResolveReference(reference);

        return obj as PdfDictionary;
    }

    /// <summary>
    /// Gets the Pattern resources dictionary
    /// Maps pattern names to pattern dictionaries or streams
    /// </summary>
    public PdfDictionary? GetPatterns()
    {
        if (!_dictionary.TryGetValue(new PdfName("Pattern"), out PdfObject? obj))
            return null;

        if (obj is PdfIndirectReference reference && _document is not null)
            obj = _document.ResolveReference(reference);

        return obj as PdfDictionary;
    }

    /// <summary>
    /// Gets the Shading resources dictionary
    /// Maps shading names to shading dictionaries
    /// </summary>
    public PdfDictionary? GetShadings()
    {
        if (!_dictionary.TryGetValue(new PdfName("Shading"), out PdfObject? obj))
            return null;

        if (obj is PdfIndirectReference reference && _document is not null)
            obj = _document.ResolveReference(reference);

        return obj as PdfDictionary;
    }

    /// <summary>
    /// Gets the ProcSet array (obsolete but still commonly present)
    /// </summary>
    public PdfArray? GetProcSet()
    {
        if (_dictionary.TryGetValue(new PdfName("ProcSet"), out PdfObject obj) && obj is PdfArray array)
            return array;

        return null;
    }

    /// <summary>
    /// Gets the Properties resources dictionary
    /// Maps property names to property dictionaries
    /// </summary>
    public PdfDictionary? GetProperties()
    {
        if (!_dictionary.TryGetValue(new PdfName("Properties"), out PdfObject? obj))
            return null;

        if (obj is PdfIndirectReference reference && _document is not null)
            obj = _document.ResolveReference(reference);

        return obj as PdfDictionary;
    }

    /// <summary>
    /// Gets all font names defined in the resources
    /// </summary>
    public List<string> GetFontNames()
    {
        var names = new List<string>();
        PdfDictionary? fonts = GetFonts();

        if (fonts is not null)
        {
            foreach (KeyValuePair<PdfName, PdfObject> kvp in fonts)
            {
                names.Add(kvp.Key.Value);
            }
        }

        return names;
    }

    /// <summary>
    /// Gets all XObject names defined in the resources
    /// </summary>
    public List<string> GetXObjectNames()
    {
        var names = new List<string>();
        PdfDictionary? xobjects = GetXObjects();

        if (xobjects is not null)
        {
            foreach (KeyValuePair<PdfName, PdfObject> kvp in xobjects)
            {
                names.Add(kvp.Key.Value);
            }
        }

        return names;
    }
}
