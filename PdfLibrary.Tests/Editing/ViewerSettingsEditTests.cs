using PdfLibrary.Builder;
using PdfLibrary.Builder.Bookmark;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Optimization;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class ViewerSettingsEditTests
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

    // ── PageMode ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PdfPageMode.UseNone, "UseNone")]
    [InlineData(PdfPageMode.UseOutlines, "UseOutlines")]
    [InlineData(PdfPageMode.UseThumbs, "UseThumbs")]
    [InlineData(PdfPageMode.FullScreen, "FullScreen")]
    [InlineData(PdfPageMode.UseOC, "UseOC")]
    [InlineData(PdfPageMode.UseAttachments, "UseAttachments")]
    public void PageMode_RoundTripsThroughSaveReload(PdfPageMode mode, string expectedName)
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.ViewerSettings.PageMode = mode;

        // Verify the catalog entry before save
        PdfDictionary catalog = doc.CatalogDictionary!;
        PdfObject? pmObj = catalog.Get(new PdfName("PageMode"));
        Assert.NotNull(pmObj);
        Assert.Equal(expectedName, ((PdfName)pmObj!).Value);

        // Save and reload
        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));
        PdfPageMode? reloadedMode = reloaded.Edit().ViewerSettings.PageMode;
        Assert.Equal(mode, reloadedMode);
    }

    [Fact]
    public void PageMode_Null_WhenAbsent()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        Assert.Null(doc.Edit().ViewerSettings.PageMode);
    }

    // ── PageLayout ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PdfPageLayout.SinglePage, "SinglePage")]
    [InlineData(PdfPageLayout.OneColumn, "OneColumn")]
    [InlineData(PdfPageLayout.TwoColumnLeft, "TwoColumnLeft")]
    [InlineData(PdfPageLayout.TwoColumnRight, "TwoColumnRight")]
    [InlineData(PdfPageLayout.TwoPageLeft, "TwoPageLeft")]
    [InlineData(PdfPageLayout.TwoPageRight, "TwoPageRight")]
    public void PageLayout_RoundTripsThroughSaveReload(PdfPageLayout layout, string expectedName)
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.ViewerSettings.PageLayout = layout;

        PdfDictionary catalog = doc.CatalogDictionary!;
        PdfObject? plObj = catalog.Get(new PdfName("PageLayout"));
        Assert.NotNull(plObj);
        Assert.Equal(expectedName, ((PdfName)plObj!).Value);

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));
        PdfPageLayout? reloadedLayout = reloaded.Edit().ViewerSettings.PageLayout;
        Assert.Equal(layout, reloadedLayout);
    }

    [Fact]
    public void PageLayout_Null_WhenAbsent()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        Assert.Null(doc.Edit().ViewerSettings.PageLayout);
    }

    // ── OpenAction ────────────────────────────────────────────────────────

    [Fact]
    public void OpenAction_SetDest_RoundTripsThroughSaveReload()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.ViewerSettings.OpenAction = PdfDestination.FitPage(2);

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));
        PdfDestination? oa = reloaded.Edit().ViewerSettings.OpenAction;

        Assert.NotNull(oa);
        Assert.Equal(2, oa!.PageIndex);
        Assert.Equal(PdfDestinationType.Fit, oa.Type);
    }

    [Fact]
    public void OpenAction_IsDestinationArray_NotGoToActionDict()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.ViewerSettings.OpenAction = PdfDestination.FitWidth(1, 750);

        PdfDictionary catalog = doc.CatalogDictionary!;
        PdfObject? oa = Deref(doc, catalog.Get(new PdfName("OpenAction")));

        // Must be an array (dest form), not a dict (action form)
        Assert.IsType<PdfArray>(oa);
        var arr = (PdfArray)oa!;
        Assert.IsType<PdfIndirectReference>(arr[0]);
        Assert.Equal("FitH", ((PdfName)arr[1]).Value);
    }

    [Fact]
    public void OpenAction_Null_WhenAbsent()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        Assert.Null(doc.Edit().ViewerSettings.OpenAction);
    }

    [Fact]
    public void OpenAction_FitWidth_TopCoordPreserved()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.ViewerSettings.OpenAction = PdfDestination.FitWidth(0, 650);

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));
        PdfDestination? oa = reloaded.Edit().ViewerSettings.OpenAction;

        Assert.NotNull(oa);
        Assert.Equal(PdfDestinationType.FitH, oa!.Type);
        Assert.Equal(650, oa.Top);
    }

    // ── ViewerPreferences booleans ────────────────────────────────────────

    [Fact]
    public void HideToolbar_RoundTripsThroughSaveReload()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.ViewerSettings.HideToolbar = true;

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));
        Assert.Equal(true, reloaded.Edit().ViewerSettings.HideToolbar);
    }

    [Fact]
    public void FitWindow_RoundTripsThroughSaveReload()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.ViewerSettings.FitWindow = true;

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));
        Assert.Equal(true, reloaded.Edit().ViewerSettings.FitWindow);
    }

    [Fact]
    public void CenterWindow_RoundTripsThroughSaveReload()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.ViewerSettings.CenterWindow = true;

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));
        Assert.Equal(true, reloaded.Edit().ViewerSettings.CenterWindow);
    }

    [Fact]
    public void DisplayDocTitle_RoundTripsThroughSaveReload()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.ViewerSettings.DisplayDocTitle = true;

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));
        Assert.Equal(true, reloaded.Edit().ViewerSettings.DisplayDocTitle);
    }

    [Fact]
    public void Booleans_Null_WhenAbsent()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfViewerSettings vs = doc.Edit().ViewerSettings;
        Assert.Null(vs.HideToolbar);
        Assert.Null(vs.FitWindow);
        Assert.Null(vs.CenterWindow);
        Assert.Null(vs.DisplayDocTitle);
    }

    [Fact]
    public void AllFourBooleans_RoundTripTogether()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.ViewerSettings.HideToolbar = true;
        edit.ViewerSettings.FitWindow = false;
        edit.ViewerSettings.CenterWindow = true;
        edit.ViewerSettings.DisplayDocTitle = false;

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));
        PdfViewerSettings vs = reloaded.Edit().ViewerSettings;
        Assert.Equal(true, vs.HideToolbar);
        Assert.Equal(false, vs.FitWindow);
        Assert.Equal(true, vs.CenterWindow);
        Assert.Equal(false, vs.DisplayDocTitle);
    }

    // ── ViewerPreferences dict is referenced from catalog ──────────────────

    [Fact]
    public void ViewerPreferences_SubDict_IsReferencedFromCatalog()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.ViewerSettings.HideToolbar = true;

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));
        PdfDictionary catalog = reloaded.CatalogDictionary!;

        // /ViewerPreferences must exist in catalog and resolve to a dict
        PdfObject? vpRef = catalog.Get(new PdfName("ViewerPreferences"));
        Assert.NotNull(vpRef);
        PdfObject? vpObj = Deref(reloaded, vpRef);
        Assert.IsType<PdfDictionary>(vpObj);

        var vpDict = (PdfDictionary)vpObj!;
        var hideObj = (PdfBoolean)vpDict.Get(new PdfName("HideToolbar"))!;
        Assert.True(hideObj.Value);
    }

    // ── Combined settings round-trip ────────────────────────────────────────

    [Fact]
    public void AllSettings_RoundTripTogether()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.ViewerSettings.PageMode = PdfPageMode.UseOutlines;
        edit.ViewerSettings.PageLayout = PdfPageLayout.TwoColumnLeft;
        edit.ViewerSettings.OpenAction = PdfDestination.FitPage(1);
        edit.ViewerSettings.HideToolbar = true;
        edit.ViewerSettings.DisplayDocTitle = true;

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));
        PdfViewerSettings vs = reloaded.Edit().ViewerSettings;

        Assert.Equal(PdfPageMode.UseOutlines, vs.PageMode);
        Assert.Equal(PdfPageLayout.TwoColumnLeft, vs.PageLayout);
        Assert.NotNull(vs.OpenAction);
        Assert.Equal(1, vs.OpenAction!.PageIndex);
        Assert.Equal(true, vs.HideToolbar);
        Assert.Equal(true, vs.DisplayDocTitle);
    }

    // ── GC survival ────────────────────────────────────────────────────────

    [Fact]
    public void ViewerSettings_SurviveGcUnderObjectStreams()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.ViewerSettings.PageMode = PdfPageMode.FullScreen;
        edit.ViewerSettings.HideToolbar = true;

        using var ms = new MemoryStream();
        edit.Save(ms, new PdfSaveOptions { RemoveOrphans = true, UseObjectStreams = true });

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(ms.ToArray()));
        PdfViewerSettings vs = reloaded.Edit().ViewerSettings;
        Assert.Equal(PdfPageMode.FullScreen, vs.PageMode);
        Assert.Equal(true, vs.HideToolbar);
    }
}
