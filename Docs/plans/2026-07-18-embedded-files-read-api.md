# Embedded-Files Read API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A public, generic, read-only embedded-files API on `PdfDocument` — `GetEmbeddedFiles()` → `IReadOnlyList<EmbeddedFileDescriptor>` — so external consumers (first: the EInvoice Factur-X bridge) can extract embedded attachments such as `factur-x.xml` from PDF/A-3 files.

**Architecture:** The established reader pattern: an `internal static EmbeddedFileReader` in `PdfLibrary.Document` walks the catalog's `/Names /EmbeddedFiles` name tree (iterative, cycle-guarded, node-budgeted) plus the catalog-level `/AF` array, decodes each `/EF` stream eagerly, and returns public immutable `EmbeddedFileDescriptor`s. It deliberately duplicates the small catalog walk rather than reusing `ConformanceContext` (documented precedent in `OutputIntents.cs`).

**Tech Stack:** .NET (net8.0;net9.0;net10.0 multi-target), xunit (PdfLibrary.Tests has `InternalsVisibleTo`), no new dependencies.

**Spec:** `Docs/specs/2026-07-18-embedded-files-read-api-design.md`

## Global Constraints

- **Repo:** ALL work happens in `C:\Users\jorda\RiderProjects\PDF` (the PdfLibrary repo) — NOT the EInvoice repo. All relative paths below are from that root. Main branch is `master`.
- **Branch:** create `feat/embedded-files-read-api` from `master` in Task 1; commit per task; NO push (standing gate: pushes only on explicit user instruction).
- **Pre-existing dirt:** the working tree carries an uncommitted `pack-local.ps1` fix. Do NOT sweep it into commits — always `git add` specific files. It is committed deliberately in Task 4.
- Public surface is exactly `PdfDocument.GetEmbeddedFiles()` + `EmbeddedFileDescriptor` (sealed, internal ctor). Reader and walker stay `internal`/`private`.
- Never throw for document content: per-entry failures (missing `/EF`, broken filter, junk nodes) degrade to `HasData = false` or skipped entries; no name tree → empty list.
- Version stays `2.4.1` until Task 4 bumps it to `2.5.0` (new public API = minor bump; 2.4.1 never published).
- Test command (from repo root): `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~EmbeddedFilesTests"`. The full suite run happens once, in Task 4.
- C# style: file-scoped namespaces, implicit usings (match `PdfLibrary/Document/OutputIntents.cs`), XML doc comments on all public members citing ISO 32000 clauses.

---

### Task 1: Descriptor model + reader (name-tree path) + `PdfDocument` accessor

**Files:**
- Create: `PdfLibrary/Document/EmbeddedFiles.cs`
- Modify: `PdfLibrary/Structure/PdfDocument.cs` (insert after `GetOutputIntents()`, ~line 415)
- Test: `PdfLibrary.Tests/Document/EmbeddedFilesTests.cs`

**Interfaces:**
- Consumes (all `internal` to PdfLibrary, reachable because the reader lives inside the assembly): `PdfDocument.GetCatalog()` → catalog with `.Dictionary`; `PdfDocument.ResolveReference(PdfIndirectReference)`; `PdfDocument.Decryptor`; `PdfStream.GetDecodedData(PdfDecryptor?)` (throws `NotSupportedException` on unknown `/Filter` — must be caught); primitives `PdfDictionary` (`Get`, `IsIndirect`, `ObjectNumber`), `PdfArray`, `PdfName.Value`, `PdfString.GetText()`.
- Produces (Tasks 2–3 rely on): `public sealed class EmbeddedFileDescriptor` in `PdfLibrary.Document` with members `string? Name`, `string? FileName`, `string? UnicodeFileName`, `string? Description`, `string? AfRelationship`, `string? MimeType`, `bool IsAssociated`, `bool HasData`, `byte[]? GetDataBytes()`, and internal ctor `(string? name, string? fileName, string? unicodeFileName, string? description, string? afRelationship, string? mimeType, bool isAssociated, byte[]? data)`; `internal static class EmbeddedFileReader` with `public static IReadOnlyList<EmbeddedFileDescriptor> Read(PdfDocument document)` and private static helpers `Describe`, `EnumerateNameTree`, `TextValue`, `Resolve`; `public IReadOnlyList<Document.EmbeddedFileDescriptor> PdfDocument.GetEmbeddedFiles()`.

