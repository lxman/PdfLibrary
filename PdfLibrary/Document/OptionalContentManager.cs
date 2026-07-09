using Logging;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Document;

/// <summary>
/// Manages Optional Content (layers/OCGs) visibility for PDF rendering
/// PDF spec ISO 32000-1:2008 section 8.11
/// </summary>
internal class OptionalContentManager
{
    private readonly HashSet<string> _disabledOCGs = [];
    private readonly PdfDocument? _document;

    /// <summary>
    /// Creates an OptionalContentManager from a PDF document
    /// Reads the /OCProperties dictionary from the document catalog
    /// </summary>
    public OptionalContentManager(PdfDocument? document = null)
    {
        _document = document;

        if (document is null)
            return;

        // Get the document catalog
        PdfCatalog? catalog = document.GetCatalog();
        if (catalog is null)
        {
            PdfLogger.Log(LogCategory.Graphics, "OptionalContentManager: No catalog found");
            return;
        }

        // Look for /OCProperties in the catalog
        if (!catalog.Dictionary.TryGetValue(new PdfName("OCProperties"), out PdfObject ocPropsObj))
        {
            PdfLogger.Log(LogCategory.Graphics, "OptionalContentManager: No /OCProperties in catalog");
            return;
        }

        if (Resolve(ocPropsObj) is not PdfDictionary ocProperties)
        {
            PdfLogger.Log(LogCategory.Graphics, "OptionalContentManager: /OCProperties is not a dictionary");
            return;
        }

        PdfLogger.Log(LogCategory.Graphics, "OptionalContentManager: Found /OCProperties");

        // Get the default configuration dictionary /OCProperties/D
        // Per PDF spec ISO 32000-1:2008 section 8.11.4.3
        if (!ocProperties.TryGetValue(new PdfName("D"), out PdfObject dObj) || Resolve(dObj) is not PdfDictionary defaultConfig)
        {
            PdfLogger.Log(LogCategory.Graphics, "OptionalContentManager: No /D (default configuration) found");
            return;
        }

        PdfLogger.Log(LogCategory.Graphics, "OptionalContentManager: Found /D (default configuration)");

        // Get /BaseState - determines default visibility (can be /ON or /OFF)
        // If /BaseState is /ON (or missing), all OCGs are visible by default except those in /OFF
        // If /BaseState is /OFF, all OCGs are hidden by default except those in /ON
        var defaultVisible = true; // Default is /ON per spec
        if (defaultConfig.TryGetValue(new PdfName("BaseState"), out PdfObject baseStateObj) && baseStateObj is PdfName baseState)
        {
            defaultVisible = baseState.Value != "OFF";
            PdfLogger.Log(LogCategory.Graphics, $"OptionalContentManager: /BaseState = {baseState.Value}, defaultVisible = {defaultVisible}");
        }
        else
        {
            PdfLogger.Log(LogCategory.Graphics, "OptionalContentManager: No /BaseState found, using default (/ON)");
        }

        // Process based on BaseState
        if (defaultVisible)
        {
            // BaseState is /ON - read /OFF array for OCGs that should be hidden
            if (defaultConfig.TryGetValue(new PdfName("OFF"), out PdfObject offObj) && offObj is PdfArray offArray)
            {
                PdfLogger.Log(LogCategory.Graphics, $"OptionalContentManager: Found /OFF array with {offArray.Count} items");
                foreach (PdfObject item in offArray)
                {
                    // Items are indirect references to OCG dictionaries
                    if (item is not PdfIndirectReference reference) continue;
                    var ocgKey = $"{reference.ObjectNumber} {reference.GenerationNumber} R";
                    _disabledOCGs.Add(ocgKey);
                    PdfLogger.Log(LogCategory.Graphics, $"  Disabled OCG: {ocgKey}");
                }
            }
            else
            {
                PdfLogger.Log(LogCategory.Graphics, "OptionalContentManager: No /OFF array found in /D");
            }
        }
        else
        {
            // BaseState is /OFF - all OCGs are hidden by default
            // We need to get ALL OCGs and disable them, except those in /ON array
            PdfLogger.Log(LogCategory.Graphics, "OptionalContentManager: /BaseState is /OFF - all OCGs hidden by default");

            // Get the /OCGs array which contains all OCGs in the document
            if (ocProperties.TryGetValue(new PdfName("OCGs"), out PdfObject ocgsObj) && ocgsObj is PdfArray ocgsArray)
            {
                HashSet<string> enabledOCGs = [];

                // First, collect all enabled OCGs from /ON array
                if (defaultConfig.TryGetValue(new PdfName("ON"), out PdfObject onObj) && onObj is PdfArray onArray)
                {
                    PdfLogger.Log(LogCategory.Graphics, $"OptionalContentManager: Found /ON array with {onArray.Count} items");
                    foreach (PdfObject item in onArray)
                    {
                        if (item is not PdfIndirectReference reference) continue;
                        var ocgKey = $"{reference.ObjectNumber} {reference.GenerationNumber} R";
                        enabledOCGs.Add(ocgKey);
                        PdfLogger.Log(LogCategory.Graphics, $"  Enabled OCG: {ocgKey}");
                    }
                }

                // Now disable all OCGs except those in /ON array
                foreach (PdfObject ocg in ocgsArray)
                {
                    if (ocg is not PdfIndirectReference reference) continue;
                    var ocgKey = $"{reference.ObjectNumber} {reference.GenerationNumber} R";
                    if (!enabledOCGs.Contains(ocgKey))
                    {
                        _disabledOCGs.Add(ocgKey);
                    }
                }
            }
        }

        PdfLogger.Log(LogCategory.Graphics, $"OptionalContentManager: Total disabled OCGs: {_disabledOCGs.Count}");
    }

