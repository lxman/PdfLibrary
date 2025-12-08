using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Fonts;

/// <summary>
/// Represents a CIDFont (Character Identifier font)
/// Used as a descendant font of Type 0 fonts
/// </summary>
internal class CidFont : PdfFont
{
    private double _defaultWidth = 1000;
    private Dictionary<int, double>? _widths;
    private Dictionary<int, int>? _cidToGidMap;
    private bool _isIdentityMapping;

    public CidFont(PdfDictionary dictionary, PdfDocument? document = null)
        : base(dictionary, document)
    {
        LoadWidths();
        LoadCidToGidMap();
    }

    /// <summary>
    /// Maps a CID (Character ID) to a GID (Glyph ID) for the embedded font
    /// </summary>
    public int MapCidToGid(int cid)
    {
        // Identity mapping: CID = GID (most common for subset fonts)
        if (_isIdentityMapping)
            return cid;

        // Look up in the mapping table
        if (_cidToGidMap is not null && _cidToGidMap.TryGetValue(cid, out int gid))
            return gid;

        // Default: assume identity mapping
        return cid;
    }

    private void LoadCidToGidMap()
    {
        if (!_dictionary.TryGetValue(new PdfName("CIDToGIDMap"), out PdfObject? mapObj))
        {
            // No mapping specified, assume identity
            _isIdentityMapping = true;
            return;
        }

        // Resolve indirect reference
        if (mapObj is PdfIndirectReference reference && _document is not null)
            mapObj = _document.ResolveReference(reference);

        switch (mapObj)
        {
            // Check for /Identity name
            case PdfName { Value: "Identity" }:
                _isIdentityMapping = true;
                return;
            // Parse stream containing the mapping
            case PdfStream stream:
            {
                byte[] data = stream.GetDecodedData(_document?.Decryptor);
                _cidToGidMap = new Dictionary<int, int>();

                // Each entry is 2 bytes (big-endian GID), indexed by CID
                for (var cid = 0; cid < data.Length / 2; cid++)
                {
                    int gid = (data[cid * 2] << 8) | data[cid * 2 + 1];
                    if (gid != 0)  // Only store non-zero mappings
                        _cidToGidMap[cid] = gid;
                }

                break;
            }
            default:
                // Unknown format, assume identity
                _isIdentityMapping = true;
                break;
        }
    }

    internal override PdfFontType FontType => PdfFontType.Type0;

    public override double GetCharacterWidth(int charCode)
    {
        if (_widths is not null && _widths.TryGetValue(charCode, out double width))
            return width;

        return _defaultWidth;
    }

    private void LoadWidths()
    {
        // Get default width (DW)
        if (_dictionary.TryGetValue(new PdfName("DW"), out PdfObject dwObj))
        {
            _defaultWidth = dwObj.ToDouble();
        }

        // Get width array (W)
        if (_dictionary.TryGetValue(new PdfName("W"), out PdfObject? wObj))
        {
            if (wObj is PdfIndirectReference reference && _document is not null)
                wObj = _document.ResolveReference(reference);

            if (wObj is PdfArray widthArray)
            {
                _widths = ParseWidthArray(widthArray);
            }
        }

        // Try to get from descriptor
        if (_widths is not null && _widths.Count != 0) return;
        PdfFontDescriptor? descriptor = GetDescriptor();
        if (descriptor is { MissingWidth: > 0 })
            _defaultWidth = descriptor.MissingWidth;
    }

    private static Dictionary<int, double> ParseWidthArray(PdfArray array)
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
                    widths[start + j] = widthList[j].ToDouble();
                }
                i++;
            }
            // Format 2: start_cid end_cid width
            else if (array[i] is PdfInteger endCid && i + 1 < array.Count)
            {
                int end = endCid.Value;
                var width = array[i + 1].ToDouble();

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

}