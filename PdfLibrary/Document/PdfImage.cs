using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Document;

/// <summary>
/// Represents a PDF image XObject (ISO 32000-1:2008 section 8.9.5)
/// Provides access to image metadata and raw image data
/// </summary>
public class PdfImage
{
    private readonly PdfStream _stream;
    private readonly PdfDocument? _document;

    /// <summary>
    /// Creates a PdfImage from an XObject stream
    /// </summary>
    /// <param name="stream">The image XObject stream</param>
    /// <param name="document">The parent PDF document (optional, for resolving references)</param>
    /// <exception cref="ArgumentException">Thrown if the stream is not an image XObject</exception>
    public PdfImage(PdfStream stream, PdfDocument? document = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _document = document;

        // Verify this is an image XObject
        if (!IsImageXObject(stream))
            throw new ArgumentException("Stream is not an image XObject (Subtype must be /Image)");
    }

    /// <summary>
    /// Gets the underlying XObject stream
    /// </summary>
    public PdfStream Stream => _stream;

    /// <summary>
    /// Gets the image width in pixels
    /// </summary>
    public int Width
    {
        get
        {
            if (_stream.Dictionary.TryGetValue(new PdfName("Width"), out PdfObject? obj) && obj is PdfInteger width)
                return width.Value;
            return 0;
        }
    }

    /// <summary>
    /// Gets the image height in pixels
    /// </summary>
    public int Height
    {
        get
        {
            if (_stream.Dictionary.TryGetValue(new PdfName("Height"), out PdfObject? obj) && obj is PdfInteger height)
                return height.Value;
            return 0;
        }
    }

    /// <summary>
    /// Gets the number of bits per color component (typically 1, 2, 4, 8, or 16)
    /// </summary>
    public int BitsPerComponent
    {
        get
        {
            if (_stream.Dictionary.TryGetValue(new PdfName("BitsPerComponent"), out PdfObject? obj) && obj is PdfInteger bits)
                return bits.Value;
            return 8; // Default per PDF spec
        }
    }

    /// <summary>
    /// Gets the color space name (DeviceGray, DeviceRGB, DeviceCMYK, etc.)
    /// </summary>
    public string ColorSpace
    {
        get
        {
            if (!_stream.Dictionary.TryGetValue(new PdfName("ColorSpace"), out PdfObject? obj))
                return "Unknown";

            // Resolve indirect reference
            if (obj is PdfIndirectReference reference && _document != null)
                obj = _document.ResolveReference(reference);

            return obj switch
            {
                PdfName name => name.Value,
                PdfArray array when array.Count > 0 && array[0] is PdfName arrayName => arrayName.Value,
                _ => "Unknown"
            };
        }
    }

    /// <summary>
    /// Gets the number of color components (1 for Gray, 3 for RGB, 4 for CMYK, etc.)
    /// </summary>
    public int ComponentCount
    {
        get
        {
            return ColorSpace switch
            {
                "DeviceGray" or "G" => 1,
                "DeviceRGB" or "RGB" => 3,
                "DeviceCMYK" or "CMYK" => 4,
                "Indexed" => 1, // Indexed color uses a lookup table
                "CalGray" => 1,
                "CalRGB" => 3,
                "Lab" => 3,
                _ => 3 // Default to RGB
            };
        }
    }

    /// <summary>
    /// Gets the filter(s) used to encode the image data
    /// </summary>
    public List<string> Filters
    {
        get
        {
            var filters = new List<string>();

            if (!_stream.Dictionary.TryGetValue(new PdfName("Filter"), out PdfObject? obj))
                return filters;

            switch (obj)
            {
                case PdfName name:
                    filters.Add(name.Value);
                    break;
                case PdfArray array:
                    foreach (PdfObject item in array)
                    {
                        if (item is PdfName filterName)
                            filters.Add(filterName.Value);
                    }
                    break;
            }

            return filters;
        }
    }

    /// <summary>
    /// Checks if the image has an alpha channel (transparency)
    /// </summary>
    public bool HasAlpha
    {
        get
        {
            // Check for soft mask (alpha channel)
            if (_stream.Dictionary.ContainsKey(new PdfName("SMask")))
                return true;

            // Check for mask (binary transparency)
            if (_stream.Dictionary.ContainsKey(new PdfName("Mask")))
                return true;

            return false;
        }
    }

