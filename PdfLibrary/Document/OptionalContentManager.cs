using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Document;

/// <summary>
/// Manages Optional Content (layers/OCGs) visibility for PDF rendering
/// PDF spec ISO 32000-1:2008 section 8.11
/// </summary>
public class OptionalContentManager
{
    private readonly HashSet<string> _disabledOCGs = new();
    private readonly PdfDocument? _document;

    /// <summary>
    /// Creates an OptionalContentManager from a PDF document
    /// Reads the /OCProperties dictionary from the document catalog
    /// </summary>
    public OptionalContentManager(PdfDocument? document = null)
    {
        _document = document;

        if (document == null)
            return;

        // Get the document catalog
        var catalog = document.GetCatalog();
        if (catalog == null)
        {
            Console.WriteLine("OptionalContentManager: No catalog found");
            return;
        }

        // Look for /OCProperties in the catalog
        if (!catalog.Dictionary.TryGetValue(new PdfName("OCProperties"), out PdfObject? ocPropsObj))
        {
            Console.WriteLine("OptionalContentManager: No /OCProperties in catalog");
            return;
        }

        if (ocPropsObj is not PdfDictionary ocProperties)
        {
            Console.WriteLine("OptionalContentManager: /OCProperties is not a dictionary");
            return;
        }

        Console.WriteLine("OptionalContentManager: Found /OCProperties");

        // Get the default configuration dictionary /OCProperties/D
        // Per PDF spec ISO 32000-1:2008 section 8.11.4.3
        if (!ocProperties.TryGetValue(new PdfName("D"), out PdfObject? dObj) || dObj is not PdfDictionary defaultConfig)
        {
            Console.WriteLine("OptionalContentManager: No /D (default configuration) found");
            return;
        }

        Console.WriteLine("OptionalContentManager: Found /D (default configuration)");

        // Get /BaseState - determines default visibility (can be /ON or /OFF)
        // If /BaseState is /ON (or missing), all OCGs are visible by default except those in /OFF
        // If /BaseState is /OFF, all OCGs are hidden by default except those in /ON
        bool defaultVisible = true; // Default is /ON per spec
        if (defaultConfig.TryGetValue(new PdfName("BaseState"), out PdfObject? baseStateObj) && baseStateObj is PdfName baseState)
        {
            defaultVisible = baseState.Value != "OFF";
            Console.WriteLine($"OptionalContentManager: /BaseState = {baseState.Value}, defaultVisible = {defaultVisible}");
        }
        else
        {
            Console.WriteLine("OptionalContentManager: No /BaseState found, using default (/ON)");
        }

        // Process based on BaseState
        if (defaultVisible)
        {
            // BaseState is /ON - read /OFF array for OCGs that should be hidden
            if (defaultConfig.TryGetValue(new PdfName("OFF"), out PdfObject? offObj) && offObj is PdfArray offArray)
            {
                Console.WriteLine($"OptionalContentManager: Found /OFF array with {offArray.Count} items");
                foreach (var item in offArray)
                {
                    // Items are indirect references to OCG dictionaries
                    if (item is PdfIndirectReference reference)
                    {
                        string ocgKey = $"{reference.ObjectNumber} {reference.GenerationNumber} R";
                        _disabledOCGs.Add(ocgKey);
                        Console.WriteLine($"  Disabled OCG: {ocgKey}");
                    }
                }
            }
            else
            {
                Console.WriteLine("OptionalContentManager: No /OFF array found in /D");
            }
        }
        else
        {
            // BaseState is /OFF - all OCGs are hidden by default
            // We need to get ALL OCGs and disable them, except those in /ON array
            Console.WriteLine("OptionalContentManager: /BaseState is /OFF - all OCGs hidden by default");

            // Get the /OCGs array which contains all OCGs in the document
            if (ocProperties.TryGetValue(new PdfName("OCGs"), out PdfObject? ocgsObj) && ocgsObj is PdfArray ocgsArray)
            {
                HashSet<string> enabledOCGs = new();

                // First, collect all enabled OCGs from /ON array
                if (defaultConfig.TryGetValue(new PdfName("ON"), out PdfObject? onObj) && onObj is PdfArray onArray)
                {
                    Console.WriteLine($"OptionalContentManager: Found /ON array with {onArray.Count} items");
                    foreach (var item in onArray)
                    {
                        if (item is PdfIndirectReference reference)
                        {
                            string ocgKey = $"{reference.ObjectNumber} {reference.GenerationNumber} R";
                            enabledOCGs.Add(ocgKey);
                            Console.WriteLine($"  Enabled OCG: {ocgKey}");
                        }
                    }
                }

                // Now disable all OCGs except those in /ON array
                foreach (var ocg in ocgsArray)
                {
                    if (ocg is PdfIndirectReference reference)
                    {
                        string ocgKey = $"{reference.ObjectNumber} {reference.GenerationNumber} R";
                        if (!enabledOCGs.Contains(ocgKey))
                        {
                            _disabledOCGs.Add(ocgKey);
                        }
                    }
                }
            }
        }

        Console.WriteLine($"OptionalContentManager: Total disabled OCGs: {_disabledOCGs.Count}");
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
            Console.WriteLine("  IsVisible: No /OC entry - visible");
            return true;
        }

        Console.WriteLine($"  IsVisible: Found /OC entry, type = {ocObj?.GetType().Name}");
        Console.WriteLine($"  Disabled OCGs count: {_disabledOCGs.Count}");

        // /OC can be either:
        // 1. An indirect reference to an OCG dictionary
        // 2. An OCMD (Optional Content Membership Dictionary)

        if (ocObj is PdfIndirectReference reference)
        {
            // Check if this OCG is in the disabled set
            string ocgKey = $"{reference.ObjectNumber} {reference.GenerationNumber} R";
            bool isDisabled = _disabledOCGs.Contains(ocgKey);
            Console.WriteLine($"  IsVisible: OCG reference {ocgKey}, disabled = {isDisabled}");
            return !isDisabled;
        }

        // For OCMD dictionaries, we need to resolve the /OCGs entry
        if (ocObj is PdfDictionary ocmd)
        {
            if (ocmd.TryGetValue(new PdfName("OCGs"), out PdfObject? ocgsObj))
            {
                // /OCGs can be a single reference or an array of references
                if (ocgsObj is PdfIndirectReference ocgRef)
                {
                    string ocgKey = $"{ocgRef.ObjectNumber} {ocgRef.GenerationNumber} R";
                    return !_disabledOCGs.Contains(ocgKey);
                }
                else if (ocgsObj is PdfArray ocgsArray && ocgsArray.Count > 0)
                {
                    // For simplicity, if ANY referenced OCG is disabled, hide the content
                    // A full implementation would check the /P (policy) entry
                    foreach (var ocgItem in ocgsArray)
                    {
                        if (ocgItem is PdfIndirectReference ocgArrayRef)
                        {
                            string ocgKey = $"{ocgArrayRef.ObjectNumber} {ocgArrayRef.GenerationNumber} R";
                            if (_disabledOCGs.Contains(ocgKey))
                                return false;
                        }
                    }
                }
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