    /// <summary>Resolves an indirect reference to its target object (no-op for direct objects).</summary>
    private PdfObject? Resolve(PdfObject? obj) =>
        obj is PdfIndirectReference r && _document is not null ? _document.ResolveReference(r) : obj;

    /// <summary>
    /// Checks if an XObject's Optional Content is visible
    /// Returns true if the content should be rendered, false if it should be hidden
    /// </summary>
    public bool IsVisible(PdfStream xobject)
    {
        // Check if XObject has an /OC (Optional Content) entry
        if (!xobject.Dictionary.TryGetValue(new PdfName("OC"), out var ocObj))
        {
            // No OC entry means always visible
            PdfLogger.Log(LogCategory.Graphics, "  IsVisible: No /OC entry - visible");
            return true;
        }

        PdfLogger.Log(LogCategory.Graphics, $"  IsVisible: Found /OC entry, type = {ocObj?.GetType().Name}");
        PdfLogger.Log(LogCategory.Graphics, $"  Disabled OCGs count: {_disabledOCGs.Count}");

        // /OC can be either:
        // 1. An indirect reference to an OCG dictionary
        // 2. An OCMD (Optional Content Membership Dictionary)

        switch (ocObj)
        {
            case PdfIndirectReference reference:
            {
                // Check if this OCG is in the disabled set
                var ocgKey = $"{reference.ObjectNumber} {reference.GenerationNumber} R";
                bool isDisabled = _disabledOCGs.Contains(ocgKey);
                PdfLogger.Log(LogCategory.Graphics, $"  IsVisible: OCG reference {ocgKey}, disabled = {isDisabled}");
                return !isDisabled;
            }
            // For OCMD dictionaries, we need to resolve the /OCGs entry
            case PdfDictionary ocmd:
            {
                if (ocmd.TryGetValue(new PdfName("OCGs"), out PdfObject ocgsObj))
                {
                    switch (ocgsObj)
                    {
                        // /OCGs can be a single reference or an array of references
                        case PdfIndirectReference ocgRef:
                        {
                            var ocgKey = $"{ocgRef.ObjectNumber} {ocgRef.GenerationNumber} R";
                            return !_disabledOCGs.Contains(ocgKey);
                        }
                        case PdfArray { Count: > 0 } ocgsArray:
                        {
                            // For simplicity, if ANY referenced OCG is disabled, hide the content
                            // A full implementation would check the /P (policy) entry
                            foreach (PdfObject ocgItem in ocgsArray)
                            {
                                if (ocgItem is PdfIndirectReference ocgArrayRef)
                                {
                                    var ocgKey = $"{ocgArrayRef.ObjectNumber} {ocgArrayRef.GenerationNumber} R";
                                    if (_disabledOCGs.Contains(ocgKey))
                                        return false;
                                }
                            }

                            break;
                        }
                    }
                }

                break;
            }
        }

        // If we can't determine, default to visible
        return true;
    }

