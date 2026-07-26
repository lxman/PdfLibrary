using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Document;

/// <summary>
/// Reads a page's distinct named Separation/DeviceN colorants (Soft-Proof SP-1/SP-6b) by walking the
/// page's reachable resource graph: /Resources/ColorSpace in every scope, image XObject colour spaces,
/// and — recursively — the resources of form XObjects and tiling patterns (plus shading colour spaces).
/// Indexed base spaces are unwrapped to their base colorants. All/None are recognised but not emitted;
/// process colorants (Cyan/Magenta/Yellow/Black) are emitted with Process kind. Discovery is usage-
/// agnostic (resource dictionaries only — content streams are never parsed) and cycle-guarded, mirroring
/// <see cref="Conformance.DeviceColourAnalysis"/>'s resource-graph walk. Page-level colorants are
/// collected first so plane indices stay stable for pages without nested spots.
/// Type3-font CharProcs and annotation appearance streams are not descended (a spot used only there
/// falls back to the flattened path, like the plane-cap deferral).
/// </summary>
internal static class PageColorantReader
{
    private const int MaxDepth = 24;

    public static IReadOnlyList<PageColorant> Read(PdfDocument document, int pageIndex)
    {
        var result = new List<PageColorant>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var graphSeen = new HashSet<int>();
        PdfPage? page = document.GetPage(pageIndex);
        try
        {
            WalkResources(page?.GetResources(), document, seen, result, graphSeen, 0);
        }
        catch
        {
            // Defensive: a malformed resource graph must never throw out of the public GetPageColorants
            // (spec "Guards and stability" contract). Return whatever was collected before the fault.
        }
        return result;
    }

    private static void WalkResources(PdfResources? resources, PdfDocument document, HashSet<string> seen,
        List<PageColorant> result, HashSet<int> graphSeen, int depth)
    {
        if (resources is null || depth > MaxDepth) return;
        if (resources.Dictionary.IsIndirect && !graphSeen.Add(resources.Dictionary.ObjectNumber)) return;

        // Named colour spaces in this scope (page-level first ⇒ stable plane order).
        if (resources.GetColorSpaces() is { } colorSpaces)
            foreach (PdfName key in colorSpaces.Keys)
                if (colorSpaces.TryGetValue(key, out PdfObject? defObj))
                    AddColorants(defObj, document, seen, result);

        // Image XObjects contribute their (array) colour space; form XObjects recurse into their resources.
        if (resources.GetXObjects() is { } xobjects)
            foreach (PdfObject xobj in xobjects.Values)
                WalkXObject(xobj, document, seen, result, graphSeen, depth);

        // Tiling patterns recurse into their own resources; shading patterns contribute the shading space.
        if (resources.GetPatterns() is { } patterns)
            foreach (PdfObject pat in patterns.Values)
                WalkPattern(pat, document, seen, result, graphSeen, depth);

        // Shadings declared directly in this scope's /Shading.
        if (resources.GetShadings() is { } shadings)
            foreach (PdfObject sh in shadings.Values)
                WalkShading(sh, document, seen, result);
    }

    private static void WalkXObject(PdfObject? xobjObj, PdfDocument document, HashSet<string> seen,
        List<PageColorant> result, HashSet<int> graphSeen, int depth)
    {
        if (Deref(xobjObj, document) is not PdfStream stream) return;
        string? subtype = (stream.Dictionary.Get("Subtype") as PdfName)?.Value;
        if (subtype == "Image")
        {
            // A resource-name colour space is already covered by the enclosing scope's /ColorSpace walk;
            // AddColorants no-ops on names/device spaces, so only inline/indirect ARRAY spaces (image-only
            // spots) are collected here.
            AddColorants(stream.Dictionary.Get("ColorSpace"), document, seen, result);
        }
        else if (subtype == "Form")
        {
            if (stream.IsIndirect && !graphSeen.Add(stream.ObjectNumber)) return; // form cycle guard
            PdfResources? formResources =
                Deref(stream.Dictionary.Get("Resources"), document) is PdfDictionary rd
                    ? new PdfResources(rd, document)
                    : null;
            WalkResources(formResources, document, seen, result, graphSeen, depth + 1);
        }
    }

    private static void WalkPattern(PdfObject? patObj, PdfDocument document, HashSet<string> seen,
        List<PageColorant> result, HashSet<int> graphSeen, int depth)
    {
        PdfObject? resolved = Deref(patObj, document);
        PdfDictionary? dict = resolved as PdfDictionary ?? (resolved as PdfStream)?.Dictionary;
        if (dict is null) return;
        int patternType = (dict.Get("PatternType") as PdfInteger)?.Value ?? 0;
        if (patternType == 2) { WalkShading(dict.Get("Shading"), document, seen, result); return; }
        if (resolved is not PdfStream tiling) return;                      // PatternType 1 is a stream
        if (tiling.IsIndirect && !graphSeen.Add(tiling.ObjectNumber)) return;
        PdfResources? patternResources =
            Deref(tiling.Dictionary.Get("Resources"), document) is PdfDictionary prd
                ? new PdfResources(prd, document)
                : null;
        WalkResources(patternResources, document, seen, result, graphSeen, depth + 1);
    }

