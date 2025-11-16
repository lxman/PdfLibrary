using System.Text;
using PdfLibrary.Filters;

namespace PdfLibrary.Core.Primitives;

/// <summary>
/// Represents a PDF stream object (ISO 32000-1:2008 section 7.3.8)
/// A stream consists of a dictionary followed by binary data
/// Format: dictionary\nstream\n...data...endstream
/// </summary>
public sealed class PdfStream : PdfObject
{
    private byte[] _data;

    public PdfStream(PdfDictionary dictionary, byte[] data)
    {
        Dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        _data = data ?? throw new ArgumentNullException(nameof(data));

        // Set the Length entry in the dictionary
        Dictionary[PdfName.Length] = new PdfInteger(data.Length);
    }

    public PdfStream(byte[] data)
        : this(new PdfDictionary(), data)
    {
    }

    public override PdfObjectType Type => PdfObjectType.Stream;

    /// <summary>
    /// Gets the stream dictionary
    /// </summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>
    /// Gets or sets the raw stream data (encoded/compressed)
    /// </summary>
    public byte[] Data
    {
        get => (byte[])_data.Clone();
        set
        {
            _data = value ?? throw new ArgumentNullException(nameof(value));
            Dictionary[PdfName.Length] = new PdfInteger(value.Length);
        }
    }

    /// <summary>
    /// Gets the length of the stream data
    /// </summary>
    public int Length => _data.Length;

    public override string ToPdfString()
    {
        var sb = new StringBuilder();

        // Write dictionary
        sb.Append(Dictionary.ToPdfString());
        sb.Append('\n');

        // Write stream keyword
        sb.Append("stream\n");

        // For string representation, we'll include a marker instead of binary data
        sb.Append($"<< {_data.Length} bytes of binary data >>");

        // Write endstream keyword
        sb.Append("\nendstream");

        return sb.ToString();
    }

    /// <summary>
    /// Writes the stream object to a byte array in PDF format
    /// </summary>
    public byte[] ToBytes()
    {
        // Calculate approximate size
        string dictStr = Dictionary.ToPdfString();
        int totalSize = Encoding.ASCII.GetByteCount(dictStr) +
                       20 + // "stream\n" + "endstream\n"
                       _data.Length;

        using var ms = new MemoryStream(totalSize);
        using var writer = new BinaryWriter(ms);

        // Write dictionary
        writer.Write(Encoding.ASCII.GetBytes(dictStr));
        writer.Write((byte)'\n');

        // Write stream keyword
        writer.Write("stream\n"u8.ToArray());

        // Write binary data
        writer.Write(_data);

        // Write endstream keyword
        writer.Write("\nendstream"u8.ToArray());

        return ms.ToArray();
    }

    /// <summary>
    /// Decodes the stream data using the filters specified in the stream dictionary
    /// </summary>
    public byte[] GetDecodedData()
    {
        byte[] data = _data;

        // Check if stream has filters
        if (!Dictionary.TryGetValue(PdfName.Filter, out PdfObject filterObj))
            return data; // No filters, return raw data

        // Get decode parameters if present
        Dictionary<string, object>? decodeParams = null;
        if (Dictionary.TryGetValue(PdfName.DecodeParms, out PdfObject decodeParmObj) && decodeParmObj is PdfDictionary decodeParmDict)
        {
            decodeParams = ConvertToDecodeParams(decodeParmDict);
        }

        switch (filterObj)
        {
            // Handle a single filter
            case PdfName filterName:
                return ApplyFilter(data, filterName.Value, decodeParams);
            // Handle array of filters (applied in sequence)
            case PdfArray filterArray:
            {
                for (var i = 0; i < filterArray.Count; i++)
                {
                    if (filterArray[i] is not PdfName name) continue;
                    // Get the corresponding decode params if it's an array
                    Dictionary<string, object>? currentParams = null;
                    if (Dictionary.TryGetValue(PdfName.DecodeParms, out PdfObject dpObj) && dpObj is PdfArray dpArray && i < dpArray.Count)
                    {
                        if (dpArray[i] is PdfDictionary dpDict)
                            currentParams = ConvertToDecodeParams(dpDict);
                    }

                    data = ApplyFilter(data, name.Value, currentParams ?? decodeParams);
                }

                break;
            }
        }

        return data;
    }

    /// <summary>
    /// Applies a filter to decode data
    /// </summary>
    private byte[] ApplyFilter(byte[] data, string filterName, Dictionary<string, object>? decodeParams)
    {
        IStreamFilter? filter = StreamFilterFactory.CreateFilter(filterName);
        return filter == null
            ? throw new NotSupportedException($"Unsupported filter: {filterName}")
            : filter.Decode(data, decodeParams);
    }

    /// <summary>
    /// Converts a PDF dictionary to decode parameters
    /// </summary>
    private Dictionary<string, object> ConvertToDecodeParams(PdfDictionary dict)
    {
        var result = new Dictionary<string, object>();

        foreach (KeyValuePair<PdfName, PdfObject> kvp in dict)
        {
            string key = kvp.Key.Value;
            object? value = kvp.Value switch
            {
                PdfInteger intVal => intVal.Value,
                PdfReal realVal => realVal.Value,
                PdfBoolean boolVal => (bool)boolVal,
                PdfName nameVal => nameVal.Value,
                _ => null
            };

            if (value != null)
                result[key] = value;
        }

        return result;
    }

    /// <summary>
    /// Encodes data and sets it as the stream data with the specified filter
    /// </summary>
    public void SetEncodedData(byte[] decodedData, string filterName)
    {
        IStreamFilter? filter = StreamFilterFactory.CreateFilter(filterName);
        if (filter == null)
            throw new NotSupportedException($"Unsupported filter: {filterName}");

        _data = filter.Encode(decodedData);
        Dictionary[PdfName.Filter] = new PdfName(filterName);
        Dictionary[PdfName.Length] = new PdfInteger(_data.Length);
    }
}
