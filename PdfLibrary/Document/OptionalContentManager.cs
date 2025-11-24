using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using Logging;
using PdfLibrary.Structure;

namespace PdfLibrary.Document;

/// <summary>
/// Manages Optional Content (layers/OCGs) visibility for PDF rendering
/// PDF spec ISO 32000-1:2008 section 8.11
/// </summary>
public class OptionalContentManager
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

        if (ocPropsObj is not PdfDictionary ocProperties)
        {
            PdfLogger.Log(LogCategory.Graphics, "OptionalContentManager: /OCProperties is not a dictionary");
            return;
        }

        PdfLogger.Log(LogCategory.Graphics, "OptionalContentManager: Found /OCProperties");

        // Get the default configuration dictionary /OCProperties/D
        // Per PDF spec ISO 32000-1:2008 section 8.11.4.3
        if (!ocProperties.TryGetValue(new PdfName("D"), out PdfObject dObj) || dObj is not PdfDictionary defaultConfig)
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

    /// <summary>
    /// Checks if an XObject's Optional Content is visible
    /// Returns true if the content should be rendered, false if it should be hidden
    /// </summary>
    public bool IsVisible(PdfStream xobject)
    {
        // Check if XObject has an /OC (Optional Content) entry
        if (!xobject.Dictionary.TryGetValue(new PdfName("OC"), out PdfObject? ocObj))
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
    /// Gets the number of disabled OCGs
    /// </summary>
    public int DisabledCount => _disabledOCGs.Count;

    /// <summary>
    /// Checks if any OCGs are disabled
    /// </summary>
    public bool HasDisabledContent => _disabledOCGs.Count > 0;
}
