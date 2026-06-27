using PdfLibrary.Builder;
using PdfLibrary.Builder.Bookmark;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Optimization;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class NamedDestEditTests
{
    private static byte[] ThreePageDoc() =>
        PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("PAGE 0", 100, 700))
            .AddPage(p => p.AddText("PAGE 1", 100, 700))
            .AddPage(p => p.AddText("PAGE 2", 100, 700))
            .ToByteArray();

    private static byte[] SaveReload(PdfDocument doc)
    {
        using var ms = new MemoryStream();
        doc.Edit().Save(ms, new PdfSaveOptions { RemoveOrphans = true });
        return ms.ToArray();
    }

    private static PdfObject? Deref(PdfDocument doc, PdfObject? o) =>
        o is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : o;

    // ── Set / Get ──────────────────────────────────────────────────────────

    [Fact]
    public void Set_Get_ReturnsCorrectDestination()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();

        edit.NamedDestinations.Set("intro", PdfDestination.FitPage(1));

        PdfDestination? got = edit.NamedDestinations.Get("intro");
        Assert.NotNull(got);
        Assert.Equal(1, got!.PageIndex);
        Assert.Equal(PdfDestinationType.Fit, got.Type);
    }

    [Fact]
    public void Get_MissingName_ReturnsNull()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();

        Assert.Null(edit.NamedDestinations.Get("nonexistent"));
    }

    // ── Count / Enumeration ────────────────────────────────────────────────

    [Fact]
    public void Count_ReflectsSetAndRemove()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();

        Assert.Empty(edit.NamedDestinations);

        edit.NamedDestinations.Set("a", PdfDestination.FitPage(0));
        edit.NamedDestinations.Set("b", PdfDestination.FitPage(1));
        Assert.Equal(2, edit.NamedDestinations.Count);

        edit.NamedDestinations.Remove("a");
        Assert.Single(edit.NamedDestinations);
    }

    [Fact]
    public void Enumerate_YieldsAllNames()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();

        edit.NamedDestinations.Set("z", PdfDestination.FitPage(2));
        edit.NamedDestinations.Set("a", PdfDestination.FitPage(0));
        edit.NamedDestinations.Set("m", PdfDestination.FitPage(1));

        List<string> names = edit.NamedDestinations.ToList();
        Assert.Contains("a", names);
        Assert.Contains("m", names);
        Assert.Contains("z", names);
        Assert.Equal(3, names.Count);
    }

    // ── Names array sorted by name ─────────────────────────────────────────

    [Fact]
    public void NamesArray_IsSortedByName_AfterMultipleSets()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();

        // Insert out of alphabetical order
        edit.NamedDestinations.Set("zebra", PdfDestination.FitPage(0));
        edit.NamedDestinations.Set("apple", PdfDestination.FitPage(1));
        edit.NamedDestinations.Set("mango", PdfDestination.FitPage(2));

        byte[] bytes = SaveReload(doc);
        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(bytes));

        // Navigate to /Names /Dests /Names array
        PdfDictionary catalog = reloaded.CatalogDictionary!;
        var namesDict = (PdfDictionary)Deref(reloaded, catalog.Get(new PdfName("Names")))!;
        var destsTree = (PdfDictionary)Deref(reloaded, namesDict.Get(new PdfName("Dests")))!;
        var namesArr = (PdfArray)Deref(reloaded, destsTree.Get(new PdfName("Names")))!;

        // Pairs: [name, dest, name, dest, ...]
        var keys = new List<string>();
        for (var i = 0; i < namesArr.Count; i += 2)
            keys.Add(((PdfString)namesArr[i]).Value);

        Assert.Equal(new[] { "apple", "mango", "zebra" }, keys);
    }

    // ── Rename ─────────────────────────────────────────────────────────────

    [Fact]
    public void Rename_UpdatesNamePreservesDestination()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();

        edit.NamedDestinations.Set("old", PdfDestination.FitPage(2));
        bool ok = edit.NamedDestinations.Rename("old", "new");

        Assert.True(ok);
        Assert.Null(edit.NamedDestinations.Get("old"));
        PdfDestination? d = edit.NamedDestinations.Get("new");
        Assert.NotNull(d);
        Assert.Equal(2, d!.PageIndex);
    }

    [Fact]
    public void Rename_MissingName_ReturnsFalse()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();

        bool ok = edit.NamedDestinations.Rename("nonexistent", "whatever");
        Assert.False(ok);
    }

    // ── Remove ─────────────────────────────────────────────────────────────

    [Fact]
    public void Remove_ExistingName_ReturnsTrueAndRemoves()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();

        edit.NamedDestinations.Set("toc", PdfDestination.FitPage(0));
        bool ok = edit.NamedDestinations.Remove("toc");

        Assert.True(ok);
        Assert.Null(edit.NamedDestinations.Get("toc"));
    }

    [Fact]
    public void Remove_MissingName_ReturnsFalse()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();

        Assert.False(edit.NamedDestinations.Remove("ghost"));
    }

    // ── Save / Reload round-trip ────────────────────────────────────────────

    [Fact]
    public void Destinations_SurviveSaveReload()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();

        edit.NamedDestinations.Set("ch1", PdfDestination.FitPage(0));
        edit.NamedDestinations.Set("ch2", PdfDestination.FitWidth(1, 600));
        edit.NamedDestinations.Set("ch3", PdfDestination.At(2, 50, 700, null));

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));
        PdfNamedDestinations nd = reloaded.Edit().NamedDestinations;

        PdfDestination? ch1 = nd.Get("ch1");
        Assert.NotNull(ch1);
        Assert.Equal(0, ch1!.PageIndex);
        Assert.Equal(PdfDestinationType.Fit, ch1.Type);

        PdfDestination? ch2 = nd.Get("ch2");
        Assert.NotNull(ch2);
        Assert.Equal(1, ch2!.PageIndex);
        Assert.Equal(PdfDestinationType.FitH, ch2.Type);
        Assert.Equal(600, ch2.Top);

        PdfDestination? ch3 = nd.Get("ch3");
        Assert.NotNull(ch3);
        Assert.Equal(2, ch3!.PageIndex);
        Assert.Equal(PdfDestinationType.XYZ, ch3.Type);
        Assert.Equal(50, ch3.Left);
        Assert.Equal(700, ch3.Top);
        Assert.Null(ch3.Zoom);
    }

    // ── Legacy /Dests dict ─────────────────────────────────────────────────

    [Fact]
    public void Read_LegacyDestsDict_CanGetAndUpdate()
    {
        // Build a document with a legacy /Dests entry injected directly
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        doc.Edit(); // materialize

        // Inject a legacy /Dests dict
        PdfDictionary catalog = doc.CatalogDictionary!;

        // Resolve page 0 indirect ref for the legacy dest
        PdfArray kids = PageTreeOps.Kids(doc);
        var page0Ref = (PdfIndirectReference)kids[0];

        var legacyDestArr = new PdfArray(page0Ref, new PdfName("Fit"));
        var legacyDests = new PdfDictionary
        {
            [new PdfName("legacyName")] = legacyDestArr
        };
        catalog[new PdfName("Dests")] = doc.RegisterObject(legacyDests);

        // Now read via the facade
        PdfDocumentEditor edit = doc.Edit();
        PdfDestination? got = edit.NamedDestinations.Get("legacyName");
        Assert.NotNull(got);
        Assert.Equal(0, got!.PageIndex);
        Assert.Equal(PdfDestinationType.Fit, got.Type);

        // Update in-place in the legacy dict
        edit.NamedDestinations.Set("legacyName", PdfDestination.FitPage(2));
        PdfDestination? updated = edit.NamedDestinations.Get("legacyName");
        Assert.NotNull(updated);
        Assert.Equal(2, updated!.PageIndex);

        // Should still be in /Dests (not moved to tree)
        Assert.NotNull(Deref(doc, catalog.Get(new PdfName("Dests"))));
    }

    // ── GC survival ─────────────────────────────────────────────────────────

    [Fact]
    public void NameTree_SurvivesGcUnderObjectStreams()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();

        edit.NamedDestinations.Set("s1", PdfDestination.FitPage(0));
        edit.NamedDestinations.Set("s2", PdfDestination.FitPage(1));

        using var ms = new MemoryStream();
        edit.Save(ms, new PdfSaveOptions { RemoveOrphans = true, UseObjectStreams = true });

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(ms.ToArray()));
        PdfNamedDestinations nd = reloaded.Edit().NamedDestinations;

        Assert.NotNull(nd.Get("s1"));
        Assert.NotNull(nd.Get("s2"));
    }
}
