using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Builder;

/// <summary>
/// Fluent builder for creating PDF documents
/// </summary>
public class PdfDocumentBuilder
{
    private readonly List<PdfPageBuilder> _pages = new();
    private readonly PdfMetadataBuilder _metadata = new();
    private PdfAcroFormBuilder? _acroForm;

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