    private static void WalkShading(PdfObject? shadingObj, PdfDocument document, HashSet<string> seen,
        List<PageColorant> result)
    {
        PdfObject? resolved = Deref(shadingObj, document);
        PdfDictionary? dict = resolved as PdfDictionary ?? (resolved as PdfStream)?.Dictionary;
        if (dict is null) return;
        AddColorants(dict.Get("ColorSpace"), document, seen, result);
    }

    private static void AddColorants(PdfObject? defObj, PdfDocument document, HashSet<string> seen,
        List<PageColorant> result, int depth = 0)
    {
        if (depth > 8) return;                                   // bounded Indexed/base unwrap
        PdfObject? resolved = Deref(defObj, document);
        if (resolved is not PdfArray arr || arr.Count == 0) return;

        // Indexed carries its colorants in the base space: [/Indexed base hival lookup] → recurse `base`.
        if (arr[0] is PdfName { Value: "Indexed" } && arr.Count >= 2)
        {
            AddColorants(arr[1], document, seen, result, depth + 1);
            return;
        }

        ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(resolved, null, document);
        if (origin is null) return;

        for (var i = 0; i < origin.Names.Count; i++)
        {
            string name = origin.Names[i];
            ColorantKind kind = KindFor(origin, i, name);
            if (kind is ColorantKind.All or ColorantKind.None) continue; // recognised, not a plate
            // `Kind` is now order-dependent for the first time: before this change it was a pure function
            // of `Name`, so first-wins dedup could never affect it. Now the same name can resolve to
            // Process under one space's role (e.g. an NChannel /Process /Components entry) and Spot under
            // a plain DeviceN elsewhere on the page — whichever the resource walk (WalkResources, above)
            // reaches first wins and the later definition is dropped by this dedup. Walk order is
            // deterministic and page-level-first by design, so this is accepted, not a bug — pathological
            // input, not one this reader needs to reconcile.
            if (!seen.Add(name)) continue;

            (double[][]? ramp, (byte R, byte G, byte B) solid) =
                ColorSpaceResolver.BuildTintRamp(arr, document, i, origin.Names.Count);
            result.Add(new PageColorant(name, kind, origin.AlternateSpace, ramp, solid));
        }
    }

    /// <summary>
    /// The colorant's kind, preferring the per-component role the resolver already computed over the
    /// name-only classification.
    ///
    /// <para><see cref="PageColorant.Classify"/> knows only the four reserved names, so a process
    /// colorant named anything else — <c>/PrCyan</c> in the veraPDF NChannel conformance fixture — reads
    /// as Spot and is handed a plane by the compositor's registry, while the per-operator path treats it
    /// as a process channel. The same colorant then exists twice.</para>
    ///
    /// <para><c>Components</c> is populated only for NChannel spaces, so every DeviceN and Separation
    /// space keeps the name-based answer unchanged. A Spot role also falls back to the name, because
    /// <c>ColourantRole</c> has no <c>All</c> member — <c>RoleFor</c> maps <c>/All</c> to Spot as a
    /// documented leniency — and that distinction must survive.</para>
    ///
    /// <para><c>origin.Components</c> is already-materialised data from the <c>OriginForColorSpaceObject</c>
    /// call above — reading it resolves nothing new, so no additional guard against a throw is needed
    /// here beyond the shape checks below (null list, and an index the list is too short to cover).</para>
    /// </summary>
    private static ColorantKind KindFor(ColorantOrigin origin, int index, string name)
    {
        if (origin.Components is not { } components || index >= components.Count)
            return PageColorant.Classify(name);

        return components[index].Role switch
        {
            ColourantRole.Process => ColorantKind.Process,
            // Documentary, not load-bearing: RoleFor returns None only for the literal name "None"
            // (ColorSpaceResolver.cs), and Classify("None") independently returns ColorantKind.None, so
            // this arm and the `_ => Classify(name)` fallback below produce the same result today —
            // deleting it changes nothing observable. Kept for symmetry with the Process arm above, so
            // the mapping reads as complete rather than silently relying on Classify to catch None too.
            ColourantRole.None => ColorantKind.None,
            _ => PageColorant.Classify(name),
        };
    }

    private static PdfObject? Deref(PdfObject? obj, PdfDocument document) =>
        obj is PdfIndirectReference r ? document.ResolveReference(r) ?? obj : obj;
}
