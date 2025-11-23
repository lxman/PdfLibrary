using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Builder;

/// <summary>
/// Fluent builder for creating PDF documents
/// </summary>
public class PdfDocumentBuilder
{
    private readonly List<PdfPageBuilder> _pages = [];
    private readonly PdfMetadataBuilder _metadata = new();
    private PdfAcroFormBuilder? _acroForm;
    private readonly Dictionary<string, CustomFontInfo> _customFonts = new();

    /// <summary>
    /// Create a new PDF document builder
    /// </summary>
    public static PdfDocumentBuilder Create() => new();

    /// <summary>
    /// Set document metadata
    /// </summary>
    public PdfDocumentBuilder WithMetadata(Action<PdfMetadataBuilder> configure)
    {
        configure(_metadata);
        return this;
    }

    /// <summary>
    /// Add a new page to the document
    /// </summary>
    public PdfDocumentBuilder AddPage(Action<PdfPageBuilder> configure)
    {
        var page = new PdfPageBuilder(PdfPageSize.Letter);
        configure(page);
        _pages.Add(page);
        return this;
    }

    /// <summary>
    /// Add a new page with the specified size
    /// </summary>
    public PdfDocumentBuilder AddPage(PdfSize size, Action<PdfPageBuilder> configure)
    {
        var page = new PdfPageBuilder(size);
        configure(page);
        _pages.Add(page);
        return this;
    }

    /// <summary>
    /// Configure the document's AcroForm
    /// </summary>
    public PdfDocumentBuilder WithAcroForm(Action<PdfAcroFormBuilder> configure)
    {
        _acroForm ??= new PdfAcroFormBuilder();
        configure(_acroForm);
        return this;
    }

    /// <summary>
    /// Load a custom TrueType or OpenType font from a file
    /// </summary>
    /// <param name="fontPath">Path to the font file (.ttf, .otf, .ttc, .woff, .woff2)</param>
    /// <param name="fontAlias">Alias name to use when referencing this font (e.g., "MyFont")</param>
    /// <returns>The document builder for chaining</returns>
    /// <exception cref="FileNotFoundException">If the font file doesn't exist</exception>
    /// <exception cref="InvalidDataException">If the font file cannot be parsed</exception>
    public PdfDocumentBuilder LoadFont(string fontPath, string fontAlias)
    {
        if (!File.Exists(fontPath))
            throw new FileNotFoundException($"Font file not found: {fontPath}");

        if (_customFonts.ContainsKey(fontAlias))
            throw new ArgumentException($"A font with alias '{fontAlias}' is already loaded");

        // Read the font file
        byte[] fontData = File.ReadAllBytes(fontPath);

        // Parse font metrics
        EmbeddedFontMetrics metrics;
        try
        {
            metrics = new EmbeddedFontMetrics(fontData);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Failed to parse font file '{fontPath}': {ex.Message}", ex);
        }

        if (!metrics.IsValid)
            throw new InvalidDataException($"Font file '{fontPath}' does not contain valid font data");

        // Store font info
        _customFonts[fontAlias] = new CustomFontInfo
        {
            Alias = fontAlias,
            FilePath = fontPath,
            FontData = fontData,
            Metrics = metrics,
            PostScriptName = metrics.PostScriptName ?? fontAlias,
            FamilyName = metrics.FamilyName ?? fontAlias
        };

        return this;
    }

    /// <summary>
    /// Get the list of pages
    /// </summary>
    internal IReadOnlyList<PdfPageBuilder> Pages => _pages;

    /// <summary>
    /// Get the metadata builder
    /// </summary>
    internal PdfMetadataBuilder Metadata => _metadata;

    /// <summary>
    /// Get the AcroForm builder
    /// </summary>
    internal PdfAcroFormBuilder? AcroForm => _acroForm;

    /// <summary>
    /// Get the custom fonts dictionary
    /// </summary>
    internal IReadOnlyDictionary<string, CustomFontInfo> CustomFonts => _customFonts;

    /// <summary>
    /// Build and save the document to a file
    /// </summary>
    public void Save(string filePath)
    {
        var writer = new PdfDocumentWriter();
        writer.Write(this, filePath);
    }

    /// <summary>
    /// Build and save the document to a stream
    /// </summary>
    public void Save(Stream stream)
    {
        var writer = new PdfDocumentWriter();
        writer.Write(this, stream);
    }

    /// <summary>
    /// Build and return the document as a byte array
    /// </summary>
    public byte[] ToByteArray()
    {
        using var stream = new MemoryStream();
        Save(stream);
        return stream.ToArray();
    }
}