    /// <summary>
    /// Gets the image intent (rendering intent)
    /// </summary>
    public string? Intent
    {
        get
        {
            if (_stream.Dictionary.TryGetValue(new PdfName("Intent"), out PdfObject? obj) && obj is PdfName intent)
                return intent.Value;
            return null;
        }
    }

    /// <summary>
    /// Checks if this is an image mask (1-bit stencil mask)
    /// </summary>
    public bool IsImageMask
    {
        get
        {
            if (_stream.Dictionary.TryGetValue(new PdfName("ImageMask"), out PdfObject? obj) && obj is PdfBoolean mask)
                return mask.Value;
            return false;
        }
    }

    /// <summary>
    /// Gets the raw decoded image data
    /// The data format depends on ColorSpace, BitsPerComponent, and dimensions
    /// </summary>
    public byte[] GetDecodedData()
    {
        return _stream.GetDecodedData();
    }

    /// <summary>
    /// Gets the raw encoded image data (before filter decoding)
    /// </summary>
    public byte[] GetEncodedData()
    {
        return _stream.Data;
    }

    /// <summary>
    /// Gets the expected size of decoded data in bytes
    /// </summary>
    public int GetExpectedDataSize()
    {
        int bitsPerPixel = BitsPerComponent * ComponentCount;
        int bytesPerRow = (Width * bitsPerPixel + 7) / 8; // Round up to nearest byte
        return bytesPerRow * Height;
    }

    /// <summary>
    /// Checks if a stream is an image XObject
    /// </summary>
    public static bool IsImageXObject(PdfStream stream)
    {
        if (!stream.Dictionary.TryGetValue(new PdfName("Subtype"), out PdfObject? obj))
            return false;

        return obj is PdfName subtype && subtype.Value == "Image";
    }

    /// <summary>
    /// Gets the color palette for Indexed color images
    /// Returns null if the image is not Indexed or if palette cannot be extracted
    /// </summary>
    /// <param name="baseColorSpace">Outputs the base color space (DeviceRGB, DeviceGray, etc.)</param>
    /// <param name="hival">Outputs the maximum palette index (0 to hival)</param>
    /// <returns>Palette data as byte array, or null if not applicable</returns>
    public byte[]? GetIndexedPalette(out string? baseColorSpace, out int hival)
    {
        baseColorSpace = null;
        hival = 0;

        if (!_stream.Dictionary.TryGetValue(new PdfName("ColorSpace"), out PdfObject? obj))
            return null;

        // Resolve indirect reference
        if (obj is PdfIndirectReference reference && _document != null)
            obj = _document.ResolveReference(reference);

        // Indexed color space is an array: [/Indexed base hival lookup]
        if (obj is not PdfArray csArray || csArray.Count < 4)
            return null;

        // Check if it's Indexed
        if (csArray[0] is not PdfName csName || csName.Value != "Indexed")
            return null;

        // Extract base color space (index 1)
        if (csArray[1] is PdfName baseName)
        {
            baseColorSpace = baseName.Value;
        }
        else if (csArray[1] is PdfArray baseArray && baseArray.Count > 0 && baseArray[0] is PdfName baseArrayName)
        {
            baseColorSpace = baseArrayName.Value;
        }
        else
        {
            baseColorSpace = "DeviceRGB"; // Default
        }

        // Extract hival (index 2) - maximum palette index
        if (csArray[2] is PdfInteger hivalInt)
        {
            hival = hivalInt.Value;
        }
        else
        {
            return null; // Invalid indexed color space
        }

        // Extract lookup table (index 3) - can be string or stream
        PdfObject lookupObj = csArray[3];

        // Resolve indirect reference to lookup table
        if (lookupObj is PdfIndirectReference lookupRef && _document != null)
            lookupObj = _document.ResolveReference(lookupRef);

        // Get palette data
        byte[]? paletteData = null;

        if (lookupObj is PdfString lookupString)
        {
            // Palette is stored as a string
            paletteData = lookupString.Bytes;
        }
        else if (lookupObj is PdfStream lookupStream)
        {
            // Palette is stored as a stream
            paletteData = lookupStream.GetDecodedData();
        }

        return paletteData;
    }

    /// <summary>
    /// Returns a string representation of the image
    /// </summary>
    public override string ToString()
    {
        string filterInfo = Filters.Count > 0 ? $" [{string.Join(", ", Filters)}]" : "";
        string alphaInfo = HasAlpha ? " +Alpha" : "";
        return $"PdfImage {Width}x{Height} {ColorSpace}/{BitsPerComponent}bpc{filterInfo}{alphaInfo}";
    }
}
