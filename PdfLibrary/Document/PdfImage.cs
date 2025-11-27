using PdfLibrary.Content.Operators;
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
    private readonly bool _isInlineImage;

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
        _isInlineImage = false;

        // Verify this is an image XObject
        if (!IsImageXObject(stream))
            throw new ArgumentException("Stream is not an image XObject (Subtype must be /Image)");
    }

    /// <summary>
    /// Creates a PdfImage from an inline image operator
    /// </summary>
    /// <param name="inlineImage">The inline image operator containing image data</param>
    public PdfImage(InlineImageOperator inlineImage)
    {
        ArgumentNullException.ThrowIfNull(inlineImage);

        _isInlineImage = true;
        _document = null;

        // Create a synthetic PdfStream from the inline image data
        var dict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(inlineImage.Width),
            [new PdfName("Height")] = new PdfInteger(inlineImage.Height),
            [new PdfName("BitsPerComponent")] = new PdfInteger(inlineImage.BitsPerComponent)
        };

        // Map color space (handle abbreviated names)
        string colorSpace = inlineImage.ColorSpace;
        dict[new PdfName("ColorSpace")] = new PdfName(colorSpace);

        // Copy filter if present
        string? filterName = null;
        if (inlineImage.Filter is not null)
        {
            // Map abbreviated filter names to full names
            filterName = inlineImage.Filter switch
            {
                "AHx" => "ASCIIHexDecode",
                "A85" => "ASCII85Decode",
                "LZW" => "LZWDecode",
                "Fl" => "FlateDecode",
                "RL" => "RunLengthDecode",
                "CCF" => "CCITTFaxDecode",
                "DCT" => "DCTDecode",
                _ => inlineImage.Filter
            };
            dict[new PdfName("Filter")] = new PdfName(filterName);
        }

        // Copy decode params if present, or create them for CCITTFaxDecode
        if (inlineImage.DecodeParms is not null)
        {
            // For CCITTFaxDecode, ensure Columns/Rows are set
            if (filterName == "CCITTFaxDecode" && inlineImage.DecodeParms is PdfDictionary dpDict)
            {
                // Clone the dictionary and add missing Columns/Rows
                var newDpDict = new PdfDictionary();
                foreach (KeyValuePair<PdfName, PdfObject> kvp in dpDict)
                {
                    newDpDict[kvp.Key] = kvp.Value;
                }

                // Add Columns if not present
                if (!newDpDict.ContainsKey(new PdfName("Columns")))
                {
                    newDpDict[new PdfName("Columns")] = new PdfInteger(inlineImage.Width);
                }

                // Add Rows if not present
                if (!newDpDict.ContainsKey(new PdfName("Rows")))
                {
                    newDpDict[new PdfName("Rows")] = new PdfInteger(inlineImage.Height);
                }

                dict[new PdfName("DecodeParms")] = newDpDict;
            }
            else
            {
                dict[new PdfName("DecodeParms")] = inlineImage.DecodeParms;
            }
        }
        else if (filterName == "CCITTFaxDecode")
        {
            // Create DecodeParms with Columns/Rows for CCITTFaxDecode
            var dpDict = new PdfDictionary
            {
                [new PdfName("Columns")] = new PdfInteger(inlineImage.Width),
                [new PdfName("Rows")] = new PdfInteger(inlineImage.Height)
            };
            dict[new PdfName("DecodeParms")] = dpDict;
        }

        // Copy decode array if present
        if (inlineImage.Decode is not null)
        {
            dict[new PdfName("Decode")] = inlineImage.Decode;
        }

        // Copy image mask flag if true
        if (inlineImage.ImageMask)
        {
            dict[new PdfName("ImageMask")] = PdfBoolean.True;
        }

        // Copy interpolate flag if true
        if (inlineImage.Interpolate)
        {
            dict[new PdfName("Interpolate")] = PdfBoolean.True;
        }

        // Create the synthetic stream with raw image data
        _stream = new PdfStream(dict, inlineImage.ImageData);
    }

    /// <summary>
    /// Gets whether this is an inline image (from BI/ID/EI operators)
    /// </summary>
    public bool IsInlineImage => _isInlineImage;

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
            if (_stream.Dictionary.TryGetValue(new PdfName("Width"), out PdfObject obj) && obj is PdfInteger width)
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
            if (_stream.Dictionary.TryGetValue(new PdfName("Height"), out PdfObject obj) && obj is PdfInteger height)
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
            if (_stream.Dictionary.TryGetValue(new PdfName("BitsPerComponent"), out PdfObject obj) && obj is PdfInteger bits)
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
            if (obj is PdfIndirectReference reference && _document is not null)
                obj = _document.ResolveReference(reference);

            return obj switch
            {
                PdfName name => name.Value,
                PdfArray { Count: > 0 } array when array[0] is PdfName arrayName => arrayName.Value,
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

            if (!_stream.Dictionary.TryGetValue(new PdfName("Filter"), out PdfObject obj))
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
    public bool HasAlpha =>
        // Check for soft mask (alpha channel)
        _stream.Dictionary.ContainsKey(new PdfName("SMask")) ||
        // Check for mask (binary transparency)
        _stream.Dictionary.ContainsKey(new PdfName("Mask"));

    /// <summary>
    /// Gets the image intent (rendering intent)
    /// </summary>
    public string? Intent
    {
        get
        {
            if (_stream.Dictionary.TryGetValue(new PdfName("Intent"), out PdfObject obj) && obj is PdfName intent)
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
            if (_stream.Dictionary.TryGetValue(new PdfName("ImageMask"), out PdfObject obj) && obj is PdfBoolean mask)
                return mask.Value;
            return false;
        }
    }

    /// <summary>
    /// Gets the Decode array for the image (if present).
    /// For image masks, [0 1] means 0=transparent/1=paint (default), [1 0] means 1=transparent/0=paint.
    /// For regular images, maps sample values to a range.
    /// </summary>
    public double[]? DecodeArray
    {
        get
        {
            if (!_stream.Dictionary.TryGetValue(new PdfName("Decode"), out PdfObject? obj))
                return null;

            if (obj is not PdfArray array)
                return null;

            var result = new double[array.Count];
            for (var i = 0; i < array.Count; i++)
            {
                result[i] = array[i] switch
                {
                    PdfInteger intVal => intVal.Value,
                    PdfReal realVal => realVal.Value,
                    _ => 0.0
                };
            }

            return result;
        }
    }

    /// <summary>
    /// Gets the raw decoded image data
    /// The data format depends on ColorSpace, BitsPerComponent, and dimensions
    /// </summary>
    public byte[] GetDecodedData()
    {
        // For CCITTFaxDecode, we need to ensure Rows is set to the image Height
        // because the PDF may omit Rows in DecodeParms, expecting the decoder to use the image dimensions
        if (IsCcittFilter())
        {
            return GetDecodedDataWithCcittFix();
        }

        return _stream.GetDecodedData();
    }

    /// <summary>
    /// Checks if the image uses CCITTFaxDecode filter
    /// </summary>
    private bool IsCcittFilter()
    {
        if (!_stream.Dictionary.TryGetValue(PdfName.Filter, out PdfObject? filterObj))
            return false;

        return filterObj switch
        {
            PdfName name => name.Value is "CCITTFaxDecode" or "CCF",
            PdfArray array => array.Count > 0 && array[0] is PdfName name && name.Value is "CCITTFaxDecode" or "CCF",
            _ => false
        };
    }

    /// <summary>
    /// Gets decoded data for CCITT images, ensuring Rows parameter is set
    /// </summary>
    private byte[] GetDecodedDataWithCcittFix()
    {
        // Get or create decode params with Rows set to image Height
        var decodeParams = new Dictionary<string, object>();

        // Copy existing decode params
        if (_stream.Dictionary.TryGetValue(PdfName.DecodeParms, out PdfObject? decodeParmObj) && decodeParmObj is PdfDictionary decodeParmDict)
        {
            foreach (var kvp in decodeParmDict)
            {
                object? value = kvp.Value switch
                {
                    PdfInteger intVal => intVal.Value,
                    PdfReal realVal => realVal.Value,
                    PdfBoolean boolVal => (bool)boolVal,
                    PdfName nameVal => nameVal.Value,
                    _ => null
                };

                if (value is not null)
                    decodeParams[kvp.Key.Value] = value;
            }
        }

        // Ensure Columns is set to image Width
        if (!decodeParams.ContainsKey("Columns"))
        {
            decodeParams["Columns"] = Width;
        }

        // Ensure Rows is set to image Height - this is the critical fix!
        if (!decodeParams.ContainsKey("Rows"))
        {
            decodeParams["Rows"] = Height;
        }

        // Apply CCITT filter directly
        var filter = new Filters.CcittFaxDecodeFilter();
        return filter.Decode(_stream.Data, decodeParams);
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
        if (!stream.Dictionary.TryGetValue(new PdfName("Subtype"), out PdfObject obj))
            return false;

        return obj is PdfName { Value: "Image" };
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
        if (obj is PdfIndirectReference reference && _document is not null)
            obj = _document.ResolveReference(reference);

        // Indexed color space is an array: [/Indexed base hival lookup]
        if (obj is not PdfArray csArray || csArray.Count < 4)
            return null;

        // Check if it's Indexed
        if (csArray[0] is not PdfName { Value: "Indexed" })
            return null;

        baseColorSpace = csArray[1] switch
        {
            // Extract base color space (index 1)
            PdfName baseName => baseName.Value,
            PdfArray { Count: > 0 } baseArray when baseArray[0] is PdfName baseArrayName => baseArrayName.Value,
            _ => "DeviceRGB"
        };

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
        if (lookupObj is PdfIndirectReference lookupRef && _document is not null)
            lookupObj = _document.ResolveReference(lookupRef);

        // Get palette data

        byte[]? paletteData = lookupObj switch
        {
            PdfString lookupString => lookupString.Bytes,
            PdfStream lookupStream => lookupStream.GetDecodedData(),
            _ => null
        };

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
