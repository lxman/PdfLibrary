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