    /// <summary>
    /// Visibility of a marked-content <c>/OC</c> property — the value looked up in the resource
    /// <c>/Properties</c> dictionary for a <c>/OC /MCx BDC … EMC</c> sequence (or an inline dictionary).
    /// The property is either an OCG (visible unless its reference is in the default <c>/OFF</c> set) or an
    /// OCMD, whose visibility derives from its <c>/OCGs</c> members and <c>/P</c> policy (ISO 32000-1
    /// §8.11.2.2). Returns true if the marked content should be drawn.
    /// </summary>
    public bool IsMarkedContentVisible(PdfObject? ocProperty)
    {
        if (ocProperty is null) return true;

        // An OCG is keyed by its own indirect reference; an OCMD must be resolved and evaluated by policy.
        string? ocgKey = null;
        PdfObject? resolved = ocProperty;
        if (ocProperty is PdfIndirectReference reference)
        {
            ocgKey = $"{reference.ObjectNumber} {reference.GenerationNumber} R";
            resolved = _document?.ResolveReference(reference);
        }

        if (resolved is PdfDictionary dict
            && dict.TryGetValue(new PdfName("Type"), out PdfObject? typeObj)
            && typeObj is PdfName { Value: "OCMD" })
        {
            return IsOcmdVisible(dict);
        }

        // Plain OCG: visible unless its reference is in the disabled set. An inline OCG dictionary with no
        // reference cannot appear in /OFF, so it is visible.
        return ocgKey is null || !_disabledOCGs.Contains(ocgKey);
    }

    /// <summary>
    /// Evaluates an OCMD's visibility from its member OCGs and <c>/P</c> visibility policy
    /// (ISO 32000-1 §8.11.2.2, Table 99). Default policy is <c>AnyOn</c>.
    /// </summary>
    private bool IsOcmdVisible(PdfDictionary ocmd)
    {
        // A /VE visibility expression takes precedence over /OCGs + /P (ISO 32000-1 §8.11.2.2).
        if (ocmd.TryGetValue(new PdfName("VE"), out PdfObject? veObj) && Resolve(veObj) is PdfArray ve)
            return EvaluateVisibilityExpression(ve);

        var members = new List<string>();
        if (ocmd.TryGetValue(new PdfName("OCGs"), out PdfObject? ocgsObj))
        {
            switch (ocgsObj)
            {
                case PdfIndirectReference single:
                    members.Add($"{single.ObjectNumber} {single.GenerationNumber} R");
                    break;
                case PdfArray array:
                    foreach (PdfObject item in array)
                        if (item is PdfIndirectReference r) members.Add($"{r.ObjectNumber} {r.GenerationNumber} R");
                    break;
            }
        }

        // An OCMD with no member OCGs imposes no visibility constraint → visible (§8.11.2.2).
        if (members.Count == 0) return true;

        bool IsOn(string key) => !_disabledOCGs.Contains(key);
        bool AnyOn() { foreach (string m in members) if (IsOn(m)) return true; return false; }
        bool AllOn() { foreach (string m in members) if (!IsOn(m)) return false; return true; }
        bool AnyOff() { foreach (string m in members) if (!IsOn(m)) return true; return false; }
        bool AllOff() { foreach (string m in members) if (IsOn(m)) return false; return true; }

        string policy = ocmd.TryGetValue(new PdfName("P"), out PdfObject? pObj) && pObj is PdfName p ? p.Value : "AnyOn";
        return policy switch
        {
            "AllOn" => AllOn(),
            "AnyOff" => AnyOff(),
            "AllOff" => AllOff(),
            _ => AnyOn(),   // "AnyOn" is the default
        };
    }

    /// <summary>
    /// Evaluates an OCMD visibility expression (<c>/VE</c>, ISO 32000-1 §8.11.2.2): a nested array whose
    /// first element is <c>/And</c>, <c>/Or</c>, or <c>/Not</c> and whose other elements are OCG references
    /// or sub-expressions. A referenced OCG is "true" when it is ON (not in the default /OFF set).
    /// </summary>
    private bool EvaluateVisibilityExpression(PdfObject? node)
    {
        // A leaf is a reference to an OCG (kept unresolved so it can be matched by reference key).
        if (node is PdfIndirectReference r)
            return !_disabledOCGs.Contains($"{r.ObjectNumber} {r.GenerationNumber} R");

        if (node is not PdfArray arr || arr.Count < 1 || arr[0] is not PdfName op)
            return true;   // malformed expression → conservatively visible

        switch (op.Value)
        {
            case "Not":
                return arr.Count >= 2 && !EvaluateVisibilityExpression(arr[1]);
            case "And":
                for (var i = 1; i < arr.Count; i++)
                    if (!EvaluateVisibilityExpression(arr[i])) return false;
                return true;
            case "Or":
                for (var i = 1; i < arr.Count; i++)
                    if (EvaluateVisibilityExpression(arr[i])) return true;
                return false;
            default:
                return true;
        }
    }

    /// <summary>
    /// Gets the number of disabled OCGs
    /// </summary>
    public int DisabledCount => _disabledOCGs.Count;

    /// <summary>
    /// Checks if any OCGs are disabled
    /// </summary>
    public bool HasDisabledContent => _disabledOCGs.Count > 0;
}
