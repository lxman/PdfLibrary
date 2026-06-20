using PdfLibrary.Builder.Bookmark;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing;

/// <summary>
/// A live view of the document's viewer settings:
/// <c>/PageMode</c>, <c>/PageLayout</c>, <c>/OpenAction</c> (destination form),
/// and the four <c>/ViewerPreferences</c> booleans.
/// </summary>
public sealed class PdfViewerSettings
{
    private readonly PdfDocument _document;

    internal PdfViewerSettings(PdfDocument document) => _document = document;

    // ── PageMode ───────────────────────────────────────────────────────────

    /// <summary>Gets or sets the document's page mode (<c>/Catalog /PageMode</c>).</summary>
    public PdfPageMode? PageMode
    {
        get
        {
            PdfObject? obj = Catalog?.Get(new PdfName("PageMode"));
            return obj is PdfName n ? NameToPageMode(n.Value) : null;
        }
        set
        {
            if (Catalog is not { } cat) return;
            if (value.HasValue)
                cat[new PdfName("PageMode")] = new PdfName(PageModeToName(value.Value));
            else
                cat.Remove(new PdfName("PageMode"));
        }
    }

    // ── PageLayout ────────────────────────────────────────────────────────

    /// <summary>Gets or sets the document's page layout (<c>/Catalog /PageLayout</c>).</summary>
    public PdfPageLayout? PageLayout
    {
        get
        {
            PdfObject? obj = Catalog?.Get(new PdfName("PageLayout"));
            return obj is PdfName n ? NameToPageLayout(n.Value) : null;
        }
        set
        {
            if (Catalog is not { } cat) return;
            if (value.HasValue)
                cat[new PdfName("PageLayout")] = new PdfName(PageLayoutToName(value.Value));
            else
                cat.Remove(new PdfName("PageLayout"));
        }
    }

    // ── OpenAction ────────────────────────────────────────────────────────

    /// <summary>
    /// Gets or sets the document's open action as a destination array
    /// (<c>/Catalog /OpenAction</c>). Destination form only (not GoTo action dict).
    /// </summary>
    public PdfDestination? OpenAction
    {
        get
        {
            PdfObject? obj = Catalog?.Get(new PdfName("OpenAction"));
            if (obj is null) return null;
            // Resolve indirect ref
            PdfObject? resolved = Resolve(obj);
            return resolved is PdfArray arr ? DestinationCodec.Decode(_document, arr) : null;
        }
        set
        {
            if (Catalog is not { } cat) return;
            if (value is null)
            {
                cat.Remove(new PdfName("OpenAction"));
                return;
            }

            PdfArray kids = PageTreeOps.Kids(_document);
            if (value.PageIndex < 0 || value.PageIndex >= kids.Count)
                throw new ArgumentOutOfRangeException(nameof(value), $"Page index {value.PageIndex} is out of range.");
            var pageRef = (PdfIndirectReference)kids[value.PageIndex];
            PdfArray destArr = DestinationCodec.Encode(value, pageRef);
            cat[new PdfName("OpenAction")] = destArr;
        }
    }

    // ── ViewerPreferences booleans ────────────────────────────────────────

    /// <summary>Gets or sets <c>/ViewerPreferences /HideToolbar</c>.</summary>
    public bool? HideToolbar
    {
        get => GetViewerPref("HideToolbar");
        set => SetViewerPref("HideToolbar", value);
    }

    /// <summary>Gets or sets <c>/ViewerPreferences /FitWindow</c>.</summary>
    public bool? FitWindow
    {
        get => GetViewerPref("FitWindow");
        set => SetViewerPref("FitWindow", value);
    }

    /// <summary>Gets or sets <c>/ViewerPreferences /CenterWindow</c>.</summary>
    public bool? CenterWindow
    {
        get => GetViewerPref("CenterWindow");
        set => SetViewerPref("CenterWindow", value);
    }

