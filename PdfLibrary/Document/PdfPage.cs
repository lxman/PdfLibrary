using System.Text;
using PdfLibrary.Content;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Logging;
using PdfLibrary.Structure;

namespace PdfLibrary.Document;

/// <summary>
/// Represents a PDF page (ISO 32000-1:2008 section 7.7.3.3)
/// </summary>
public class PdfPage
{
    private readonly PdfDictionary _dictionary;
    private readonly PdfDocument? _document;
    private readonly PdfDictionary? _parentNode;

    /// <summary>
    /// Creates a page from a dictionary
    /// </summary>
    public PdfPage(PdfDictionary dictionary, PdfDocument? document = null, PdfDictionary? parentNode = null)
    {
        _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        _document = document;
        _parentNode = parentNode;

        // Verify this is a page
        if (!_dictionary.TryGetValue(PdfName.TypeName, out PdfObject typeObj) ||
            typeObj is not PdfName typeName) return;
        if (typeName.Value != "Page")
            throw new ArgumentException($"Dictionary is not a Page (Type = {typeName.Value})");
    }

    /// <summary>
    /// Gets the underlying dictionary
    /// </summary>
    public PdfDictionary Dictionary => _dictionary;

    /// <summary>
    /// Gets the page resources (inheritable)
    /// </summary>
    public PdfResources? GetResources()
    {
        // Try to get from page first
        if (_dictionary.TryGetValue(new PdfName("Resources"), out PdfObject? obj))
        {
            if (obj is PdfIndirectReference reference && _document != null)
                obj = _document.ResolveReference(reference);

            if (obj is PdfDictionary resourceDict)
                return new PdfResources(resourceDict, _document);
        }

        // Inherit from parent if not found
        if (_parentNode == null || !_parentNode.TryGetValue(new PdfName("Resources"), out obj)) return null;
        {
            if (obj is PdfIndirectReference reference && _document != null)
                obj = _document.ResolveReference(reference);

            if (obj is PdfDictionary resourceDict)
                return new PdfResources(resourceDict, _document);
        }

        return null;
    }

    /// <summary>
    /// Gets the MediaBox (page size) - inheritable, required
    /// </summary>
    public PdfRectangle GetMediaBox()
    {
        PdfArray? array = GetInheritableArray("MediaBox");
        if (array == null)
            throw new InvalidOperationException("Page missing required MediaBox");

        PdfRectangle rect = PdfRectangle.FromArray(array);
        PdfLogger.Log(LogCategory.PdfTool, $"[MEDIABOX] Found: {rect} (document={_document != null})");
        return rect;
    }

    /// <summary>
    /// Gets the CropBox (visible area) - inheritable, optional
    /// Defaults to MediaBox if not specified
    /// </summary>
    public PdfRectangle GetCropBox()
    {
        PdfArray? array = GetInheritableArray("CropBox");
        return array != null
            ? PdfRectangle.FromArray(array)
            : GetMediaBox();
    }

    /// <summary>
    /// Gets the page rotation in degrees (0, 90, 180, or 270) - inheritable
    /// </summary>
    public int Rotate
    {
        get
        {
            // Try page first
            if (_dictionary.TryGetValue(new PdfName("Rotate"), out PdfObject obj) && obj is PdfInteger rotate)
                return rotate.Value;

            // Inherit from the parent
            if (_parentNode != null && _parentNode.TryGetValue(new PdfName("Rotate"), out obj) && obj is PdfInteger parentRotate)
                return parentRotate.Value;

            return 0;
        }
    }

    /// <summary>
    /// Gets the page contents (content streams)
    /// </summary>
    public List<PdfStream> GetContents()
    {
        var streams = new List<PdfStream>();

        if (!_dictionary.TryGetValue(new PdfName("Contents"), out PdfObject? obj))
            return streams;

        // Resolve indirect reference
        if (obj is PdfIndirectReference reference && _document != null)
            obj = _document.ResolveReference(reference);

        switch (obj)
        {
            // Contents can be a single stream or an array of streams
            case PdfStream stream:
                streams.Add(stream);
                break;
            case PdfArray array:
            {
                foreach (PdfObject item in array)
                {
                    PdfObject? itemObj = item;

                    if (itemObj is PdfIndirectReference itemRef && _document != null)
                        itemObj = _document.ResolveReference(itemRef);

                    if (itemObj is PdfStream itemStream)
                        streams.Add(itemStream);
                }

                break;
            }
        }

        return streams;
    }

    /// <summary>
    /// Gets the page annotations
    /// </summary>
    public PdfArray? GetAnnotations()
    {
        if (!_dictionary.TryGetValue(new PdfName("Annots"), out PdfObject? obj))
            return null;

        if (obj is PdfIndirectReference reference && _document != null)
            obj = _document.ResolveReference(reference);

        return obj as PdfArray;
    }

    /// <summary>
    /// Gets the page width in points (1/72 inch)
    /// </summary>
    public double Width
    {
        get
        {
            PdfRectangle mediaBox = GetMediaBox();
            double width = mediaBox.Width;

            // Swap width/height if rotated 90 or 270 degrees
            int rotation = Rotate;
            return rotation is 90 or 270
                ? mediaBox.Height
                : width;
        }
    }

