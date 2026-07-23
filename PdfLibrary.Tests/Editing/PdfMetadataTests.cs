using PdfLibrary.Builder;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Metadata;
using PdfLibrary.Structure;
using System.Text;
using Xunit;

namespace PdfLibrary.Tests.Editing;

/// <summary>
/// Tests for PdfMetadata facade: Info dictionary access, XMP sync, and persistence.
/// </summary>
public class PdfMetadataTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static byte[] BlankDoc()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.CreateEmpty())
            doc.Save(ms);
        return ms.ToArray();
    }

    private static byte[] OnePageDoc() =>
        PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Hello", 100, 700))
            .ToByteArray();

    private static PdfDocument Reload(byte[] bytes)
    {
        var ms = new MemoryStream(bytes);
        return PdfDocument.Load(ms);
    }

    // ── Task 5: Info dict typed properties read/write ────────────────────────

    [Fact]
    public void Title_SetAndGet_RoundTrips()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        edit.Metadata.Title = "My Title";
        Assert.Equal("My Title", edit.Metadata.Title);
    }

    [Fact]
    public void Author_SetAndGet_RoundTrips()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        edit.Metadata.Author = "Jane Doe";
        Assert.Equal("Jane Doe", edit.Metadata.Author);
    }

    [Fact]
    public void Subject_SetAndGet_RoundTrips()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        edit.Metadata.Subject = "Testing";
        Assert.Equal("Testing", edit.Metadata.Subject);
    }

    [Fact]
    public void Keywords_SetAndGet_RoundTrips()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        edit.Metadata.Keywords = "pdf, library, csharp";
        Assert.Equal("pdf, library, csharp", edit.Metadata.Keywords);
    }

    [Fact]
    public void Creator_SetAndGet_RoundTrips()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        edit.Metadata.Creator = "TestApp";
        Assert.Equal("TestApp", edit.Metadata.Creator);
    }

    [Fact]
    public void Producer_SetAndGet_RoundTrips()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        edit.Metadata.Producer = "PdfLibrary 1.0";
        Assert.Equal("PdfLibrary 1.0", edit.Metadata.Producer);
    }

    [Fact]
    public void CreationDate_SetAndGet_RoundTrips()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        var dt = new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);
        edit.Metadata.CreationDate = dt;
        Assert.Equal(dt, edit.Metadata.CreationDate);
    }

    [Fact]
    public void ModificationDate_SetAndGet_RoundTrips()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        var dt = new DateTimeOffset(2026, 6, 21, 8, 30, 0, TimeSpan.Zero);
        edit.Metadata.ModificationDate = dt;
        Assert.Equal(dt, edit.Metadata.ModificationDate);
    }

    [Fact]
    public void GetProperty_OnNewDoc_ReturnsNull()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        Assert.Null(edit.Metadata.Title);
        Assert.Null(edit.Metadata.Author);
        Assert.Null(edit.Metadata.CreationDate);
    }

    // ── Task 5: Metadata property is lazy (same instance) ────────────────────

    [Fact]
    public void Metadata_Property_ReturnsSameInstanceOnMultipleCalls()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        PdfMetadata m1 = edit.Metadata;
        PdfMetadata m2 = edit.Metadata;
        Assert.Same(m1, m2);
    }

    // ── Task 6: XMP sync ─────────────────────────────────────────────────────

    [Fact]
    public void Title_Set_SyncsXmpDcTitle()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        edit.Metadata.Title = "XMP Test Title";
        XmpProperty? prop = edit.Metadata.Xmp.Get(XmpSchemas.Dc, "title");
        Assert.NotNull(prop);
        Assert.Equal(XmpValueKind.LangAlt, prop!.Kind);
        Assert.Equal("XMP Test Title", prop.LangAlt["x-default"]);
    }

    [Fact]
    public void Author_Set_SyncsXmpDcCreatorSeq()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        edit.Metadata.Author = "Alice";
        XmpProperty? prop = edit.Metadata.Xmp.Get(XmpSchemas.Dc, "creator");
        Assert.NotNull(prop);
        Assert.Equal(XmpValueKind.Array, prop!.Kind);
        Assert.True(prop.Ordered);
        Assert.Equal(new[] { "Alice" }, prop.Items);
    }

    [Fact]
    public void Subject_Set_SyncsXmpDcDescription()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        edit.Metadata.Subject = "Test subject";
        XmpProperty? prop = edit.Metadata.Xmp.Get(XmpSchemas.Dc, "description");
        Assert.NotNull(prop);
        Assert.Equal(XmpValueKind.LangAlt, prop!.Kind);
        Assert.Equal("Test subject", prop.LangAlt["x-default"]);
    }

    [Fact]
    public void Keywords_Set_SyncsXmpPdfKeywordsAndDcSubjectBag()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        edit.Metadata.Keywords = "pdf, library";
        XmpProperty? pdfKw = edit.Metadata.Xmp.Get(XmpSchemas.Pdf, "Keywords");
        Assert.NotNull(pdfKw);
        Assert.Equal("pdf, library", pdfKw!.Value);
        XmpProperty? dcSubject = edit.Metadata.Xmp.Get(XmpSchemas.Dc, "subject");
        Assert.NotNull(dcSubject);
        Assert.Equal(XmpValueKind.Array, dcSubject!.Kind);
        Assert.False(dcSubject.Ordered);
        Assert.Contains("pdf", dcSubject.Items);
        Assert.Contains("library", dcSubject.Items);
    }

    [Fact]
    public void Creator_Set_SyncsXmpCreatorTool()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        edit.Metadata.Creator = "MyApp 2.0";
        XmpProperty? prop = edit.Metadata.Xmp.Get(XmpSchemas.Xmp, "CreatorTool");
        Assert.NotNull(prop);
        Assert.Equal("MyApp 2.0", prop!.Value);
    }

    [Fact]
    public void Producer_Set_SyncsXmpPdfProducer()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        edit.Metadata.Producer = "PdfLib 1.0";
        XmpProperty? prop = edit.Metadata.Xmp.Get(XmpSchemas.Pdf, "Producer");
        Assert.NotNull(prop);
        Assert.Equal("PdfLib 1.0", prop!.Value);
    }

    [Fact]
    public void CreationDate_Set_SyncsXmpCreateDate()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        var dt = new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);
        edit.Metadata.CreationDate = dt;
        XmpProperty? prop = edit.Metadata.Xmp.Get(XmpSchemas.Xmp, "CreateDate");
        Assert.NotNull(prop);
        Assert.Equal("2026-06-20T12:00:00+00:00", prop!.Value);
    }

    [Fact]
    public void ModificationDate_Set_SyncsXmpModifyDate()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        var dt = new DateTimeOffset(2026, 6, 21, 8, 30, 0, TimeSpan.FromHours(-5));
        edit.Metadata.ModificationDate = dt;
        XmpProperty? prop = edit.Metadata.Xmp.Get(XmpSchemas.Xmp, "ModifyDate");
        Assert.NotNull(prop);
        Assert.Equal("2026-06-21T08:30:00-05:00", prop!.Value);
    }

    // ── Task 6: /Catalog /Metadata stream is created ─────────────────────────

    [Fact]
    public void SetTitle_CreatesMetadataStreamInCatalog()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        edit.Metadata.Title = "Catalog Stream Test";
        // After setting, the catalog should have /Metadata
        Assert.NotNull(doc.CatalogDictionary);
        bool hasMetadata = doc.CatalogDictionary!.ContainsKey(new PdfLibrary.Core.Primitives.PdfName("Metadata"));
        Assert.True(hasMetadata, "/Catalog should have /Metadata entry after setting a property");
    }

    // ── Task 7: Save/Load round-trip ─────────────────────────────────────────

    [Fact]
    public void SetTitle_SaveReload_TitlePersistedInInfoDict()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        edit.Metadata.Title = "Persisted Title";
        var ms = new MemoryStream();
        edit.Save(ms);
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);
        PdfDocumentEditor edit2 = reloaded.Edit();
        Assert.Equal("Persisted Title", edit2.Metadata.Title);
    }

    [Fact]
    public void SetAllStringProps_SaveReload_AllPersistedInInfoDict()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        edit.Metadata.Title    = "TitleVal";
        edit.Metadata.Author   = "AuthorVal";
        edit.Metadata.Subject  = "SubjectVal";
        edit.Metadata.Keywords = "kw1, kw2";
        edit.Metadata.Creator  = "CreatorVal";
        edit.Metadata.Producer = "ProducerVal";
        var ms = new MemoryStream();
        edit.Save(ms);
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);
        PdfDocumentEditor edit2 = reloaded.Edit();
        Assert.Equal("TitleVal",    edit2.Metadata.Title);
        Assert.Equal("AuthorVal",   edit2.Metadata.Author);
        Assert.Equal("SubjectVal",  edit2.Metadata.Subject);
        Assert.Equal("kw1, kw2",   edit2.Metadata.Keywords);
        Assert.Equal("CreatorVal",  edit2.Metadata.Creator);
        Assert.Equal("ProducerVal", edit2.Metadata.Producer);
    }

    [Fact]
    public void SetTitle_SaveReload_XmpStreamPresent()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        edit.Metadata.Title = "XMP Persisted";
        var ms = new MemoryStream();
        edit.Save(ms);
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);
        PdfDocumentEditor edit2 = reloaded.Edit();
        // XMP title should have survived round-trip
        XmpProperty? prop = edit2.Metadata.Xmp.Get(XmpSchemas.Dc, "title");
        Assert.NotNull(prop);
        Assert.Equal("XMP Persisted", prop!.LangAlt["x-default"]);
    }

    [Fact]
    public void CreationDate_SaveReload_PersistedWithCorrectPrecision()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        var dt = new DateTimeOffset(2026, 6, 20, 14, 30, 45, TimeSpan.Zero);
        edit.Metadata.CreationDate = dt;
        var ms = new MemoryStream();
        edit.Save(ms);
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);
        DateTimeOffset? reloaded2 = reloaded.Edit().Metadata.CreationDate;
        Assert.NotNull(reloaded2);
        Assert.Equal(dt, reloaded2!.Value);
    }

    // ── Task 7: GC survival — /Metadata reachable from /Root ─────────────────

    [Fact]
    public void SetTitle_AfterSaveLoad_MetadataStreamSurvivesGc()
    {
        // The /Catalog/Metadata stream must survive the full-rewrite GC during Save().
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        edit.Metadata.Title = "GC Survival";
        var ms = new MemoryStream();
        edit.Save(ms);
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);
        // Verify /Catalog has /Metadata
        bool hasMetadata = reloaded.CatalogDictionary?.ContainsKey(
            new PdfLibrary.Core.Primitives.PdfName("Metadata")) ?? false;
        Assert.True(hasMetadata, "/Metadata in /Catalog must survive Save/Load GC");
    }

    // ── Task 7: existing XMP preserved ───────────────────────────────────────

    [Fact]
    public void ExistingXmpProp_SetUnrelatedProp_ExistingPreserved()
    {
        // Load a blank doc, set a custom XMP property via Xmp directly,
        // then set a title via Metadata. The custom XMP prop must survive.
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        // Write a custom XMP property directly
        edit.Metadata.Xmp.SetSimple(XmpSchemas.Xmp, XmpSchemas.XmpPrefix, "MetadataDate", "2026-01-01T00:00:00+00:00");
        // Now write MetadataDate to the stream by setting title (which triggers WriteXmpStream)
        edit.Metadata.Title = "ExistingXmpTest";
        // Both properties should be present in the in-memory XMP
        XmpProperty? metaDate = edit.Metadata.Xmp.Get(XmpSchemas.Xmp, "MetadataDate");
        XmpProperty? title    = edit.Metadata.Xmp.Get(XmpSchemas.Dc, "title");
        Assert.NotNull(metaDate);
        Assert.NotNull(title);
        Assert.Equal("2026-01-01T00:00:00+00:00", metaDate!.Value);
        Assert.Equal("ExistingXmpTest", title!.LangAlt["x-default"]);
    }

    // ── Task 7: null clear property ───────────────────────────────────────────

    [Fact]
    public void SetTitle_ToNull_ClearsTitle()
    {
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        edit.Metadata.Title = "To be cleared";
        edit.Metadata.Title = null;
        Assert.Null(edit.Metadata.Title);
        // XMP should also be cleared
        Assert.Null(edit.Metadata.Xmp.Get(XmpSchemas.Dc, "title"));
    }

    // ── Compressed /Metadata stream must be decoded before XMP parse ─────────
    //
    // Compression on /Metadata is legal since PDF/A-2 (only PDF/A-1 forbade filters there), and
    // is what real-world generators (e.g. the official ZUGFeRD 2.5 example PDFs) commonly do.
    // LoadOrCreateXmp must decode the stream the same way EmbeddedFileReader/OutputIntentReader
    // do (GetDecodedData), not read metaStream.Data (raw, possibly-compressed bytes) directly.

    /// <summary>
    /// Rewrites the document's existing (uncompressed) /Catalog/Metadata stream in place to be
    /// FlateDecode-compressed, preserving its object number so the catalog reference and any
    /// PdfMetadata bookkeeping (_metadataObjectNumber) still point at the same object.
    /// </summary>
    private static void CompressExistingMetadataStreamInPlace(PdfDocument doc)
    {
        PdfDictionary catalog = doc.CatalogDictionary!;
        var metaRef = (PdfIndirectReference)catalog[new PdfName("Metadata")];
        var metaStream = (PdfStream)doc.GetObject(metaRef.ObjectNumber)!;
        byte[] decoded = metaStream.Data; // currently raw/uncompressed XMP bytes
        metaStream.SetEncodedData(decoded, "FlateDecode");
    }

    [Fact]
    public void CompressedMetadataStream_Xmp_SurfacesProperty()
    {
        // Build a document with a real /Catalog/Metadata stream (via the normal Title setter),
        // then compress that stream in place with FlateDecode, exactly like an external
        // generator's PDF/A-2/3 output would arrive on disk.
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        edit.Metadata.Title = "Compressed Metadata Test";
        CompressExistingMetadataStreamInPlace(doc);

        var ms = new MemoryStream();
        edit.Save(ms);
        ms.Position = 0;

        using PdfDocument reloaded = PdfDocument.Load(ms);
        // Sanity: the reloaded /Metadata stream really is FlateDecode-compressed.
        PdfDictionary reloadedCatalog = reloaded.CatalogDictionary!;
        var reloadedMetaRef = (PdfIndirectReference)reloadedCatalog[new PdfName("Metadata")];
        var reloadedMetaStream = (PdfStream)reloaded.GetObject(reloadedMetaRef.ObjectNumber)!;
        Assert.True(reloadedMetaStream.Dictionary.ContainsKey(new PdfName("Filter")),
            "test setup sanity: reloaded /Metadata stream must carry /Filter");

        XmpProperty? prop = reloaded.Edit().Metadata.Xmp.Get(XmpSchemas.Dc, "title");
        Assert.NotNull(prop);
        Assert.Equal("Compressed Metadata Test", prop!.LangAlt["x-default"]);
    }

    [Fact]
    public void CompressedMetadataStream_SetRawXmp_ReplacesRatherThanDuplicatesAndRoundTrips()
    {
        // Guards the ReplaceObject interaction called out in the task: after loading a document
        // whose /Metadata stream is compressed on disk, SetRawXmp must replace that stream object
        // (not duplicate it), the replacement must not carry a stale /Filter entry from the
        // compressed stream it replaced, and the new content must round-trip through Save/Load.
        //
        // Phase 1: build a document whose /Metadata stream is FlateDecode-compressed on disk.
        using PdfDocument doc = Reload(BlankDoc());
        PdfDocumentEditor edit = doc.Edit();
        edit.Metadata.Title = "Original";
        CompressExistingMetadataStreamInPlace(doc);
        var ms1 = new MemoryStream();
        edit.Save(ms1);
        ms1.Position = 0;

        // Phase 2: fresh reload -- a clean PdfMetadata (no cached in-memory packet), reading the
        // compressed stream from disk for the first time before we replace it.
        using PdfDocument reloaded = PdfDocument.Load(ms1);
        PdfDocumentEditor edit2 = reloaded.Edit();
        PdfDictionary catalog = reloaded.CatalogDictionary!;
        var originalMetaRef = (PdfIndirectReference)catalog[new PdfName("Metadata")];
        int originalObjectNumber = originalMetaRef.ObjectNumber;

        Assert.NotNull(edit2.Metadata.Xmp.Get(XmpSchemas.Dc, "title"));

        XmpPacket freshPacket = XmpPacket.CreateEmpty();
        freshPacket.SetSimple(XmpSchemas.Xmp, XmpSchemas.XmpPrefix, "CreatorTool", "ReplacedPacket");
        edit2.Metadata.SetRawXmp(freshPacket.Serialize());

        var newMetaRef = (PdfIndirectReference)catalog[new PdfName("Metadata")];
        Assert.Equal(originalObjectNumber, newMetaRef.ObjectNumber); // replaced, not duplicated

        var newMetaStream = (PdfStream)reloaded.GetObject(newMetaRef.ObjectNumber)!;
        Assert.False(newMetaStream.Dictionary.ContainsKey(new PdfName("Filter")),
            "the replacement stream must be a fresh dict with no stale /Filter from the compressed stream it replaced");

        Assert.NotNull(edit2.Metadata.Xmp.Get(XmpSchemas.Xmp, "CreatorTool"));
        Assert.Null(edit2.Metadata.Xmp.Get(XmpSchemas.Dc, "title")); // old packet is gone, not merged

        // Phase 3: full disk round-trip of the replacement -- must read back cleanly (uncompressed,
        // no stale filter causing mis-decode).
        var ms2 = new MemoryStream();
        edit2.Save(ms2);
        ms2.Position = 0;
        using PdfDocument reloaded2 = PdfDocument.Load(ms2);
        XmpProperty? prop = reloaded2.Edit().Metadata.Xmp.Get(XmpSchemas.Xmp, "CreatorTool");
        Assert.NotNull(prop);
        Assert.Equal("ReplacedPacket", prop!.Value);
    }
}
