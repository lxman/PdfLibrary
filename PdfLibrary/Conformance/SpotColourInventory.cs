using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance;

/// <summary>A Separation colour space definition: <c>[/Separation name alternate tintTransform]</c>.</summary>
internal readonly record struct SeparationDef(string Colorant, PdfObject? Alternate, PdfObject? TintTransform);

/// <summary>A DeviceN colour space definition: <c>[/DeviceN [names] alternate tintTransform attributes?]</c>.
/// <paramref name="Attributes"/> is the optional 4th element (carries /Subtype /NChannel and /Colorants).</summary>
internal readonly record struct DeviceNDef(
    IReadOnlyList<string> Colorants, PdfObject? Alternate, PdfObject? TintTransform, PdfDictionary? Attributes);

/// <summary>
/// Inventories every Separation and DeviceN colour-space definition in a document by object-scanning all
/// indirect objects and recursing through their directly-nested arrays/dictionaries (indirect children are
/// themselves top-level objects, so they are reached without following references — which also makes the
/// walk cycle-free). Colorant names, alternate spaces and tint transforms are captured for the PDF/X-4
/// spot-colour rules; the alternate/tint objects are returned unresolved for the caller to classify.
/// </summary>
internal static class SpotColourInventory
{
    /// <summary>The standard process colorants, which need no /Colorants entry in an NChannel space.</summary>
    public static readonly IReadOnlySet<string> ProcessColorants =
        new HashSet<string>(StringComparer.Ordinal) { "Cyan", "Magenta", "Yellow", "Black" };

    public static void Collect(ConformanceContext context,
        out List<SeparationDef> separations, out List<DeviceNDef> deviceNs)
    {
        separations = [];
        deviceNs = [];
        context.Document.MaterializeAllObjects();
        foreach (PdfObject obj in context.Document.Objects.Values)
            Walk(context, obj, separations, deviceNs, depth: 0);
    }

    private static void Walk(ConformanceContext context, PdfObject? node,
        List<SeparationDef> separations, List<DeviceNDef> deviceNs, int depth)
    {
        if (depth > 32)
            return;

        switch (node)
        {
            case PdfArray array when array.Count > 0:
                switch ((context.Resolve(array[0]) as PdfName)?.Value)
                {
                    case "Separation" when array.Count >= 4:
                        separations.Add(new SeparationDef(
                            (context.Resolve(array[1]) as PdfName)?.Value ?? string.Empty, array[2], array[3]));
                        break;
                    case "DeviceN" when array.Count >= 4:
                        deviceNs.Add(new DeviceNDef(
                            ReadNames(context, array[1]), array[2], array[3],
                            array.Count >= 5 ? context.Resolve(array[4]) as PdfDictionary : null));
                        break;
                }
                foreach (PdfObject element in array)
                    if (element is not PdfIndirectReference) // indirect elements are walked at top level
                        Walk(context, element, separations, deviceNs, depth + 1);
                break;

            case PdfDictionary dict:
                foreach (PdfObject value in dict.Values)
                    if (value is not PdfIndirectReference)
                        Walk(context, value, separations, deviceNs, depth + 1);
                break;

            case PdfStream stream:
                foreach (PdfObject value in stream.Dictionary.Values)
                    if (value is not PdfIndirectReference)
                        Walk(context, value, separations, deviceNs, depth + 1);
                break;
        }
    }

    private static IReadOnlyList<string> ReadNames(ConformanceContext context, PdfObject? namesObj)
    {
        var names = new List<string>();
        if (context.Resolve(namesObj) is PdfArray array)
            foreach (PdfObject entry in array)
                if (context.Resolve(entry) is PdfName name)
                    names.Add(name.Value);
        return names;
    }

    /// <summary>
    /// A content-derived signature of a Separation/DeviceN tint transform: the function's defining keys
    /// (canonicalised so that numerically-equal values such as <c>0</c> and <c>0.0</c> match) plus a hash of
    /// any sampled/PostScript stream data. Two transforms with identical content produce identical
    /// signatures, so duplicated-but-equal function objects never read as inconsistent; distinct transforms
    /// differ. Missing/unreadable ⇒ a stable placeholder.
    /// </summary>
    public static string TintTransformSignature(ConformanceContext context, PdfObject? tintTransform)
    {
        PdfObject? resolved = context.Resolve(tintTransform);
        PdfDictionary? dict = resolved as PdfDictionary ?? (resolved as PdfStream)?.Dictionary;
        if (dict is null)
            return resolved is null ? "none" : "opaque";

        var sb = new StringBuilder();
        foreach (string key in FunctionKeys)
        {
            sb.Append(Canonical(context, dict.Get(key), depth: 0));
            sb.Append('|');
        }
        if (resolved is PdfStream stream)
        {
            try { sb.Append(Fnv1a(stream.GetDecodedData(context.Document.Decryptor))); }
            catch { sb.Append("data?"); }
        }
        return sb.ToString();
    }

    // The keys that define a Type 0/2/3/4 function's mapping (excluding /Length and filter keys, which are
    // representation noise). /Functions canonicalises its sub-functions, distinguishing distinct stitches.
    private static readonly string[] FunctionKeys =
        ["FunctionType", "Domain", "Range", "C0", "C1", "N", "Size", "BitsPerSample", "Encode", "Decode", "Functions"];

    /// <summary>
    /// Canonical text of a value for content comparison: numbers are normalised to a single form (so integer
    /// <c>1</c> and real <c>1.0</c> compare equal — the difference is only lexical), arrays/dictionaries recurse
    /// (dictionary keys sorted), and a nested function stream folds in a hash of its decoded data. Bounded by depth.
    /// </summary>
    private static string Canonical(ConformanceContext context, PdfObject? value, int depth)
    {
        if (depth > 16)
            return "…";
        switch (context.Resolve(value))
        {
            case null:
                return string.Empty;
            case PdfInteger i:
                return ((double)i.LongValue).ToString("R", CultureInfo.InvariantCulture);
            case PdfReal r:
                return r.Value.ToString("R", CultureInfo.InvariantCulture);
            case PdfBoolean b:
                return b.Value ? "true" : "false";
            case PdfName n:
                return "/" + n.Value;
            case PdfString s:
                return "(" + s + ")";
            case PdfArray a:
                return "[" + string.Join(" ", a.Select(e => Canonical(context, e, depth + 1))) + "]";
            case PdfStream st:
                string data;
                try { data = Fnv1a(st.GetDecodedData(context.Document.Decryptor)); }
                catch { data = "data?"; }
                return "{" + CanonicalDict(context, st.Dictionary, depth + 1) + ":" + data + "}";
            case PdfDictionary d:
                return "<" + CanonicalDict(context, d, depth + 1) + ">";
            default:
                return "?";
        }
    }

    private static string CanonicalDict(ConformanceContext context, PdfDictionary dict, int depth) =>
        string.Join(" ", dict.Keys
            .Where(k => k.Value is not ("Length" or "Filter" or "DecodeParms" or "DL"))
            .OrderBy(k => k.Value, StringComparer.Ordinal)
            .Select(k => k.Value + "=" + Canonical(context, dict.Get(k), depth)));

    private static string Fnv1a(byte[] data)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte b in data)
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        return hash.ToString("x");
    }
}