    /// <summary>
    /// Gets the page height in points (1/72 inch)
    /// </summary>
    public double Height
    {
        get
        {
            PdfRectangle mediaBox = GetMediaBox();
            double height = mediaBox.Height;

            // Swap width/height if rotated 90 or 270 degrees
            int rotation = Rotate;
            return rotation is 90 or 270
                ? mediaBox.Width
                : height;
        }
    }

    /// <summary>
    /// Helper to get inheritable array values
    /// Traverses the full parent chain as per PDF spec
    /// </summary>
    private PdfArray? GetInheritableArray(string key)
    {
        var keyName = new PdfName(key);

        // Try page first
        if (_dictionary.TryGetValue(keyName, out PdfObject obj) && obj is PdfArray array)
            return array;

        // Traverse parent chain to find inherited value
        PdfDictionary? current = _parentNode;

        // If no parent node was passed, try to get it from the page's /Parent key
        if (current == null && _document != null)
        {
            if (_dictionary.TryGetValue(new PdfName("Parent"), out PdfObject parentObj))
            {
                current = ResolveDict(parentObj);
                PdfLogger.Log(LogCategory.PdfTool, $"[INHERIT] Got parent from /Parent key: {current != null}");
            }
            else
            {
                PdfLogger.Log(LogCategory.PdfTool, "[INHERIT] No /Parent key in page dictionary");
            }
        }
        else if (current == null)
        {
            PdfLogger.Log(LogCategory.PdfTool, $"[INHERIT] No parent and no document (document={_document != null})");
        }

        // Walk up the parent chain
        while (current != null)
        {
            if (current.TryGetValue(keyName, out obj) && obj is PdfArray parentArray)
                return parentArray;

            // Move to next parent
            if (current.TryGetValue(new PdfName("Parent"), out PdfObject nextParent))
            {
                current = ResolveDict(nextParent);
            }
            else
            {
                break;
            }
        }

        return null;
    }

    private PdfDictionary? ResolveDict(PdfObject obj)
    {
        if (obj is PdfDictionary dict)
            return dict;

        if (obj is PdfIndirectReference reference && _document != null)
        {
            PdfObject? resolved = _document.ResolveReference(reference);
            if (resolved is PdfDictionary resolvedDict)
                return resolvedDict;
        }

        return null;
    }

    /// <summary>
    /// Extracts all text from this page
    /// </summary>
    public string ExtractText()
    {
        List<PdfStream> contents = GetContents();
        if (contents.Count == 0)
            return string.Empty;

        var allText = new StringBuilder();
        PdfResources? resources = GetResources();

        foreach (byte[] contentData in contents.Select(stream => stream.GetDecodedData()))
        {
            string text = PdfTextExtractor.ExtractText(contentData, resources);
            allText.Append(text);
        }

        return allText.ToString();
    }

    /// <summary>
    /// Extracts text with position and formatting information
    /// </summary>
    public (string Text, List<TextFragment> Fragments) ExtractTextWithFragments()
    {
        List<PdfStream> contents = GetContents();
        if (contents.Count == 0)
            return (string.Empty, []);

        var allText = new StringBuilder();
        var allFragments = new List<TextFragment>();
        PdfResources? resources = GetResources();

        foreach (byte[] decodedData in contents.Select(stream => stream.GetDecodedData()))
        {
            (string text, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(decodedData, resources);
            allText.Append(text);
            allFragments.AddRange(fragments);
        }

        return (allText.ToString(), allFragments);
    }

    /// <summary>
    /// Gets all images on this page
    /// </summary>
    public List<PdfImage> GetImages()
    {
        var images = new List<PdfImage>();
        PdfResources? resources = GetResources();

        if (resources == null)
            return images;

        // Get all XObject names
        List<string> xobjectNames = resources.GetXObjectNames();

        // Check each XObject to see if it's an image
        foreach (string name in xobjectNames)
        {
            PdfStream? xobject = resources.GetXObject(name);
            if (xobject == null)
                continue;

            // Check if this XObject is an image
            if (PdfImage.IsImageXObject(xobject))
            {
                try
                {
                    var image = new PdfImage(xobject, _document);
                    images.Add(image);
                }
                catch (Exception)
                {
                    // Skip malformed images
                }
            }
        }

        return images;
    }

    /// <summary>
    /// Gets the number of images on this page
    /// </summary>
    public int GetImageCount()
    {
        return GetImages().Count;
    }
}

/// <summary>
/// Represents a PDF rectangle (used for page boxes)
/// </summary>
public readonly struct PdfRectangle(double x1, double y1, double x2, double y2)
{
    public double X1 { get; } = x1;
    public double Y1 { get; } = y1;
    public double X2 { get; } = x2;
    public double Y2 { get; } = y2;

    public double Width => Math.Abs(X2 - X1);
    public double Height => Math.Abs(Y2 - Y1);

    public static PdfRectangle FromArray(PdfArray array)
    {
        if (array.Count != 4)
            throw new ArgumentException($"Rectangle array must have 4 elements, got {array.Count}");

        double x1 = GetNumber(array[0]);
        double y1 = GetNumber(array[1]);
        double x2 = GetNumber(array[2]);
        double y2 = GetNumber(array[3]);

        return new PdfRectangle(x1, y1, x2, y2);
    }

    private static double GetNumber(PdfObject obj)
    {
        return obj switch
        {
            PdfInteger integer => integer.Value,
            PdfReal real => real.Value,
            _ => 0
        };
    }

    public override string ToString() => $"[{X1}, {Y1}, {X2}, {Y2}] ({Width}x{Height})";
}