    /// <summary>Gets or sets <c>/ViewerPreferences /DisplayDocTitle</c>.</summary>
    public bool? DisplayDocTitle
    {
        get => GetViewerPref("DisplayDocTitle");
        set => SetViewerPref("DisplayDocTitle", value);
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private PdfDictionary? Catalog => _document.CatalogDictionary;

    private PdfObject? Resolve(PdfObject? obj) =>
        obj is PdfIndirectReference r ? _document.GetObject(r.ObjectNumber) : obj;

    private bool? GetViewerPref(string key)
    {
        PdfDictionary? vp = GetViewerPrefsDict(create: false);
        if (vp is null) return null;
        PdfObject? obj = vp.Get(new PdfName(key));
        return obj is PdfBoolean b ? b.Value : null;
    }

    private void SetViewerPref(string key, bool? value)
    {
        if (value is null) return; // per spec: null means absent; no removal required
        PdfDictionary vp = GetViewerPrefsDict(create: true)!;
        vp[new PdfName(key)] = PdfBoolean.FromValue(value.Value);
    }

    /// <summary>
    /// Gets (or creates on first write) the <c>/ViewerPreferences</c> sub-dict.
    /// When <paramref name="create"/> is false returns null if absent.
    /// </summary>
    private PdfDictionary? GetViewerPrefsDict(bool create)
    {
        if (Catalog is not { } cat) return null;

        PdfObject? existing = cat.Get(new PdfName("ViewerPreferences"));
        if (Resolve(existing) is PdfDictionary dict) return dict;

        if (!create) return null;

        var newDict = new PdfDictionary();
        cat[new PdfName("ViewerPreferences")] = _document.RegisterObject(newDict);
        return newDict;
    }

    // ── Enum ↔ name mappings ───────────────────────────────────────────────

    private static string PageModeToName(PdfPageMode mode) => mode switch
    {
        PdfPageMode.UseNone        => "UseNone",
        PdfPageMode.UseOutlines    => "UseOutlines",
        PdfPageMode.UseThumbs      => "UseThumbs",
        PdfPageMode.FullScreen     => "FullScreen",
        PdfPageMode.UseOC          => "UseOC",
        PdfPageMode.UseAttachments => "UseAttachments",
        _                          => "UseNone"
    };

    private static PdfPageMode? NameToPageMode(string name) => name switch
    {
        "UseNone"        => PdfPageMode.UseNone,
        "UseOutlines"    => PdfPageMode.UseOutlines,
        "UseThumbs"      => PdfPageMode.UseThumbs,
        "FullScreen"     => PdfPageMode.FullScreen,
        "UseOC"          => PdfPageMode.UseOC,
        "UseAttachments" => PdfPageMode.UseAttachments,
        _                => null
    };

    private static string PageLayoutToName(PdfPageLayout layout) => layout switch
    {
        PdfPageLayout.SinglePage      => "SinglePage",
        PdfPageLayout.OneColumn       => "OneColumn",
        PdfPageLayout.TwoColumnLeft   => "TwoColumnLeft",
        PdfPageLayout.TwoColumnRight  => "TwoColumnRight",
        PdfPageLayout.TwoPageLeft     => "TwoPageLeft",
        PdfPageLayout.TwoPageRight    => "TwoPageRight",
        _                             => "SinglePage"
    };

    private static PdfPageLayout? NameToPageLayout(string name) => name switch
    {
        "SinglePage"     => PdfPageLayout.SinglePage,
        "OneColumn"      => PdfPageLayout.OneColumn,
        "TwoColumnLeft"  => PdfPageLayout.TwoColumnLeft,
        "TwoColumnRight" => PdfPageLayout.TwoColumnRight,
        "TwoPageLeft"    => PdfPageLayout.TwoPageLeft,
        "TwoPageRight"   => PdfPageLayout.TwoPageRight,
        _                => null
    };
}
