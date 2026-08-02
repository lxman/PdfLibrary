using Logging;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Structure;

namespace PdfLibrary.Fonts;

/// <summary>
/// Represents a Type 0 (composite/CID) font (ISO 32000-1:2008 section 9.7)
/// Used for fonts with large character sets (e.g., CJK fonts)
/// </summary>
internal class Type0Font : PdfFont
{
    private PdfFont? _descendantFont;
    private EmbeddedFontExtractor? _embeddedFont;
    private EmbeddedFontMetrics? _embeddedMetrics;
    private bool _metricsLoaded;

    // B-1 registry CID→Unicode context (lazy; armed only when the descendant declares Registry
    // "Adobe" with a bundled ordering). _ordering non-null == the path is armed.
    private bool _registryContextLoaded;
    private string? _ordering;
    private CidCMap? _encodingCMap;     // parsed embedded /Encoding stream (stream-encoding case)
    private bool _identityEncoding;     // Identity-H / Identity-V
    private bool _ucs2Encoding;         // Uni*-UCS2-*: the code IS a UCS-2 value

    public Type0Font(PdfDictionary dictionary, PdfDocument? document = null)
        : base(dictionary, document)
    {
        LoadToUnicodeCMap(); // ToUnicode is critical for Type 0 fonts
        LoadDescendantFont();
        LoadEmbeddedFont();  // Load embedded font for glyph name fallback
    }

    internal override PdfFontType FontType => PdfFontType.Type0;

    /// <summary>
    /// Returns the raw PdfDictionary of the descendant CIDFont (for subsetting).
    /// </summary>
    internal PdfDictionary? DescendantCidFontDictionary =>
        (_descendantFont as CidFont)?.RawDictionary;

    /// <summary>
    /// Returns the font descriptor from the descendant CIDFont (for subsetting).
    /// </summary>
    internal PdfFontDescriptor? DescendantDescriptor =>
        _descendantFont?.GetDescriptor();

    /// <summary>
    /// Returns the /Encoding value (e.g. "Identity-H") from the Type0 dictionary.
    /// </summary>
    internal string? EncodingName
    {
        get
        {
            if (!_dictionary.TryGetValue(new PdfName("Encoding"), out PdfObject? obj))
                return null;
            return obj is PdfName n ? n.Value : null;
        }
    }

    /// <summary>
    /// Gets the descendant CIDFont
    /// </summary>
    public PdfFont? DescendantFont => _descendantFont;

    public override double GetCharacterWidth(int charCode)
    {
        // Delegate to descendant font
        if (_descendantFont is not null)
            return _descendantFont.GetCharacterWidth(charCode);

        return 1000; // CID fonts typically use 1000 as default
    }

    public override string DecodeCharacter(int charCode)
    {
        // 4-step fallback chain for Type 0 fonts:
        // 1. Try ToUnicode CMap (standard, correct approach)
        string? unicode = ToUnicode?.Lookup(charCode);
        if (unicode is not null)
            return unicode;

        // 2. Registered Adobe CID collection (B-1): code→CID (embedded /Encoding CMap, Identity,
        //    or UCS2 shortcut) → CID→Unicode (bundled Adobe-<Ordering>-UCS2 tables).
        string? registryUnicode = DecodeViaRegistry(charCode);
        if (registryUnicode is not null)
            return registryUnicode;

        // 3. Try embedded font glyph name → Unicode (handles broken PDFs)
        if (_embeddedFont is not { IsValid: true }) return char.ConvertFromUtf32(charCode);
        string? unicodeFromGlyph = _embeddedFont.GetUnicodeFromGlyphName(charCode);
        if (unicodeFromGlyph is null) return char.ConvertFromUtf32(charCode);
        // Log fallback usage for debugging
        PdfLogger.Log(LogCategory.Text,
            $"Type0Font: Using embedded font fallback for charCode 0x{charCode:X4} → '{unicodeFromGlyph}'");
        return unicodeFromGlyph;

        // 4. Fall back to character code as Unicode (last resort)
    }

