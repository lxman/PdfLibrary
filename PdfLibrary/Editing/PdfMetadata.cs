using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Metadata;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing;

/// <summary>
/// Facade for reading and writing document information (Info dictionary + XMP stream).
/// Obtained via <see cref="PdfDocumentEditor.Metadata"/>.
/// </summary>
public sealed class PdfMetadata
{
    private readonly PdfDocument _document;
    private XmpPacket? _xmp;
    private int _metadataObjectNumber = -1;

    internal PdfMetadata(PdfDocument document)
    {
        _document = document;
    }

    // ---- Typed Info-dict properties -----------------------------------------

    /// <summary>Document title (/Title; dc:title LangAlt in XMP).</summary>
    public string? Title
    {
        get => GetInfoString("Title");
        set
        {
            SetInfoString("Title", value);
            SyncXmp(value is not null
                ? () => Xmp.SetLangAlt(XmpSchemas.Dc, XmpSchemas.DcPrefix, "title", value)
                : () => Xmp.Remove(XmpSchemas.Dc, "title"));
        }
    }

    /// <summary>Author (/Author; dc:creator Seq in XMP).</summary>
    public string? Author
    {
        get => GetInfoString("Author");
        set
        {
            SetInfoString("Author", value);
            SyncXmp(value is not null
                ? () => Xmp.SetArray(XmpSchemas.Dc, XmpSchemas.DcPrefix, "creator", new[] { value }, ordered: true)
                : () => Xmp.Remove(XmpSchemas.Dc, "creator"));
        }
    }

    /// <summary>Subject (/Subject; dc:description LangAlt in XMP).</summary>
    public string? Subject
    {
        get => GetInfoString("Subject");
        set
        {
            SetInfoString("Subject", value);
            SyncXmp(value is not null
                ? () => Xmp.SetLangAlt(XmpSchemas.Dc, XmpSchemas.DcPrefix, "description", value)
                : () => Xmp.Remove(XmpSchemas.Dc, "description"));
        }
    }

    /// <summary>Keywords (/Keywords; pdf:Keywords + dc:subject Bag in XMP).</summary>
    public string? Keywords
    {
        get => GetInfoString("Keywords");
        set
        {
            SetInfoString("Keywords", value);
            SyncXmp(value is not null
                ? () => SyncKeywordsXmp(value)
                : () => { Xmp.Remove(XmpSchemas.Pdf, "Keywords"); Xmp.Remove(XmpSchemas.Dc, "subject"); });
        }
    }

    /// <summary>Creator application (/Creator; xmp:CreatorTool in XMP).</summary>
    public string? Creator
    {
        get => GetInfoString("Creator");
        set
        {
            SetInfoString("Creator", value);
            SyncXmp(value is not null
                ? () => Xmp.SetSimple(XmpSchemas.Xmp, XmpSchemas.XmpPrefix, "CreatorTool", value)
                : () => Xmp.Remove(XmpSchemas.Xmp, "CreatorTool"));
        }
    }

    /// <summary>Producer (/Producer; pdf:Producer in XMP).</summary>
    public string? Producer
    {
        get => GetInfoString("Producer");
        set
        {
            SetInfoString("Producer", value);
            SyncXmp(value is not null
                ? () => Xmp.SetSimple(XmpSchemas.Pdf, XmpSchemas.PdfPrefix, "Producer", value)
                : () => Xmp.Remove(XmpSchemas.Pdf, "Producer"));
        }
    }

    /// <summary>Creation date (/CreationDate; xmp:CreateDate in XMP).</summary>
    public DateTimeOffset? CreationDate
    {
        get => GetInfoDate("CreationDate");
        set
        {
            SetInfoDate("CreationDate", value);
            SyncXmp(value.HasValue
                ? () => Xmp.SetSimple(XmpSchemas.Xmp, XmpSchemas.XmpPrefix, "CreateDate", PdfDate.FormatIso(value.Value))
                : () => Xmp.Remove(XmpSchemas.Xmp, "CreateDate"));
        }
    }

