using Logging;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Filters;
using PdfLibrary.Structure;
using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.ColorSpaces.Conversion;

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
    internal PdfImage(PdfStream stream, PdfDocument? document = null)
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
    internal PdfImage(InlineImageOperator inlineImage)
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
        // Check if the color space was already resolved in the Parameters dictionary
        // (PdfRenderer.OnInlineImage resolves color space resource references)
        PdfName csKey = inlineImage.Parameters.ContainsKey(new PdfName("CS"))
            ? new PdfName("CS")
            : new PdfName("ColorSpace");

        if (inlineImage.Parameters.TryGetValue(csKey, out PdfObject? resolvedCs) &&
            resolvedCs is not PdfName)
        {
            // Use the resolved color space object (array or other type)
            dict[new PdfName("ColorSpace")] = resolvedCs;
        }
        else
        {
            // Fall back to creating a PdfName from the string
            string colorSpace = inlineImage.ColorSpace;
            dict[new PdfName("ColorSpace")] = new PdfName(colorSpace);
        }

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
    internal PdfStream Stream => _stream;

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
            if (!_stream.Dictionary.TryGetValue(new PdfName("Decode"), out PdfObject obj))
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

        return _stream.GetDecodedData(_document?.Decryptor);
    }

    /// <summary>
    /// Checks if the image uses CCITTFaxDecode filter
    /// </summary>
    private bool IsCcittFilter()
    {
        if (!_stream.Dictionary.TryGetValue(PdfName.Filter, out PdfObject filterObj))
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
        if (_stream.Dictionary.TryGetValue(PdfName.DecodeParms, out PdfObject decodeParmObj) && decodeParmObj is PdfDictionary decodeParmDict)
        {
            foreach (KeyValuePair<PdfName, PdfObject> kvp in decodeParmDict)
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

        // Decrypt data if necessary, then apply CCITT filter
        byte[] data = _stream.Data;
        if (_document?.Decryptor is not null && _stream.IsIndirect)
        {
            data = _document.Decryptor.Decrypt(data, _stream.ObjectNumber, _stream.GenerationNumber);
        }
        var filter = new CcittFaxDecodeFilter();
        return filter.Decode(data, decodeParams);
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
    internal static bool IsImageXObject(PdfStream stream)
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

        // Extract base color space (index 1) - keep as PdfObject for now
        PdfObject baseObj = csArray[1];
        if (baseObj is PdfIndirectReference baseRef && _document is not null)
            baseObj = _document.ResolveReference(baseRef);

        // We'll set baseColorSpace after checking for ICC transformation
        string? resolvedBaseColorSpace = null;

        if (baseObj is PdfName baseName)
        {
            resolvedBaseColorSpace = baseName.Value;
        }
        else if (baseObj is PdfArray { Count: > 0 } baseArray && baseArray[0] is PdfName baseTypeName)
        {
            // Handle ICCBased base color space - determine device equivalent but DON'T set it yet
            if (baseTypeName.Value == "ICCBased" && baseArray.Count >= 2)
            {
                // Get the ICC stream to determine actual component count
                PdfObject? iccStreamObj = baseArray[1];
                if (iccStreamObj is PdfIndirectReference iccRef && _document is not null)
                    iccStreamObj = _document.ResolveReference(iccRef);

                if (iccStreamObj is PdfStream iccStream)
                {
                    // Get /N (number of components): 1=Gray, 3=RGB, 4=CMYK
                    if (iccStream.Dictionary.TryGetValue(new PdfName("N"), out PdfObject nObj) && nObj is PdfInteger nInt)
                    {
                        int numComponents = nInt.Value;
                        // Map to device color space based on component count
                        resolvedBaseColorSpace = numComponents switch
                        {
                            1 => "DeviceGray",
                            3 => "DeviceRGB",
                            4 => "DeviceCMYK",
                            _ => "DeviceRGB"
                        };
                    }
                    else
                    {
                        // No /N found, default to RGB
                        resolvedBaseColorSpace = "DeviceRGB";
                    }
                }
                else
                {
                    resolvedBaseColorSpace = "DeviceRGB";
                }
            }
            else
            {
                // Other array-based color spaces
                resolvedBaseColorSpace = baseTypeName.Value;
            }
        }
        else
        {
            resolvedBaseColorSpace = "DeviceRGB";
        }

        // Set baseColorSpace for output parameter (will be updated if we transform ICC palette)
        baseColorSpace = resolvedBaseColorSpace;

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
        PdfObject? lookupObj = csArray[3];

        // Debug: Log what csArray[3] is before resolution
        if (lookupObj is PdfIndirectReference lookupRef0)
        {
            PdfLogger.Log(LogCategory.Images, $"INDEXED csArray[3]: indirect reference R{lookupRef0.ObjectNumber}");
        }
        else if (lookupObj is PdfString lookupStr0)
        {
            PdfLogger.Log(LogCategory.Images, $"INDEXED csArray[3]: inline PdfString len={lookupStr0.Bytes.Length}");
        }
        else
        {
            string type0 = lookupObj?.GetType().Name ?? "null";
            PdfLogger.Log(LogCategory.Images, $"INDEXED csArray[3]: type={type0}");
        }

        // Resolve indirect reference to lookup table
        if (lookupObj is PdfIndirectReference lookupRef && _document is not null)
        {
            lookupObj = _document.ResolveReference(lookupRef);
            // Debug: Log what it resolved to
            if (lookupObj is not null)
            {
                string resolvedType = lookupObj.GetType().Name;
                int? objNum = lookupObj.ObjectNumber;
                PdfLogger.Log(LogCategory.Images, $"INDEXED RESOLVED: R{lookupRef.ObjectNumber} → {resolvedType} (obj #{objNum})");
            }
        }

        // Get palette data
        string lookupType = lookupObj?.GetType().Name ?? "null";
        PdfLogger.Log(LogCategory.Images, $"INDEXED LOOKUP: type={lookupType}");

        byte[]? paletteData = lookupObj switch
        {
            PdfString lookupString => lookupString.Bytes,
            PdfStream lookupStream => lookupStream.GetDecodedData(_document?.Decryptor),
            _ => null
        };

        // Debug: Log raw hex bytes of palette data
        if (paletteData is not null && paletteData.Length >= 12)
        {
            string hex = BitConverter.ToString(paletteData, 0, Math.Min(20, paletteData.Length)).Replace("-", " ");
            PdfLogger.Log(LogCategory.Images, $"INDEXED RAW BYTES: {hex}");
        }

        // Transform ICC palette colors to device RGB BEFORE setting baseColorSpace
        // Check if base is ICCBased and transform the palette
        if (baseObj is PdfArray { Count: > 0 } baseColorArray &&
            baseColorArray[0] is PdfName baseColorTypeName &&
            baseColorTypeName.Value == "ICCBased" &&
            baseColorArray.Count >= 2 &&
            paletteData is not null)
        {
            // Debug: Log BEFORE transformation
            if (paletteData.Length >= 12)
            {
                var palette0 = $"RGB({paletteData[0]}, {paletteData[1]}, {paletteData[2]})";
                var palette1 = $"RGB({paletteData[3]}, {paletteData[4]}, {paletteData[5]})";
                var palette2 = $"RGB({paletteData[6]}, {paletteData[7]}, {paletteData[8]})";
                var palette3 = $"RGB({paletteData[9]}, {paletteData[10]}, {paletteData[11]})";
                PdfLogger.Log(LogCategory.Images, $"INDEXED PALETTE (raw ICC): hival={hival}, palette[0]={palette0}, [1]={palette1}, [2]={palette2}, [3]={palette3}");
            }

            // Get the ICC stream for transformation
            PdfObject? iccStreamObj = baseColorArray[1];
            if (iccStreamObj is PdfIndirectReference iccRef2 && _document is not null)
                iccStreamObj = _document.ResolveReference(iccRef2);

            if (iccStreamObj is PdfStream iccStream2)
            {
                paletteData = TransformIccPalette(iccStream2, paletteData, resolvedBaseColorSpace ?? "DeviceRGB");

                // Debug: Log AFTER transformation
                if (paletteData.Length >= 12)
                {
                    var palette0t = $"RGB({paletteData[0]}, {paletteData[1]}, {paletteData[2]})";
                    var palette1t = $"RGB({paletteData[3]}, {paletteData[4]}, {paletteData[5]})";
                    var palette2t = $"RGB({paletteData[6]}, {paletteData[7]}, {paletteData[8]})";
                    var palette3t = $"RGB({paletteData[9]}, {paletteData[10]}, {paletteData[11]})";
                    PdfLogger.Log(LogCategory.Images, $"INDEXED PALETTE (transformed): palette[0]={palette0t}, [1]={palette1t}, [2]={palette2t}, [3]={palette3t}");
                }
            }
        }
        else if (paletteData is not null && paletteData.Length >= 12)
        {
            // Debug: Log for non-ICC palettes
            var palette0 = $"RGB({paletteData[0]}, {paletteData[1]}, {paletteData[2]})";
            var palette1 = $"RGB({paletteData[3]}, {paletteData[4]}, {paletteData[5]})";
            var palette2 = $"RGB({paletteData[6]}, {paletteData[7]}, {paletteData[8]})";
            var palette3 = $"RGB({paletteData[9]}, {paletteData[10]}, {paletteData[11]})";
            PdfLogger.Log(LogCategory.Images, $"INDEXED PALETTE (non-ICC): baseCS={baseColorSpace}, hival={hival}, palette[0]={palette0}, [1]={palette1}, [2]={palette2}, [3]={palette3}");
        }

        return paletteData;
    }

    /// <summary>
    /// Transforms palette bytes from ICC color space to device RGB using sRGB conversion
    /// </summary>
    private byte[] TransformIccPalette(PdfStream iccStream, byte[] paletteBytes, string targetColorSpace)
    {
        try
        {
            // Get number of components from ICC profile
            int numComponents = 3; // Default to RGB
            if (iccStream.Dictionary.TryGetValue(new PdfName("N"), out PdfObject nObj) && nObj is PdfInteger nInt)
            {
                numComponents = nInt.Value;
            }

            // Only handle 3-component (RGB) ICC profiles for now
            if (numComponents != 3 || paletteBytes.Length % 3 != 0)
            {
                PdfLogger.Log(LogCategory.Images, $"ICC TRANSFORM SKIPPED: numComponents={numComponents}, palette length={paletteBytes.Length}");
                return paletteBytes;
            }

            int numColors = paletteBytes.Length / 3;
            var transformedBytes = new byte[paletteBytes.Length];
            var converter = new ColorSpaceConverter();

            PdfLogger.Log(LogCategory.Images, $"ICC TRANSFORM: Starting transformation of {numColors} palette colors");

            // Transform each RGB triple
            for (int i = 0; i < numColors; i++)
            {
                int offset = i * 3;

                // Read ICC color values (normalized to 0-1 range)
                float r = paletteBytes[offset] / 255f;
                float g = paletteBytes[offset + 1] / 255f;
                float b = paletteBytes[offset + 2] / 255f;

                // Create an sRGB color (ICC sRGB uses sRGB primaries and gamma)
                var iccColor = new Rgb(r, g, b);

                // Convert through CIE XYZ to ensure proper color space transformation
                // sRGB -> XYZ -> sRGB ensures gamma correction is applied
                var xyzColor = converter.ToCieXyz(iccColor);
                var deviceColor = converter.ToRgb(xyzColor);

                // Convert back to 0-255 range
                transformedBytes[offset] = (byte)Math.Clamp((int)(deviceColor.R * 255f + 0.5f), 0, 255);
                transformedBytes[offset + 1] = (byte)Math.Clamp((int)(deviceColor.G * 255f + 0.5f), 0, 255);
                transformedBytes[offset + 2] = (byte)Math.Clamp((int)(deviceColor.B * 255f + 0.5f), 0, 255);
            }

            PdfLogger.Log(LogCategory.Images, "ICC TRANSFORM: Completed transformation");
            return transformedBytes;
        }
        catch (Exception ex)
        {
            PdfLogger.Log(LogCategory.Images, $"ICC TRANSFORM ERROR: {ex.Message}");
            return paletteBytes; // Return untransformed on error
        }
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