- [ ] **Step 1: Create the branch**

```bash
cd /c/Users/jorda/RiderProjects/PDF
git checkout master
git checkout -b feat/embedded-files-read-api
```

- [ ] **Step 2: Write the failing tests**

Create `PdfLibrary.Tests/Document/EmbeddedFilesTests.cs`:

```csharp
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
}
```

- [ ] **Step 3: Run the tests to verify they fail**

```bash
cd /c/Users/jorda/RiderProjects/PDF
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~EmbeddedFilesTests"
```
Expected: **build FAILURE** — `EmbeddedFileDescriptor` and `GetEmbeddedFiles` do not exist yet.

- [ ] **Step 4: Implement the model + reader**

Create `PdfLibrary/Document/EmbeddedFiles.cs`:

```csharp
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Document;

/// <summary>
/// A read-only view of one embedded file: an entry of the catalog's <c>/Names /EmbeddedFiles</c> name
/// tree, or a catalog-level <c>/AF</c> associated file (ISO 32000-2, 7.11.4 / 14.13). Built with
/// <see cref="PdfDocument.GetEmbeddedFiles"/>.
/// </summary>
public sealed class EmbeddedFileDescriptor
{
    private readonly byte[]? _data;

    internal EmbeddedFileDescriptor(
        string? name, string? fileName, string? unicodeFileName, string? description,
        string? afRelationship, string? mimeType, bool isAssociated, byte[]? data)
    {
        Name = name;
        FileName = fileName;
        UnicodeFileName = unicodeFileName;
        Description = description;
        AfRelationship = afRelationship;
        MimeType = mimeType;
        IsAssociated = isAssociated;
        _data = data;
    }

    /// <summary>The entry's key in the <c>/EmbeddedFiles</c> name tree (what viewers list), or null for a
    /// file reachable only through the catalog's <c>/AF</c> array.</summary>
    public string? Name { get; }

    /// <summary>The file specification's <c>/F</c> file name, or null when absent.</summary>
    public string? FileName { get; }

    /// <summary>The file specification's <c>/UF</c> Unicode file name, or null when absent.</summary>
    public string? UnicodeFileName { get; }

    /// <summary>The file specification's <c>/Desc</c> description, or null when absent.</summary>
    public string? Description { get; }

    /// <summary>The file specification's <c>/AFRelationship</c> name (e.g. <c>"Alternative"</c>,
    /// <c>"Data"</c>, <c>"Source"</c>; ISO 32000-2, 14.13), or null when absent.</summary>
    public string? AfRelationship { get; }

    /// <summary>The embedded file stream's <c>/Subtype</c> MIME type (e.g. <c>"text/xml"</c>), or null
    /// when the stream or the key is absent.</summary>
    public string? MimeType { get; }

    /// <summary>True iff the file specification is referenced from the document catalog's <c>/AF</c>
    /// associated-files array (ISO 32000-2, 14.13) — as PDF/A-3 requires for e.g. Factur-X invoices.</summary>
    public bool IsAssociated { get; }

    /// <summary>True iff the embedded file stream resolved and its data decoded.</summary>
    public bool HasData => _data is not null;

    /// <summary>A defensive copy of the decoded embedded file bytes, or null when
    /// <see cref="HasData"/> is false.</summary>
    public byte[]? GetDataBytes() => _data is null ? null : (byte[])_data.Clone();
}

/// <summary>
/// Reads a document's embedded files — the catalog's <c>/Names /EmbeddedFiles</c> name tree plus
/// catalog-level <c>/AF</c> associated files — into public <see cref="EmbeddedFileDescriptor"/>s.
/// This deliberately duplicates the small catalog/name-tree walk that
/// <c>Conformance.ConformanceContext</c> performs internally — kept independent (rather than reused)
/// so this public reader never risks perturbing the load-bearing conformance suite.
/// Malformed content never throws: a failing entry degrades to metadata-only
/// (<see cref="EmbeddedFileDescriptor.HasData"/> = false) and junk trees yield what was reachable.
/// </summary>
internal static class EmbeddedFileReader
{
    public static IReadOnlyList<EmbeddedFileDescriptor> Read(PdfDocument document)
    {
        var result = new List<EmbeddedFileDescriptor>();
        PdfDictionary? catalog = document.GetCatalog()?.Dictionary;
        if (catalog is null)
            return result;

        // Object numbers of file specs the catalog's /AF array marks as associated files.
        var associated = new HashSet<int>();
        if (Resolve(document, catalog.Get("AF")) is PdfArray af)
            foreach (PdfObject entry in af)
                if (Resolve(document, entry) is PdfDictionary { IsIndirect: true } afSpec)
                    associated.Add(afSpec.ObjectNumber);

        if (Resolve(document, catalog.Get("Names")) is not PdfDictionary names)
            return result;

        foreach ((string? name, PdfObject value) in EnumerateNameTree(document, names.Get("EmbeddedFiles")))
        {
            if (Resolve(document, value) is not PdfDictionary spec)
                continue;
            bool isAssociated = spec.IsIndirect && associated.Contains(spec.ObjectNumber);
            result.Add(Describe(document, spec, name, isAssociated));
        }
        return result;
    }

    /// <summary>One descriptor from a /Filespec dictionary: metadata keys plus — when the /EF stream
    /// resolves and decodes — the file bytes. Decode failures degrade to HasData = false.</summary>
    private static EmbeddedFileDescriptor Describe(
        PdfDocument document, PdfDictionary spec, string? name, bool isAssociated)
    {
        string? fileName = TextValue(document, spec.Get("F"));
        string? unicodeFileName = TextValue(document, spec.Get("UF"));
        string? description = TextValue(document, spec.Get("Desc"));
        string? afRelationship = (Resolve(document, spec.Get("AFRelationship")) as PdfName)?.Value;

        string? mimeType = null;
        byte[]? data = null;
        if (Resolve(document, spec.Get("EF")) is PdfDictionary ef)
        {
            // Prefer the /UF stream, fall back to /F — the same preference the conformance rule uses.
            PdfStream? stream = Resolve(document, ef.Get("UF")) as PdfStream
                ?? Resolve(document, ef.Get("F")) as PdfStream;
            if (stream is not null)
            {
                mimeType = (Resolve(document, stream.Dictionary.Get("Subtype")) as PdfName)?.Value;
                try
                {
                    data = stream.GetDecodedData(document.Decryptor);
                }
                catch (Exception)
                {
                    // Unknown/broken filter, truncated data … — report the entry without bytes.
                    data = null;
                }
            }
        }

        return new EmbeddedFileDescriptor(
            name, fileName, unicodeFileName, description, afRelationship, mimeType, isAssociated, data);
    }

    /// <summary>
    /// Iterative name-tree walk yielding (key, value) pairs: leaf <c>/Names</c> arrays are flat
    /// <c>[key1 value1 key2 value2 …]</c>; intermediate nodes descend through <c>/Kids</c>. Guarded
    /// against indirect-node cycles and unboundedly deep/hostile trees (node budget), mirroring
    /// <c>ConformanceContext.EnumerateNameTree</c>.
    /// </summary>
    private static IEnumerable<(string? Name, PdfObject Value)> EnumerateNameTree(
        PdfDocument document, PdfObject? rootNode)
    {
        var visited = new HashSet<int>();
        var stack = new Stack<PdfObject?>();
        stack.Push(rootNode);

        for (int budget = 100_000; stack.Count > 0 && budget > 0; budget--)
        {
            if (Resolve(document, stack.Pop()) is not PdfDictionary node)
                continue;
            if (node.IsIndirect && !visited.Add(node.ObjectNumber))
                continue; // guards indirect-node cycles

            if (Resolve(document, node.Get("Names")) is PdfArray entries)
                for (int i = 1; i < entries.Count; i += 2)
                    yield return ((Resolve(document, entries[i - 1]) as PdfString)?.GetText(), entries[i]);

            if (Resolve(document, node.Get("Kids")) is PdfArray kids)
                foreach (PdfObject kid in kids)
                    stack.Push(kid);
        }
    }

    private static string? TextValue(PdfDocument document, PdfObject? obj) =>
        Resolve(document, obj) is PdfString s ? s.GetText() : null;

    private static PdfObject? Resolve(PdfDocument document, PdfObject? obj) =>
        obj is PdfIndirectReference reference ? document.ResolveReference(reference) : obj;
}
```