    /// <summary>Modification date (/ModDate; xmp:ModifyDate in XMP).</summary>
    public DateTimeOffset? ModificationDate
    {
        get => GetInfoDate("ModDate");
        set
        {
            SetInfoDate("ModDate", value);
            SyncXmp(value.HasValue
                ? () => Xmp.SetSimple(XmpSchemas.Xmp, XmpSchemas.XmpPrefix, "ModifyDate", PdfDate.FormatIso(value.Value))
                : () => Xmp.Remove(XmpSchemas.Xmp, "ModifyDate"));
        }
    }

    /// <summary>The XMP packet. Lazily loaded from /Catalog /Metadata or empty.</summary>
    public XmpPacket Xmp => _xmp ??= LoadOrCreateXmp();

    // ---- Info dict helpers --------------------------------------------------

    private PdfDictionary EnsureInfoDictionary()
    {
        if (_document.Trailer.Info is { } infoRef)
        {
            if (_document.GetObject(infoRef.ObjectNumber) is PdfDictionary existing)
                return existing;
        }
        var info = new PdfDictionary();
        PdfIndirectReference newRef = _document.RegisterObject(info);
        _document.Trailer.Info = newRef;
        return info;
    }

    private string? GetInfoString(string key)
    {
        if (_document.Trailer.Info is null) return null;
        if (_document.GetObject(_document.Trailer.Info.ObjectNumber) is not PdfDictionary info) return null;
        if (!info.TryGetValue(new PdfName(key), out PdfObject obj)) return null;
        return obj is PdfString s ? s.GetText() : null;
    }

    private void SetInfoString(string key, string? value)
    {
        PdfDictionary info = EnsureInfoDictionary();
        if (value is null)
            info.Remove(new PdfName(key));
        else
            info[new PdfName(key)] = PdfString.FromText(value);
    }

    private DateTimeOffset? GetInfoDate(string key)
    {
        string? raw = GetInfoString(key);
        if (raw is null) return null;
        return PdfDate.TryParsePdf(raw, out DateTimeOffset result) ? result : null;
    }

    private void SetInfoDate(string key, DateTimeOffset? value)
    {
        PdfDictionary info = EnsureInfoDictionary();
        if (!value.HasValue)
            info.Remove(new PdfName(key));
        else
            info[new PdfName(key)] = PdfString.FromByteLiteral(PdfDate.FormatPdf(value.Value));
    }

    // ---- XMP sync -----------------------------------------------------------

    private void SyncXmp(Action mutate)
    {
        mutate();
        WriteXmpStream();
    }

    private void SyncKeywordsXmp(string value)
    {
        Xmp.SetSimple(XmpSchemas.Pdf, XmpSchemas.PdfPrefix, "Keywords", value);
        string[] parts = value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();
        Xmp.SetArray(XmpSchemas.Dc, XmpSchemas.DcPrefix, "subject", parts, ordered: false);
    }

    // ---- /Metadata stream management ----------------------------------------

    private XmpPacket LoadOrCreateXmp()
    {
        PdfDictionary? catalog = _document.CatalogDictionary;
        if (catalog is not null &&
            catalog.TryGetValue(new PdfName("Metadata"), out PdfObject metaObj))
        {
            if (metaObj is PdfIndirectReference metaRef)
            {
                _metadataObjectNumber = metaRef.ObjectNumber;
                if (_document.GetObject(metaRef.ObjectNumber) is PdfStream metaStream)
                {
                    try { return XmpPacket.Parse(metaStream.Data); }
                    catch { return XmpPacket.CreateEmpty(); }
                }
            }
        }
        return XmpPacket.CreateEmpty();
    }

    private void WriteXmpStream()
    {
        byte[] bytes = Xmp.Serialize();
        var streamDict = new PdfDictionary
        {
            [new PdfName("Type")]    = new PdfName("Metadata"),
            [new PdfName("Subtype")] = new PdfName("XML")
        };
        var stream = new PdfStream(streamDict, bytes);

        if (_metadataObjectNumber >= 0)
        {
            _document.ReplaceObject(_metadataObjectNumber, stream);
        }
        else
        {
            PdfIndirectReference metaRef = _document.RegisterObject(stream);
            _metadataObjectNumber = metaRef.ObjectNumber;
            PdfDictionary? catalog = _document.CatalogDictionary;
            if (catalog is not null)
                catalog[new PdfName("Metadata")] = metaRef;
        }
    }
}
