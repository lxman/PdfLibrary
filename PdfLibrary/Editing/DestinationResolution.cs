using PdfLibrary.Builder.Bookmark;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing;

/// <summary>
/// Shared resolution of navigation targets for dictionaries that carry a destination or action —
/// outline (bookmark) items and Link annotations. Resolves an explicit <c>/Dest</c>, a GoTo
/// action's <c>/D</c> (<c>/A</c>), and named destinations (catalog <c>/Dests</c> plus the
/// <c>/Names /Dests</c> tree); also extracts URI actions. ISO 32000-2 §12.3.2–12.3.3, §12.6.
/// </summary>
internal static class DestinationResolution
{
    /// <summary>The destination (page + position) a holder navigates to, or null if none/unresolvable.</summary>
    public static PdfDestination? Destination(PdfDocument doc, PdfDictionary holder)
        => RawDestination(doc, holder) is { } raw ? DestinationCodec.Decode(doc, raw) : null;

    /// <summary>The URI of a holder's URI action (<c>/A &lt;&lt; /S /URI /URI (...) &gt;&gt;</c>), or null.</summary>
    public static string? Uri(PdfDocument doc, PdfDictionary holder)
    {
        if (Resolve(doc, holder.Get(new PdfName("A"))) is PdfDictionary action
            && Resolve(doc, action.Get(new PdfName("S"))) is PdfName { Value: "URI" }
            && Resolve(doc, action.Get(new PdfName("URI"))) is PdfString uri)
            return uri.GetText();
        return null;
    }

    /// <summary>The raw destination object referenced by <c>/Dest</c>, or a GoTo action's <c>/D</c>.
    /// A named destination (string/name) is looked up and unwrapped to its underlying destination.
    /// Returns null if the holder specifies no destination.</summary>
    public static PdfObject? RawDestination(PdfDocument doc, PdfDictionary holder)
    {
        PdfObject? dest = holder.Get(new PdfName("Dest"));
        if (dest is null
            && Resolve(doc, holder.Get(new PdfName("A"))) is PdfDictionary action
            && Resolve(doc, action.Get(new PdfName("S"))) is PdfName { Value: "GoTo" })
        {
            dest = action.Get(new PdfName("D"));
        }
        return ResolveNamed(doc, dest);
    }

    private static PdfObject? ResolveNamed(PdfDocument doc, PdfObject? dest)
    {
        string? name = dest switch
        {
            PdfString s => s.Value,
            PdfName n   => n.Value,
            _           => null
        };
        if (name is null) return dest;   // already an explicit array (or null)

        PdfObject? resolved = DestinationRepairer.LookupNamedDest(doc, name);
        if (Resolve(doc, resolved) is PdfDictionary d && d.Get(new PdfName("D")) is { } inner)
            return inner;
        return resolved;
    }

    private static PdfObject? Resolve(PdfDocument doc, PdfObject? obj) =>
        obj is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : obj;
}