    // B-1 registry CID→Unicode context (lazy; armed only when the descendant declares Registry
    // "Adobe" with a bundled ordering).
    private void EnsureRegistryContext()
    {
        if (_registryContextLoaded) return;
        _registryContextLoaded = true;

        if ((_descendantFont as CidFont)?.RawDictionary is not { } cidDict) return;
        PdfObject? csiObj = cidDict.Get("CIDSystemInfo");
        if (csiObj is PdfIndirectReference csiRef && _document is not null)
            csiObj = _document.ResolveReference(csiRef);
        if (csiObj is not PdfDictionary csi) return;
        if (Resolve(csi.Get("Registry")) is not PdfString { Value: "Adobe" }) return;
        if (Resolve(csi.Get("Ordering")) is not PdfString ord
            || !AdobeCidToUnicode.IsSupportedOrdering(ord.Value)) return;

        if (!_dictionary.TryGetValue(new PdfName("Encoding"), out PdfObject? enc)) return;
        if (enc is PdfIndirectReference encRef && _document is not null)
            enc = _document.ResolveReference(encRef);
        switch (enc)
        {
            case PdfName { Value: "Identity-H" or "Identity-V" }:
                _identityEncoding = true;
                break;
            case PdfName n when n.Value.StartsWith("Uni", StringComparison.Ordinal)
                                && n.Value.Contains("-UCS2-", StringComparison.Ordinal):
                _ucs2Encoding = true;
                break;
            case PdfStream stream:
                try { _encodingCMap = CidCMap.Parse(stream.GetDecodedData(_document?.Decryptor)); }
                catch { return; }   // corrupt/undecodable /Encoding stream: leave the path unarmed
                break;
            default:
                return;   // predefined non-Identity, non-UCS2 name: not bundled (spec non-goal)
        }
        _ordering = ord.Value;
        PdfLogger.Log(LogCategory.Text,
            $"Type0Font: registry CID→Unicode path active (Adobe-{_ordering})");
    }

    private PdfObject? Resolve(PdfObject? obj) =>
        obj is PdfIndirectReference r && _document is not null ? _document.ResolveReference(r) : obj;

    // B-1 step 2 of the decode chain: registered-collection mapping. Null = fall through.
    private string? DecodeViaRegistry(int charCode)
    {
        EnsureRegistryContext();
        if (_ordering is null) return null;
        if (_ucs2Encoding)
        {
            // The character code IS a UCS-2 value. Exclude NUL and surrogate halves.
            if (charCode is <= 0 or > 0xFFFF || (charCode & 0xF800) == 0xD800) return null;
            return ((char)charCode).ToString();
        }
        int? cid = _identityEncoding ? charCode : _encodingCMap?.MapCodeToCid(charCode);
        return cid is null ? null : AdobeCidToUnicode.Lookup(_ordering, cid.Value);
    }

    internal override EmbeddedFontMetrics? GetEmbeddedMetrics()
    {
        if (_metricsLoaded)
            return _embeddedMetrics;

        _metricsLoaded = true;

        try
        {
            // Get the font descriptor from descendant CIDFont or Type0 font
            PdfFontDescriptor? descriptor = _descendantFont?.GetDescriptor() ?? GetDescriptor();
            if (descriptor is null)
                return null;

            // Try to get embedded TrueType data (FontFile2)
            // Try OpenType/CFF (FontFile3)
            byte[]? fontData = descriptor.GetFontFile2() ?? descriptor.GetFontFile3();

            if (fontData is not null)
            {
                // Parse embedded font metrics (TrueType or CFF)
                _embeddedMetrics = new EmbeddedFontMetrics(fontData);
                return _embeddedMetrics;
            }

            // Try Type1 font (FontFile with Length1/Length2/Length3 parameters)
            (byte[] data, int length1, int length2, int length3)? type1Data = descriptor.GetFontFileWithLengths();
            if (type1Data is not null)
            {
                (byte[] data, int length1, int length2, int length3) = type1Data.Value;
                _embeddedMetrics = new EmbeddedFontMetrics(data, length1, length2, length3);
                if (_embeddedMetrics.IsValid)
                    return _embeddedMetrics;
            }

            return null;
        }
        catch
        {
            // If parsing fails, return null and fall back to CID widths
            return null;
        }
    }

    /// <summary>
    /// Load embedded font for glyph name fallback
    /// Handles broken PDFs with missing ToUnicode mappings
    /// </summary>
    private void LoadEmbeddedFont()
    {
        // Get font descriptor from descendant CIDFont
        // Try to get the descriptor from Type0 font dict (rare but valid)
        PdfFontDescriptor? descriptor = _descendantFont?.GetDescriptor() ?? GetDescriptor();

        if (descriptor is not null)
        {
            _embeddedFont = new EmbeddedFontExtractor(descriptor);
        }
    }

    private void LoadDescendantFont()
    {
        if (!_dictionary.TryGetValue(new PdfName("DescendantFonts"), out PdfObject? obj))
            return;

        // Resolve indirect reference
        if (obj is PdfIndirectReference reference && _document is not null)
            obj = _document.ResolveReference(reference);

        // DescendantFonts is an array with a single CIDFont
        if (obj is not PdfArray { Count: > 0 } array) return;
        PdfObject? descendantObj = array[0];

        if (descendantObj is PdfIndirectReference descRef && _document is not null)
            descendantObj = _document.ResolveReference(descRef);

        if (descendantObj is PdfDictionary descendantDict)
        {
            _descendantFont = new CidFont(descendantDict, _document);
        }
    }
}