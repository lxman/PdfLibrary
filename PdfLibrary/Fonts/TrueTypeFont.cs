using Logging;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Structure;

namespace PdfLibrary.Fonts;

/// <summary>
/// Represents a TrueType font (ISO 32000-1:2008 section 9.6.3)
/// </summary>
internal class TrueTypeFont : PdfFont
{
    private double[]? _widths;
    private double _defaultWidth;
    private EmbeddedFontMetrics? _embeddedMetrics;
    private bool _metricsLoaded;

    public TrueTypeFont(PdfDictionary dictionary, PdfDocument? document = null)
        : base(dictionary, document)
    {
        LoadEncoding();
        LoadToUnicodeCMap();
        LoadWidths();
    }

    internal override PdfFontType FontType => PdfFontType.TrueType;

    /// <summary>
    /// Returns the font descriptor for this font (for subsetting).
    /// </summary>
    internal PdfFontDescriptor? Descriptor => GetDescriptor();

    public override double GetCharacterWidth(int charCode)
    {
        // Try to use embedded font metrics first for more accurate widths
        EmbeddedFontMetrics? embeddedMetrics = GetEmbeddedMetrics();
        if (embeddedMetrics is { IsValid: true })
        {
            ushort glyphWidth = embeddedMetrics.GetCharacterAdvanceWidth((ushort)charCode);
            if (glyphWidth > 0)
            {
                // Scale embedded width to PDF's 1000-unit coordinate system
                // Embedded font may have different UnitsPerEm (e.g., 2048)
                double scaledWidth = glyphWidth * 1000.0 / embeddedMetrics.UnitsPerEm;
                PdfLogger.Log(LogCategory.Text, $"  [WIDTH] charCode={charCode} -> embedded: rawWidth={glyphWidth}, unitsPerEm={embeddedMetrics.UnitsPerEm}, scaled={scaledWidth:F1}");
                return scaledWidth;
            }
        }

        // Fallback to widths array from PDF
        if (_widths is not null && charCode >= FirstChar && charCode <= LastChar)
        {
            int index = charCode - FirstChar;
            if (index >= 0 && index < _widths.Length)
            {
                PdfLogger.Log(LogCategory.Text, $"  [WIDTH] charCode={charCode} -> PDF widths array: {_widths[index]:F1}");
                return _widths[index];
            }
        }

        // Non-embedded simple font that omits /Widths: lay the text out with the Standard-14 AFM
        // metrics of the base font this dict names (ISO 32000-1 §9.6.2.1). Acrobat PDFWriter emits
        // bare /TimesNewRoman, /Arial, /CourierNew dicts (no /Widths, no /FontDescriptor) that rely
        // on this. Without it every glyph falls through to the 500-unit default below, collapsing all
        // advances to one width — gaps after narrow glyphs (i/l/f/.), overlap on wide ones (m/W).
        // Returns null for any non-Standard-14 base font, so other fonts are unaffected.
        double? afmWidth = Standard14Metrics.WidthByName(BaseFont, Encoding?.GetGlyphName(charCode))
                           ?? Standard14Metrics.WidthByCode(BaseFont, charCode);
        if (afmWidth.HasValue)
            return afmWidth.Value;

        // Try to get from font descriptor
        PdfFontDescriptor? descriptor = GetDescriptor();
        if (descriptor is null) return _defaultWidth > 0
            ? _defaultWidth
            : 500;
        if (descriptor.MissingWidth > 0)
            return descriptor.MissingWidth;
        if (descriptor.AvgWidth > 0)
            return descriptor.AvgWidth;

        // Last resort: use default width
        return _defaultWidth > 0 ? _defaultWidth : 500;
    }

    internal override EmbeddedFontMetrics? GetEmbeddedMetrics()
    {
        if (_metricsLoaded)
            return _embeddedMetrics;

        _metricsLoaded = true;

        try
        {
            // Get font descriptor
            PdfFontDescriptor? descriptor = GetDescriptor();
            if (descriptor is null)
                return null;

            // Try to get embedded TrueType data (FontFile2)
            byte[]? fontData = descriptor.GetFontFile2();
            if (fontData is null)
            {
                // Try OpenType/CFF (FontFile3)
                fontData = descriptor.GetFontFile3();
            }

            if (fontData is null)
                return null;

            // Parse embedded font metrics
            _embeddedMetrics = new EmbeddedFontMetrics(fontData);
            return _embeddedMetrics;
        }
        catch
        {
            // If parsing fails, return null and fall back to PDF widths
            return null;
        }
    }

    private void LoadEncoding()
    {
        if (!_dictionary.TryGetValue(new PdfName("Encoding"), out PdfObject? obj))
        {
            // TrueType fonts without encoding use WinAnsiEncoding by default
            Encoding = PdfFontEncoding.GetStandardEncoding("WinAnsiEncoding");
            return;
        }

        // Resolve indirect reference
        if (obj is PdfIndirectReference reference && _document is not null)
            obj = _document.ResolveReference(reference);

        Encoding = obj switch
        {
            // Named encoding
            PdfName encodingName => PdfFontEncoding.GetStandardEncoding(encodingName.Value),
            // Custom encoding dictionary
            PdfDictionary encodingDict => PdfFontEncoding.FromDictionary(encodingDict,
                PdfFontEncoding.GetStandardEncoding("WinAnsiEncoding")),
            _ => Encoding
        };
    }

    private void LoadWidths()
    {
        // Get widths array
        if (_dictionary.TryGetValue(new PdfName("Widths"), out PdfObject? obj))
        {
            if (obj is PdfIndirectReference reference && _document is not null)
                obj = _document.ResolveReference(reference);

            if (obj is PdfArray widthsArray)
            {
                _widths = new double[widthsArray.Count];
                for (var i = 0; i < widthsArray.Count; i++)
                {
                    _widths[i] = widthsArray[i].ToDouble();
                }
            }
        }

        // Get default width from font descriptor
        PdfFontDescriptor? descriptor = GetDescriptor();
        if (descriptor is not null)
        {
            _defaultWidth = descriptor.MissingWidth > 0 ? descriptor.MissingWidth : descriptor.AvgWidth;
        }
    }

}