- [ ] **Step 5: Add the `PdfDocument` accessor**

In `PdfLibrary/Structure/PdfDocument.cs`, directly after the `GetOutputIntents()` member (~line 415), insert:

```csharp
    /// <summary>
    /// Reads the document's embedded files — the catalog's <c>/Names /EmbeddedFiles</c> name tree plus
    /// catalog-level <c>/AF</c> associated files (ISO 32000-2, 7.11.4 / 14.13) — as read-only
    /// <see cref="Document.EmbeddedFileDescriptor"/>s: names, file names, description, MIME subtype,
    /// associated-file relationship, and the decoded file bytes. Returns an empty list when the
    /// document embeds no files.
    /// </summary>
    public IReadOnlyList<Document.EmbeddedFileDescriptor> GetEmbeddedFiles() =>
        Document.EmbeddedFileReader.Read(this);
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~EmbeddedFilesTests"
```
Expected: PASS, 10 tests, 0 failures.

- [ ] **Step 7: Commit**

```bash
git add PdfLibrary/Document/EmbeddedFiles.cs PdfLibrary/Structure/PdfDocument.cs PdfLibrary.Tests/Document/EmbeddedFilesTests.cs
git commit -m "feat(document): public embedded-files read API — GetEmbeddedFiles()

EmbeddedFileReader walks the catalog /Names /EmbeddedFiles name tree
(iterative, cycle-guarded, node-budgeted) and decodes each /EF stream into
public EmbeddedFileDescriptors, following the OutputIntentReader pattern.
Content failures degrade per entry (HasData=false), never throw.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Catalog `/AF`-only entries + dedup

**Files:**
- Modify: `PdfLibrary/Document/EmbeddedFiles.cs` (the `Read` method only)
- Test: `PdfLibrary.Tests/Document/EmbeddedFilesTests.cs` (append tests)

**Interfaces:**
- Consumes: Task 1's `EmbeddedFileReader.Read`, `Describe`, `Resolve`; test helpers `Doc`, `AddFileSpec`, `RegisterInNameTree`, `N`, `Ref`.
- Produces: `Read` additionally returns descriptors (with `Name = null`, `IsAssociated = true`) for file specs referenced from the catalog `/AF` array but absent from the name tree; a spec referenced from both places yields exactly one descriptor.

- [ ] **Step 1: Write the failing tests**

Append to `PdfLibrary.Tests/Document/EmbeddedFilesTests.cs` (inside the class, after `SpecReferencedFromCatalogAF_isAssociated`):

```csharp
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
```

- [ ] **Step 2: Run the tests to verify the new ones fail**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~EmbeddedFilesTests"
```
Expected: 12 tests, **1 failure** — `AfOnlySpec_isYieldedWithNullName` (empty list). (`SpecInBothNameTreeAndAF_yieldsOneDescriptor` passes already: the union does not exist yet, so no duplicate is possible — it becomes the regression guard for this task's change.)

- [ ] **Step 3: Implement the union**

In `PdfLibrary/Document/EmbeddedFiles.cs`, replace the whole `Read` method with:

```csharp
    public static IReadOnlyList<EmbeddedFileDescriptor> Read(PdfDocument document)
    {
        var result = new List<EmbeddedFileDescriptor>();
        PdfDictionary? catalog = document.GetCatalog()?.Dictionary;
        if (catalog is null)
            return result;

        // Object numbers of file specs the catalog's /AF array marks as associated files.
        var associated = new HashSet<int>();
        if (Resolve(document, catalog.Get("AF")) is PdfArray af)
            foreach (PdfObject entry in af)
                if (Resolve(document, entry) is PdfDictionary { IsIndirect: true } afSpec)
                    associated.Add(afSpec.ObjectNumber);

        // Primary registry: the /Names /EmbeddedFiles name tree.
        var yielded = new HashSet<int>();
        if (Resolve(document, catalog.Get("Names")) is PdfDictionary names)
        {
            foreach ((string? name, PdfObject value) in EnumerateNameTree(document, names.Get("EmbeddedFiles")))
            {
                if (Resolve(document, value) is not PdfDictionary spec)
                    continue;
                if (spec.IsIndirect)
                    yielded.Add(spec.ObjectNumber);
                bool isAssociated = spec.IsIndirect && associated.Contains(spec.ObjectNumber);
                result.Add(Describe(document, spec, name, isAssociated));
            }
        }

        // Union: catalog /AF specs the name tree did not already yield (a Factur-X file references the
        // SAME spec from both places — that must stay one descriptor, carrying its name-tree identity).
        if (Resolve(document, catalog.Get("AF")) is PdfArray afArray)
            foreach (PdfObject entry in afArray)
                if (Resolve(document, entry) is PdfDictionary { IsIndirect: true } spec
                    && !yielded.Contains(spec.ObjectNumber))
                {
                    yielded.Add(spec.ObjectNumber);
                    result.Add(Describe(document, spec, name: null, isAssociated: true));
                }

        return result;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~EmbeddedFilesTests"
```
Expected: PASS, 12 tests, 0 failures.

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Document/EmbeddedFiles.cs PdfLibrary.Tests/Document/EmbeddedFilesTests.cs
git commit -m "feat(document): GetEmbeddedFiles unions catalog /AF-only associated files

Specs referenced only from the catalog /AF array (not the name tree) are
yielded with Name=null, IsAssociated=true; a spec in both registries stays
a single descriptor keyed by object number.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Real-world Factur-X fixture + integration test

**Files:**
- Create: `PdfLibrary.Tests/Resources/MINIMUM_Rechnung_fx.pdf` (copied from the EInvoice repo's standards tree)
- Create: `PdfLibrary.Tests/Resources/MINIMUM_Rechnung_fx.LICENSE.txt`
- Modify: `.gitignore` (un-ignore the fixture, following the `conformant-pdfa2b.pdf` precedent)
- Modify: `PdfLibrary.Tests/PdfLibrary.Tests.csproj` (CopyToOutputDirectory entries)
- Test: `PdfLibrary.Tests/Document/EmbeddedFilesTests.cs` (append one test)

**Interfaces:**
- Consumes: Task 2's complete `GetEmbeddedFiles()` behavior; `PdfDocument.Load(string)`.
- Produces: a committed, license-attributed real PDF/A-3 Factur-X fixture usable by future tests.

- [ ] **Step 1: Copy the fixture**

```bash
cd /c/Users/jorda/RiderProjects/PDF
cp "/c/Users/jorda/RiderProjects/EInvoice/standards/ZUGFeRD-2.5/examples/0. MINIMUM/MINIMUM_Rechnung/MINIMUM_Rechnung_fx.pdf" PdfLibrary.Tests/Resources/MINIMUM_Rechnung_fx.pdf
```

Create `PdfLibrary.Tests/Resources/MINIMUM_Rechnung_fx.LICENSE.txt`:

```text
MINIMUM_Rechnung_fx.pdf is the official "MINIMUM_Rechnung" example from the ZUGFeRD 2.5 /
Factur-X 1.09 distribution, © Forum elektronische Rechnung Deutschland (FeRD) / FNFE-MPE, used
under the FeRD license, which grants an irrevocable, royalty-free right to use the
ZUGFeRD/Factur-X data format specification and its artifacts, including in commercial software.
It is a PDF/A-3 invoice carrying an embedded factur-x.xml (CII) attachment and is used as a
real-world fixture for the embedded-files read API.
Specification: https://www.ferd-net.de (ZUGFeRD) / https://fnfe-mpe.org (Factur-X).
```

- [ ] **Step 2: Un-ignore the fixture**

In `.gitignore`, directly after the existing `!PdfLibrary.Tests/Resources/conformant-pdfa2b.pdf` line, add:

```gitignore
# Official ZUGFeRD 2.5 / Factur-X example (FeRD license) — real PDF/A-3 embedded-files fixture
!PdfLibrary.Tests/Resources/MINIMUM_Rechnung_fx.pdf
```

Verify: `git status --short` now shows `PdfLibrary.Tests/Resources/MINIMUM_Rechnung_fx.pdf` and the `.LICENSE.txt` as untracked (not ignored).

- [ ] **Step 3: Copy the fixture to the test output**

In `PdfLibrary.Tests/PdfLibrary.Tests.csproj`, directly after the two `conformant-pdfa2b` `<None Update=…/>` lines (~line 51), add:

```xml
        <!-- Official ZUGFeRD 2.5 / Factur-X example invoice (FeRD license; see
             Resources/MINIMUM_Rechnung_fx.LICENSE.txt) — real PDF/A-3 embedded-files fixture. -->
        <None Update="Resources\MINIMUM_Rechnung_fx.pdf" CopyToOutputDirectory="PreserveNewest" />
        <None Update="Resources\MINIMUM_Rechnung_fx.LICENSE.txt" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 4: Write the integration test**

Append to `PdfLibrary.Tests/Document/EmbeddedFilesTests.cs` (inside the class), and add `using System.IO;`, `using System.Linq;` and `using System.Xml.Linq;` to the file's usings:

```csharp
    // ── real-world fixture ───────────────────────────────────────────────────

    [Fact]
    public void FacturXInvoice_yieldsDecodableCiiXml()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Resources", "MINIMUM_Rechnung_fx.pdf");
        using PdfDocument doc = PdfDocument.Load(path);

        IReadOnlyList<EmbeddedFileDescriptor> files = doc.GetEmbeddedFiles();
        EmbeddedFileDescriptor facturX = files.Single(x =>
            x.Name == "factur-x.xml" || x.FileName == "factur-x.xml" || x.UnicodeFileName == "factur-x.xml");

        Assert.True(facturX.HasData);
        Assert.True(facturX.IsAssociated);            // Factur-X mandates the catalog /AF reference
        Assert.NotNull(facturX.AfRelationship);       // …and an /AFRelationship on the spec
        Assert.Equal("text/xml", facturX.MimeType);

        XDocument xml = XDocument.Load(new MemoryStream(facturX.GetDataBytes()!));
        Assert.Equal("CrossIndustryInvoice", xml.Root!.Name.LocalName);
    }
```

Note: `HasData`, `IsAssociated`, and the `CrossIndustryInvoice` root are hard requirements (the Factur-X standard mandates them). If a *metadata* assertion (`MimeType`, `AfRelationship`) fails against the real file, inspect the actual value the fixture carries and tighten the assertion to that observed value — the fixture is the oracle, not our expectation.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~EmbeddedFilesTests"
```
Expected: PASS, 13 tests, 0 failures.

- [ ] **Step 6: Commit**

```bash
git add PdfLibrary.Tests/Resources/MINIMUM_Rechnung_fx.pdf PdfLibrary.Tests/Resources/MINIMUM_Rechnung_fx.LICENSE.txt .gitignore PdfLibrary.Tests/PdfLibrary.Tests.csproj PdfLibrary.Tests/Document/EmbeddedFilesTests.cs
git commit -m "test(document): real Factur-X PDF/A-3 fixture exercises GetEmbeddedFiles end-to-end

Official ZUGFeRD 2.5 MINIMUM example (FeRD license) committed under
Tests/Resources; asserts the embedded factur-x.xml decodes to CII XML with
the mandated /AF association.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: CHANGELOG, version 2.5.0, pending pack-local fix, full-suite gate

**Files:**
- Modify: `CHANGELOG.md` (under `## [Unreleased]`)
- Modify: `PdfLibrary/PdfLibrary.csproj` (line 11, `<Version>`)
- Commit as-is: `pack-local.ps1` (the pre-existing uncommitted robustness fix)

**Interfaces:**
- Consumes: Tasks 1–3 merged state (all EmbeddedFilesTests green).
- Produces: a release-ready branch; version `2.5.0`; changelog entry.

- [ ] **Step 1: Commit the pre-existing pack-local.ps1 fix (its own commit)**

The working tree carries an uncommitted two-line fix making the `<Version>` read robust when the XML node exposes `XmlElement` children. Verify it is still exactly that (`git diff pack-local.ps1` shows only the two `ForEach-Object { if ($_ -is [string]) … InnerText }` insertions), then:

```bash
cd /c/Users/jorda/RiderProjects/PDF
git add pack-local.ps1
git commit -m "fix(pack-local): read <Version> robustly when PropertyGroup yields XmlElement nodes

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 2: CHANGELOG entry**

In `CHANGELOG.md`, replace the bare `## [Unreleased]` line with:

```markdown
## [Unreleased]

### Added

- **Embedded-files read API** — `PdfDocument.GetEmbeddedFiles()` returns read-only
  `EmbeddedFileDescriptor`s for the catalog's `/Names /EmbeddedFiles` name tree plus catalog-level
  `/AF` associated files (ISO 32000-2, 7.11.4 / 14.13): the name-tree key, `/F` and `/UF` file
  names, `/Desc`, `/AFRelationship`, the stream's MIME `/Subtype`, catalog-`/AF` membership, and
  the decoded file bytes. Content failures degrade per entry (`HasData = false`) — the reader
  never throws on malformed documents. First consumer: the EInvoice Factur-X bridge (extracting
  `factur-x.xml` from PDF/A-3 invoices); the API is generic to any embedded attachment.
```

- [ ] **Step 3: Version bump**

In `PdfLibrary/PdfLibrary.csproj` line 11, change:

```xml
        <Version>2.4.1</Version>
```
to:
```xml
        <Version>2.5.0</Version>
```
(New public API = minor bump; 2.4.1 was never published, so it is simply renamed.)

- [ ] **Step 4: Full-suite gate**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj
```
Expected: PASS, 0 failures (corpus/parity/golden tests that depend on external assets skip or pass vacuously by design — see `GoldenPdf_Parses`; several minutes is normal). Any NEW failure relative to `master` blocks this task.

- [ ] **Step 5: Commit**

```bash
git add CHANGELOG.md PdfLibrary/PdfLibrary.csproj
git commit -m "chore(release): 2.5.0 — embedded-files read API changelog + version

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Post-completion (not plan tasks)

- Merge `feat/embedded-files-read-api` into `master` with `--no-ff`; verify the full suite on the merged result. NO push, NO nuget publish (standing gates).
- Return to the EInvoice project to design the consumption story (pack-local dev feed vs published 2.5.0) and the `EInvoice.FacturX` bridge — the user asked to discuss this once the PdfLibrary work is complete.
