using System.Globalization;
using PdfLibrary.Builder.Bookmark;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing;

/// <summary>
/// Encodes/decodes PDF destination arrays for the editing graph.
/// Encode is the single source of truth for array shapes on the editing side;
/// all coordinates are formatted with InvariantCulture.
/// </summary>
internal static class DestinationCodec
{
    /// <summary>
    /// Encodes a <see cref="PdfDestination"/> into a PDF destination array
    /// (<c>[ pageRef /TypeName coord... ]</c>) using the supplied indirect page reference.
    /// Mirrors the shapes written by <c>PdfDocumentWriter.WriteBookmarkDestination</c>.
    /// </summary>
    public static PdfArray Encode(PdfDestination dest, PdfIndirectReference pageRef)
    {
        ArgumentNullException.ThrowIfNull(dest);
        ArgumentNullException.ThrowIfNull(pageRef);

        return dest.Type switch
        {
            PdfDestinationType.Fit  => new PdfArray(pageRef, new PdfName("Fit")),
            PdfDestinationType.FitB => new PdfArray(pageRef, new PdfName("FitB")),
            PdfDestinationType.FitH  => new PdfArray(pageRef, new PdfName("FitH"),  CoordObj(dest.Top)),
            PdfDestinationType.FitV  => new PdfArray(pageRef, new PdfName("FitV"),  CoordObj(dest.Left)),
            PdfDestinationType.FitBH => new PdfArray(pageRef, new PdfName("FitBH"), CoordObj(dest.Top)),
            PdfDestinationType.FitBV => new PdfArray(pageRef, new PdfName("FitBV"), CoordObj(dest.Left)),
            PdfDestinationType.FitR  => new PdfArray(
                pageRef,
                new PdfName("FitR"),
                CoordObj(dest.Left   ?? 0),
                CoordObj(dest.Bottom ?? 0),
                CoordObj(dest.Right  ?? 612),
                CoordObj(dest.Top    ?? 792)),
            // XYZ (default)
            _ => new PdfArray(
                pageRef,
                new PdfName("XYZ"),
                CoordObj(dest.Left),
                CoordObj(dest.Top),
                CoordObj(dest.Zoom))
        };
    }

    /// <summary>
    /// Decodes a PDF destination array object back into a <see cref="PdfDestination"/>.
    /// Resolves the page indirect reference to a 0-based page index via the current page order.
    /// Returns <c>null</c> if the array is malformed or the page reference is unresolvable.
    /// </summary>
    public static PdfDestination? Decode(PdfDocument doc, PdfObject destArray)
    {
        if (Resolve(doc, destArray) is not PdfArray arr || arr.Count < 2)
            return null;

        // First element must be a page indirect reference
        if (arr[0] is not PdfIndirectReference pageRef)
            return null;

        // Resolve page index from the current page order
        int pageIndex = ResolvePageIndex(doc, pageRef);
        if (pageIndex < 0) return null;

        // Second element is the destination type name
        if (arr[1] is not PdfName typeName) return null;

        return typeName.Value switch
        {
            "Fit"  => new PdfDestination { PageIndex = pageIndex, Type = PdfDestinationType.Fit },
            "FitB" => new PdfDestination { PageIndex = pageIndex, Type = PdfDestinationType.FitB },
            "FitH" => arr.Count >= 3
                ? new PdfDestination { PageIndex = pageIndex, Type = PdfDestinationType.FitH, Top = ReadCoord(arr[2]) }
                : null,
            "FitV" => arr.Count >= 3
                ? new PdfDestination { PageIndex = pageIndex, Type = PdfDestinationType.FitV, Left = ReadCoord(arr[2]) }
                : null,
            "FitBH" => arr.Count >= 3
                ? new PdfDestination { PageIndex = pageIndex, Type = PdfDestinationType.FitBH, Top = ReadCoord(arr[2]) }
                : null,
            "FitBV" => arr.Count >= 3
                ? new PdfDestination { PageIndex = pageIndex, Type = PdfDestinationType.FitBV, Left = ReadCoord(arr[2]) }
                : null,
            "FitR" => arr.Count >= 6
                ? new PdfDestination
                {
                    PageIndex = pageIndex,
                    Type  = PdfDestinationType.FitR,
                    Left   = ReadCoord(arr[2]),
                    Bottom = ReadCoord(arr[3]),
                    Right  = ReadCoord(arr[4]),
                    Top    = ReadCoord(arr[5])
                }
                : null,
            "XYZ" => arr.Count >= 5
                ? new PdfDestination
                {
                    PageIndex = pageIndex,
                    Type = PdfDestinationType.XYZ,
                    Left = ReadCoord(arr[2]),
                    Top  = ReadCoord(arr[3]),
                    Zoom = ReadCoord(arr[4])
                }
                : null,
            _ => null
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a <see cref="PdfReal"/> for a non-null coordinate, or <see cref="PdfNull.Instance"/> for null.
    /// Uses InvariantCulture implicitly via PdfReal.ToPdfString().
    /// </summary>
    private static PdfObject CoordObj(double? value) =>
        value.HasValue ? new PdfReal(value.Value) : (PdfObject)PdfNull.Instance;

    /// <summary>Non-nullable coord overload (for FitR where all coords are required).</summary>
    private static PdfObject CoordObj(double value) => new PdfReal(value);

    /// <summary>Reads an optional coordinate: PdfReal/PdfInteger → double, PdfNull → null.</summary>
    private static double? ReadCoord(PdfObject obj) =>
        obj switch
        {
            PdfReal r    => r.Value,
            PdfInteger i => (double)i.Value,
            _            => null   // PdfNull or anything unexpected
        };

    /// <summary>Resolves an indirect reference to its object, or returns the object as-is.</summary>
    private static PdfObject? Resolve(PdfDocument doc, PdfObject? obj) =>
        obj is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : obj;

    /// <summary>
    /// Finds the 0-based page index of <paramref name="pageRef"/> in the current page order.
    /// Returns -1 if not found.
    /// </summary>
    private static int ResolvePageIndex(PdfDocument doc, PdfIndirectReference pageRef)
    {
        PdfArray kids = PageTreeOps.Kids(doc);
        for (var i = 0; i < kids.Count; i++)
        {
            if (kids[i] is PdfIndirectReference kid && kid.ObjectNumber == pageRef.ObjectNumber)
                return i;
        }
        return -1;
    }
}
