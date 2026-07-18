using System;
using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Document;

/// <summary>
/// Tests for <see cref="PdfDocument.GetEmbeddedFiles"/> — the public read-only view of a document's
/// embedded files: the catalog's /Names /EmbeddedFiles name tree plus catalog-level /AF associated
/// files (ISO 32000-2, 7.11.4 / 14.13).
/// </summary>
public class EmbeddedFilesTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfString Str(string s) => new(Encoding.ASCII.GetBytes(s));

    /// <summary>A one-page document whose catalog is shaped by <paramref name="configureCatalog"/>.</summary>
    private static PdfDocument Doc(Action<PdfDocument, PdfDictionary> configureCatalog)
    {
        var doc = new PdfDocument();
        doc.AddObject(3, 0, new PdfDictionary { [N("Type")] = N("Page"), [N("Parent")] = Ref(2) });
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(3)),
            [N("Count")] = new PdfInteger(1),
        });
        var catalog = new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) };
        configureCatalog(doc, catalog);
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    /// <summary>An embedded-file stream (object <paramref name="streamNum"/>) + a /Filespec (object
    /// <paramref name="specNum"/>) carrying the given metadata. Registering the spec under
    /// /Names /EmbeddedFiles (or /AF) is the caller's job.</summary>
    private static void AddFileSpec(PdfDocument doc, int specNum, int streamNum,
        byte[] data, string? filter = null, string? subtype = "text/xml",
        string? f = "file.xml", string? uf = null, string? desc = null, string? afRel = null,
        bool withEF = true)
    {
        var streamDict = new PdfDictionary();
        if (subtype is not null) streamDict[N("Subtype")] = N(subtype);
        if (filter is not null) streamDict[N("Filter")] = N(filter);
        doc.AddObject(streamNum, 0, new PdfStream(streamDict, data));

        var spec = new PdfDictionary { [N("Type")] = N("Filespec") };
        if (withEF) spec[N("EF")] = new PdfDictionary { [N("F")] = Ref(streamNum) };
        if (f is not null) spec[N("F")] = Str(f);
        if (uf is not null) spec[N("UF")] = Str(uf);
        if (desc is not null) spec[N("Desc")] = Str(desc);
        if (afRel is not null) spec[N("AFRelationship")] = N(afRel);
        doc.AddObject(specNum, 0, spec);
    }

    /// <summary>Registers filespec object numbers as a flat /Names /EmbeddedFiles leaf on the catalog,
    /// keyed by the given names, in order.</summary>
    private static void RegisterInNameTree(PdfDictionary catalog, params (string Name, int SpecNum)[] entries)
    {
        var names = new PdfArray();
        foreach ((string name, int specNum) in entries)
        {
            names.Add(Str(name));
            names.Add(Ref(specNum));
        }
        catalog[N("Names")] = new PdfDictionary
        {
            [N("EmbeddedFiles")] = new PdfDictionary { [N("Names")] = names },
        };
    }

    // ── name-tree path ───────────────────────────────────────────────────────

    [Fact]
    public void FlatNameTree_metadataAndDataRoundTrip()
    {
        PdfDocument doc = Doc((d, c) =>
        {
            AddFileSpec(d, 10, 11, data: Encoding.ASCII.GetBytes("<x/>"),
                f: "file.xml", uf: "file.xml", desc: "an attachment", afRel: "Data");
            RegisterInNameTree(c, ("file.xml", 10));
        });

        EmbeddedFileDescriptor file = Assert.Single(doc.GetEmbeddedFiles());
        Assert.Equal("file.xml", file.Name);
        Assert.Equal("file.xml", file.FileName);
        Assert.Equal("file.xml", file.UnicodeFileName);
        Assert.Equal("an attachment", file.Description);
        Assert.Equal("Data", file.AfRelationship);
        Assert.Equal("text/xml", file.MimeType);
        Assert.False(file.IsAssociated);
        Assert.True(file.HasData);
        Assert.Equal("<x/>", Encoding.ASCII.GetString(file.GetDataBytes()!));
    }

    [Fact]
    public void KidsNestedTree_yieldsEntriesFromAllLeaves()
    {
        PdfDocument doc = Doc((d, c) =>
        {
            AddFileSpec(d, 10, 11, data: [1], f: "a.txt", subtype: "text/plain");
            AddFileSpec(d, 12, 13, data: [2], f: "b.txt", subtype: "text/plain");
            // Root node (obj 20) has two /Kids leaves (obj 21, obj 22), one entry each.
            d.AddObject(21, 0, new PdfDictionary { [N("Names")] = new PdfArray(Str("a.txt"), Ref(10)) });
            d.AddObject(22, 0, new PdfDictionary { [N("Names")] = new PdfArray(Str("b.txt"), Ref(12)) });
            d.AddObject(20, 0, new PdfDictionary { [N("Kids")] = new PdfArray(Ref(21), Ref(22)) });
            c[N("Names")] = new PdfDictionary { [N("EmbeddedFiles")] = Ref(20) };
        });

        IReadOnlyList<EmbeddedFileDescriptor> files = doc.GetEmbeddedFiles();
        Assert.Equal(2, files.Count);
        Assert.Contains(files, x => x.Name == "a.txt");
        Assert.Contains(files, x => x.Name == "b.txt");
    }

    [Fact]
    public void EfWithBothStreams_prefersUF()
    {
        PdfDocument doc = Doc((d, c) =>
        {
            d.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("from-f")));
            d.AddObject(12, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("from-uf")));
            d.AddObject(10, 0, new PdfDictionary
            {
                [N("Type")] = N("Filespec"),
                [N("F")] = Str("file.txt"),
                [N("UF")] = Str("file.txt"),
                [N("EF")] = new PdfDictionary { [N("F")] = Ref(11), [N("UF")] = Ref(12) },
            });
            RegisterInNameTree(c, ("file.txt", 10));
        });

        EmbeddedFileDescriptor file = Assert.Single(doc.GetEmbeddedFiles());
        Assert.Equal("from-uf", Encoding.ASCII.GetString(file.GetDataBytes()!));
    }

    [Fact]
    public void NoNameTree_returnsEmptyList()
    {
        PdfDocument doc = Doc((_, _) => { });
        Assert.Empty(doc.GetEmbeddedFiles());
    }

    [Fact]
    public void NamesWithoutEmbeddedFilesKey_returnsEmptyList()
    {
        PdfDocument doc = Doc((_, c) => c[N("Names")] = new PdfDictionary());
        Assert.Empty(doc.GetEmbeddedFiles());
    }

    // ── robustness ───────────────────────────────────────────────────────────

    [Fact]
    public void FilespecWithoutEF_isMetadataOnly()
    {
        PdfDocument doc = Doc((d, c) =>
        {
            AddFileSpec(d, 10, 11, data: [1], withEF: false, f: "file.xml", desc: "no payload");
            RegisterInNameTree(c, ("file.xml", 10));
        });

        EmbeddedFileDescriptor file = Assert.Single(doc.GetEmbeddedFiles());
        Assert.Equal("file.xml", file.FileName);
        Assert.Equal("no payload", file.Description);
        Assert.Null(file.MimeType);   // MIME lives on the embedded stream, which is absent here
        Assert.False(file.HasData);
        Assert.Null(file.GetDataBytes());
    }

    [Fact]
    public void UnknownFilter_keepsEntryWithoutData()
    {
        PdfDocument doc = Doc((d, c) =>
        {
            AddFileSpec(d, 10, 11, data: [1, 2, 3], filter: "NoSuchFilter");
            RegisterInNameTree(c, ("file.xml", 10));
        });

        EmbeddedFileDescriptor file = Assert.Single(doc.GetEmbeddedFiles());
        Assert.Equal("file.xml", file.Name);
        Assert.Equal("text/xml", file.MimeType);  // metadata survives the decode failure
        Assert.False(file.HasData);
        Assert.Null(file.GetDataBytes());
    }

    [Fact]
    public void CycleInKids_terminatesAndYieldsReachableEntries()
    {
        PdfDocument doc = Doc((d, c) =>
        {
            AddFileSpec(d, 10, 11, data: [1], f: "a.txt", subtype: "text/plain");
            // Root (obj 20) lists ITSELF as a kid alongside a real leaf (obj 21).
            d.AddObject(21, 0, new PdfDictionary { [N("Names")] = new PdfArray(Str("a.txt"), Ref(10)) });
            d.AddObject(20, 0, new PdfDictionary { [N("Kids")] = new PdfArray(Ref(20), Ref(21)) });
            c[N("Names")] = new PdfDictionary { [N("EmbeddedFiles")] = Ref(20) };
        });

        EmbeddedFileDescriptor file = Assert.Single(doc.GetEmbeddedFiles());
        Assert.Equal("a.txt", file.Name);
    }

    [Fact]
    public void GetDataBytes_returnsDefensiveCopy()
    {
        PdfDocument doc = Doc((d, c) =>
        {
            AddFileSpec(d, 10, 11, data: Encoding.ASCII.GetBytes("payload"));
            RegisterInNameTree(c, ("file.xml", 10));
        });
        EmbeddedFileDescriptor file = Assert.Single(doc.GetEmbeddedFiles());

        byte[]? first = file.GetDataBytes();
        Assert.NotNull(first);
        first![0] ^= 0xFF; // mutate the returned copy

        byte[]? second = file.GetDataBytes();
        Assert.NotNull(second);
        Assert.Equal((byte)'p', second![0]); // unaffected by the mutation of the first copy
    }

    // ── catalog /AF ──────────────────────────────────────────────────────────

    [Fact]
    public void SpecReferencedFromCatalogAF_isAssociated()
    {
        PdfDocument doc = Doc((d, c) =>
        {
            AddFileSpec(d, 10, 11, data: [1], afRel: "Data");
            RegisterInNameTree(c, ("file.xml", 10));
            c[N("AF")] = new PdfArray(Ref(10));
        });

        EmbeddedFileDescriptor file = Assert.Single(doc.GetEmbeddedFiles());
        Assert.True(file.IsAssociated);
    }

    [Fact]
    public void AfOnlySpec_isYieldedWithNullName()
    {
        PdfDocument doc = Doc((d, c) =>
        {
            AddFileSpec(d, 10, 11, data: Encoding.ASCII.GetBytes("<x/>"), afRel: "Alternative");
            c[N("AF")] = new PdfArray(Ref(10)); // no /Names /EmbeddedFiles at all
        });

        EmbeddedFileDescriptor file = Assert.Single(doc.GetEmbeddedFiles());
        Assert.Null(file.Name);
        Assert.Equal("file.xml", file.FileName);
        Assert.Equal("Alternative", file.AfRelationship);
        Assert.True(file.IsAssociated);
        Assert.True(file.HasData);
        Assert.Equal("<x/>", Encoding.ASCII.GetString(file.GetDataBytes()!));
    }

    [Fact]
    public void SpecInBothNameTreeAndAF_yieldsOneDescriptor()
    {
        PdfDocument doc = Doc((d, c) =>
        {
            AddFileSpec(d, 10, 11, data: [1], afRel: "Data");
            RegisterInNameTree(c, ("file.xml", 10));
            c[N("AF")] = new PdfArray(Ref(10)); // same spec, both registries — the Factur-X shape
        });

        EmbeddedFileDescriptor file = Assert.Single(doc.GetEmbeddedFiles());
        Assert.Equal("file.xml", file.Name); // the name-tree identity wins
        Assert.True(file.IsAssociated);
    }

    [Fact]
    public void DirectDictInAF_isYielded()
    {
        PdfDocument doc = Doc((d, c) =>
        {
            d.AddObject(11, 0, new PdfStream(
                new PdfDictionary { [N("Subtype")] = N("text/xml") }, Encoding.ASCII.GetBytes("<x/>")));
            // The filespec is an inline direct dictionary inside /AF — no indirect object, no name tree.
            c[N("AF")] = new PdfArray(new PdfDictionary
            {
                [N("Type")] = N("Filespec"),
                [N("F")] = Str("inline.xml"),
                [N("EF")] = new PdfDictionary { [N("F")] = Ref(11) },
            });
        });

        EmbeddedFileDescriptor file = Assert.Single(doc.GetEmbeddedFiles());
        Assert.Null(file.Name);
        Assert.Equal("inline.xml", file.FileName);
        Assert.True(file.IsAssociated);
        Assert.Equal("<x/>", Encoding.ASCII.GetString(file.GetDataBytes()!));
    }
}
