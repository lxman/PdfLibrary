using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Builder;

/// <summary>
/// Fluent builder for creating PDF documents
/// </summary>
public class PdfDocumentBuilder
{
    private readonly List<PdfPageBuilder> _pages = new();
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
    /// Add a new page with specified size
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

        // Read font file
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

/// <summary>
/// Builder for document metadata
/// </summary>
public class PdfMetadataBuilder
{
    internal string? Title { get; private set; }
    internal string? Author { get; private set; }
    internal string? Subject { get; private set; }
    internal string? Keywords { get; private set; }
    internal string? Creator { get; private set; }
    internal string? Producer { get; private set; }
    internal DateTime? CreationDate { get; private set; }
    internal DateTime? ModificationDate { get; private set; }

    public PdfMetadataBuilder SetTitle(string title)
    {
        Title = title;
        return this;
    }

    public PdfMetadataBuilder SetAuthor(string author)
    {
        Author = author;
        return this;
    }

    public PdfMetadataBuilder SetSubject(string subject)
    {
        Subject = subject;
        return this;
    }

    public PdfMetadataBuilder SetKeywords(string keywords)
    {
        Keywords = keywords;
        return this;
    }

    public PdfMetadataBuilder SetCreator(string creator)
    {
        Creator = creator;
        return this;
    }

    public PdfMetadataBuilder SetProducer(string producer)
    {
        Producer = producer;
        return this;
    }

    public PdfMetadataBuilder SetCreationDate(DateTime date)
    {
        CreationDate = date;
        return this;
    }

    public PdfMetadataBuilder SetModificationDate(DateTime date)
    {
        ModificationDate = date;
        return this;
    }
}

/// <summary>
/// Builder for document-level AcroForm settings
/// </summary>
public class PdfAcroFormBuilder
{
    internal string DefaultFont { get; private set; } = "Helvetica";
    internal double DefaultFontSize { get; private set; } = 10;
    internal bool NeedAppearances { get; private set; } = true;

    public PdfAcroFormBuilder SetDefaultFont(string fontName, double fontSize = 10)
    {
        DefaultFont = fontName;
        DefaultFontSize = fontSize;
        return this;
    }

    public PdfAcroFormBuilder SetNeedAppearances(bool value)
    {
        NeedAppearances = value;
        return this;
    }
}

/// <summary>
/// Information about a custom embedded font
/// </summary>
internal class CustomFontInfo
{
    /// <summary>
    /// Alias name used to reference this font in the document
    /// </summary>
    public required string Alias { get; init; }

    /// <summary>
    /// Path to the original font file
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Raw font file data
    /// </summary>
    public required byte[] FontData { get; init; }

    /// <summary>
    /// Parsed font metrics
    /// </summary>
    public required EmbeddedFontMetrics Metrics { get; init; }

    /// <summary>
    /// PostScript name from font
    /// </summary>
    public required string PostScriptName { get; init; }

    /// <summary>
    /// Font family name
    /// </summary>
    public required string FamilyName { get; init; }
}
