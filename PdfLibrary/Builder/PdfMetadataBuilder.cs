namespace PdfLibrary.Builder;

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

    /// <summary>
    /// Set the document title (appears in PDF viewers and search results)
    /// </summary>
    /// <param name="title">The document title</param>
    /// <returns>The metadata builder for chaining</returns>
    /// <example>
    /// <code>
    /// .WithMetadata(m => m.SetTitle("Annual Report 2024"))
    /// </code>
    /// </example>
    public PdfMetadataBuilder SetTitle(string title)
    {
        Title = title;
        return this;
    }

    /// <summary>
    /// Set the document author (creator of the document content)
    /// </summary>
    /// <param name="author">The author name or organization</param>
    /// <returns>The metadata builder for chaining</returns>
    /// <example>
    /// <code>
    /// .WithMetadata(m => m.SetAuthor("ACME Corporation"))
    /// </code>
    /// </example>
    public PdfMetadataBuilder SetAuthor(string author)
    {
        Author = author;
        return this;
    }

    /// <summary>
    /// Set the document subject (brief description of the document topic)
    /// </summary>
    /// <param name="subject">The document subject or description</param>
    /// <returns>The metadata builder for chaining</returns>
    /// <example>
    /// <code>
    /// .WithMetadata(m => m.SetSubject("Q4 Financial Results"))
    /// </code>
    /// </example>
    public PdfMetadataBuilder SetSubject(string subject)
    {
        Subject = subject;
        return this;
    }

    /// <summary>
    /// Set document keywords for search and indexing (comma or semicolon separated)
    /// </summary>
    /// <param name="keywords">Keywords or tags associated with the document</param>
    /// <returns>The metadata builder for chaining</returns>
    /// <example>
    /// <code>
    /// .WithMetadata(m => m.SetKeywords("finance, report, quarterly, 2024"))
    /// </code>
    /// </example>
    public PdfMetadataBuilder SetKeywords(string keywords)
    {
        Keywords = keywords;
        return this;
    }

    /// <summary>
    /// Set the application that created the original document (before PDF conversion)
    /// </summary>
    /// <param name="creator">The name of the application that created the document</param>
    /// <returns>The metadata builder for chaining</returns>
    /// <example>
    /// <code>
    /// .WithMetadata(m => m.SetCreator("Microsoft Word"))
    /// </code>
    /// </example>
    public PdfMetadataBuilder SetCreator(string creator)
    {
        Creator = creator;
        return this;
    }

    /// <summary>
    /// Set the application that converted the document to PDF
    /// </summary>
    /// <param name="producer">The name of the PDF producer application</param>
    /// <returns>The metadata builder for chaining</returns>
    /// <example>
    /// <code>
    /// .WithMetadata(m => m.SetProducer("PdfLibrary 1.0"))
    /// </code>
    /// </example>
    public PdfMetadataBuilder SetProducer(string producer)
    {
        Producer = producer;
        return this;
    }

    /// <summary>
    /// Set the date and time the document was created
    /// </summary>
    /// <param name="date">The creation date and time</param>
    /// <returns>The metadata builder for chaining</returns>
    /// <example>
    /// <code>
    /// .WithMetadata(m => m.SetCreationDate(DateTime.Now))
    /// </code>
    /// </example>
    public PdfMetadataBuilder SetCreationDate(DateTime date)
    {
        CreationDate = date;
        return this;
    }

    /// <summary>
    /// Set the date and time the document was last modified
    /// </summary>
    /// <param name="date">The modification date and time</param>
    /// <returns>The metadata builder for chaining</returns>
    /// <example>
    /// <code>
    /// .WithMetadata(m => m
    ///     .SetCreationDate(new DateTime(2024, 1, 1))
    ///     .SetModificationDate(DateTime.Now))
    /// </code>
    /// </example>
    public PdfMetadataBuilder SetModificationDate(DateTime date)
    {
        ModificationDate = date;
        return this;
    }
}