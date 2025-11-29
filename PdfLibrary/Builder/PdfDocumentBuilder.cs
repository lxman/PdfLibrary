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
    private PdfEncryptionSettings? _encryptionSettings;
    private readonly List<PdfLayer> _layers = [];
    private readonly List<PdfBookmark> _bookmarks = [];
    private readonly List<PdfPageLabelRange> _pageLabelRanges = [];

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
    /// Configure document encryption with passwords and permissions
    /// </summary>
    /// <param name="configure">Action to configure encryption settings</param>
    /// <returns>The document builder for chaining</returns>
    /// <example>
    /// <code>
    /// PdfDocumentBuilder.Create()
    ///     .WithEncryption(encryption => encryption
    ///         .WithUserPassword("viewpassword")
    ///         .WithOwnerPassword("adminpassword")
    ///         .WithMethod(PdfEncryptionMethod.Aes256)
    ///         .WithPermissions(PdfPermissionFlags.Print | PdfPermissionFlags.CopyContent))
    ///     .AddPage(page => page.Text("Secret content", 100, 700))
    ///     .Save("encrypted.pdf");
    /// </code>
    /// </example>
    public PdfDocumentBuilder WithEncryption(Action<PdfEncryptionSettings> configure)
    {
        _encryptionSettings = new PdfEncryptionSettings();
        configure(_encryptionSettings);
        return this;
    }

    /// <summary>
    /// Configure document encryption with a simple password (AES-256, all permissions)
    /// </summary>
    /// <param name="password">Password required to open the document</param>
    /// <returns>The document builder for chaining</returns>
    public PdfDocumentBuilder WithPassword(string password)
    {
        _encryptionSettings = new PdfEncryptionSettings()
            .WithUserPassword(password)
            .WithOwnerPassword(password)
            .WithMethod(PdfEncryptionMethod.Aes256)
            .AllowAll();
        return this;
    }

    /// <summary>
    /// Define a new layer (Optional Content Group) in the document
    /// </summary>
    /// <param name="name">Display name of the layer</param>
    /// <param name="layer">Output parameter containing the created layer reference</param>
    /// <returns>The document builder for chaining</returns>
    /// <example>
    /// <code>
    /// PdfDocumentBuilder.Create()
    ///     .DefineLayer("Background", out var background)
    ///     .DefineLayer("Annotations", out var annotations)
    ///     .AddPage(page => page
    ///         .Layer(background, content => content
    ///             .AddRectangle(0, 0, 612, 792, PdfColor.LightGray))
    ///         .Layer(annotations, content => content
    ///             .AddCircle(300, 400, 50).Stroke(PdfColor.Red)))
    ///     .Save("layered.pdf");
    /// </code>
    /// </example>
    public PdfDocumentBuilder DefineLayer(string name, out PdfLayer layer)
    {
        layer = new PdfLayer(name);
        _layers.Add(layer);
        return this;
    }

    /// <summary>
    /// Define a new layer with additional configuration options
    /// </summary>
    /// <param name="name">Display name of the layer</param>
    /// <param name="configure">Action to configure the layer properties</param>
    /// <returns>The document builder for chaining</returns>
    /// <example>
    /// <code>
    /// PdfDocumentBuilder.Create()
    ///     .DefineLayer("Watermark", layer => layer.Hidden().NeverPrint())
    ///     .AddPage(page => page.AddText("Hello", 100, 700))
    ///     .Save("document.pdf");
    /// </code>
    /// </example>
    public PdfDocumentBuilder DefineLayer(string name, Action<PdfLayerBuilder> configure)
    {
        var layer = new PdfLayer(name);
        var builder = new PdfLayerBuilder(layer);
        configure(builder);
        _layers.Add(layer);
        return this;
    }

    /// <summary>
    /// Define a new layer with configuration and return the layer reference
    /// </summary>
    /// <param name="name">Display name of the layer</param>
    /// <param name="configure">Action to configure the layer properties</param>
    /// <param name="layer">Output parameter containing the created layer reference</param>
    /// <returns>The document builder for chaining</returns>
    public PdfDocumentBuilder DefineLayer(string name, Action<PdfLayerBuilder> configure, out PdfLayer layer)
    {
        layer = new PdfLayer(name);
        var builder = new PdfLayerBuilder(layer);
        configure(builder);
        _layers.Add(layer);
        return this;
    }

    /// <summary>
    /// Add a bookmark (outline entry) to the document
    /// </summary>
    /// <param name="title">Display title of the bookmark</param>
    /// <param name="pageIndex">0-based page index the bookmark links to</param>
    /// <returns>The document builder for chaining</returns>
    /// <example>
    /// <code>
    /// PdfDocumentBuilder.Create()
    ///     .AddPage(page => page.AddText("Chapter 1", 100, 700))
    ///     .AddPage(page => page.AddText("Chapter 2", 100, 700))
    ///     .AddBookmark("Chapter 1", 0)
    ///     .AddBookmark("Chapter 2", 1)
    ///     .Save("document.pdf");
    /// </code>
    /// </example>
    public PdfDocumentBuilder AddBookmark(string title, int pageIndex)
    {
        var bookmark = new PdfBookmark(title);
        bookmark.Destination.PageIndex = pageIndex;
        _bookmarks.Add(bookmark);
        return this;
    }

    /// <summary>
    /// Add a bookmark with additional configuration
    /// </summary>
    /// <param name="title">Display title of the bookmark</param>
    /// <param name="configure">Action to configure the bookmark</param>
    /// <returns>The document builder for chaining</returns>
    /// <example>
    /// <code>
    /// PdfDocumentBuilder.Create()
    ///     .AddPage(page => page.AddText("Introduction", 100, 700))
    ///     .AddPage(page => page.AddText("Chapter 1", 100, 700))
    ///     .AddBookmark("Introduction", b => b.ToPage(0).FitWidth())
    ///     .AddBookmark("Chapter 1", b => b
    ///         .ToPage(1)
    ///         .Bold()
    ///         .WithColor(PdfColor.Blue)
    ///         .AddChild("Section 1.1", c => c.ToPage(1).AtPosition(100, 500))
    ///         .AddChild("Section 1.2", c => c.ToPage(1).AtPosition(100, 300)))
    ///     .Save("document.pdf");
    /// </code>
    /// </example>
    public PdfDocumentBuilder AddBookmark(string title, Action<PdfBookmarkBuilder> configure)
    {
        var bookmark = new PdfBookmark(title);
        var builder = new PdfBookmarkBuilder(bookmark);
        configure(builder);
        _bookmarks.Add(bookmark);
        return this;
    }

    /// <summary>
    /// Add a bookmark and get a reference to it
    /// </summary>
    /// <param name="title">Display title of the bookmark</param>
    /// <param name="bookmark">Output parameter containing the created bookmark</param>
    /// <param name="configure">Optional action to configure the bookmark</param>
    /// <returns>The document builder for chaining</returns>
    public PdfDocumentBuilder AddBookmark(string title, out PdfBookmark bookmark, Action<PdfBookmarkBuilder>? configure = null)
    {
        bookmark = new PdfBookmark(title);
        if (configure != null)
        {
            var builder = new PdfBookmarkBuilder(bookmark);
            configure(builder);
        }
        _bookmarks.Add(bookmark);
        return this;
    }

    /// <summary>
    /// Define page labels starting from a specific page
    /// </summary>
    /// <param name="startPageIndex">0-based page index where this labeling scheme starts</param>
    /// <param name="configure">Action to configure the page label style</param>
    /// <returns>The document builder for chaining</returns>
    /// <example>
    /// <code>
    /// PdfDocumentBuilder.Create()
    ///     .AddPage(page => page.AddText("Cover", 100, 700))
    ///     .AddPage(page => page.AddText("Preface", 100, 700))
    ///     .AddPage(page => page.AddText("Chapter 1", 100, 700))
    ///     .SetPageLabels(0, label => label.NoNumbering().WithPrefix("Cover"))
    ///     .SetPageLabels(1, label => label.LowercaseRoman())       // i, ii, iii...
    ///     .SetPageLabels(2, label => label.Decimal().StartingAt(1)) // 1, 2, 3...
    ///     .Save("document.pdf");
    /// </code>
    /// </example>
    public PdfDocumentBuilder SetPageLabels(int startPageIndex, Action<PdfPageLabelBuilder> configure)
    {
        var range = new PdfPageLabelRange(startPageIndex);
        var builder = new PdfPageLabelBuilder(range);
        configure(builder);
        _pageLabelRanges.Add(range);
        return this;
    }

    /// <summary>
    /// Define page labels with decimal numbering starting from a specific page
    /// </summary>
    /// <param name="startPageIndex">0-based page index where this labeling scheme starts</param>
    /// <param name="startNumber">The starting number (default = 1)</param>
    /// <param name="prefix">Optional prefix before the number</param>
    /// <returns>The document builder for chaining</returns>
    public PdfDocumentBuilder SetPageLabels(int startPageIndex, int startNumber = 1, string? prefix = null)
    {
        var range = new PdfPageLabelRange(startPageIndex)
        {
            Style = PdfPageLabelStyle.Decimal,
            StartNumber = startNumber,
            Prefix = prefix
        };
        _pageLabelRanges.Add(range);
        return this;
    }

    /// <summary>
    /// Define page labels and get a reference to the range
    /// </summary>
    /// <param name="startPageIndex">0-based page index where this labeling scheme starts</param>
    /// <param name="range">Output parameter containing the created page label range</param>
    /// <param name="configure">Optional action to configure the page label style</param>
    /// <returns>The document builder for chaining</returns>
    public PdfDocumentBuilder SetPageLabels(int startPageIndex, out PdfPageLabelRange range, Action<PdfPageLabelBuilder>? configure = null)
    {
        range = new PdfPageLabelRange(startPageIndex);
        if (configure != null)
        {
            var builder = new PdfPageLabelBuilder(range);
            configure(builder);
        }
        _pageLabelRanges.Add(range);
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
    /// Get the encryption settings (null if no encryption)
    /// </summary>
    internal PdfEncryptionSettings? EncryptionSettings => _encryptionSettings;

    /// <summary>
    /// Get the defined layers
    /// </summary>
    internal IReadOnlyList<PdfLayer> Layers => _layers;

    /// <summary>
    /// Get the bookmarks
    /// </summary>
    internal IReadOnlyList<PdfBookmark> Bookmarks => _bookmarks;

    /// <summary>
    /// Get the page label ranges
    /// </summary>
    internal IReadOnlyList<PdfPageLabelRange> PageLabelRanges => _pageLabelRanges;

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