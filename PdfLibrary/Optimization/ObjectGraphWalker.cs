using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Optimization;

/// <summary>
/// Computes the set of object numbers reachable from the trailer's Root and Info by
/// following every indirect reference through dictionaries, arrays and stream dictionaries.
/// The document should be materialized first (GetObject loads on demand regardless).
/// </summary>
internal static class ObjectGraphWalker
{
    public static HashSet<int> CollectReachable(PdfDocument document)
    {
        var live = new HashSet<int>();
        var queue = new Queue<int>();

        Seed(document.Trailer.Root);
        Seed(document.Trailer.Info);

        while (queue.Count > 0)
        {
            PdfObject? obj = document.GetObject(queue.Dequeue());
            if (obj is not null) Visit(obj, live, queue);
        }
        return live;

        void Seed(PdfIndirectReference? r)
        {
            if (r is not null && live.Add(r.ObjectNumber)) queue.Enqueue(r.ObjectNumber);
        }
    }

    // Recurses through one object's *direct* structure; indirect refs are queued (bounded recursion).
    private static void Visit(PdfObject obj, HashSet<int> live, Queue<int> queue)
    {
        switch (obj)
        {
            case PdfIndirectReference r:
                if (live.Add(r.ObjectNumber)) queue.Enqueue(r.ObjectNumber);
                break;
            case PdfStream s:
                Visit(s.Dictionary, live, queue);
                break;
            case PdfDictionary d:
                foreach (PdfObject v in d.Values) Visit(v, live, queue);
                break;
            case PdfArray a:
                foreach (PdfObject v in a) Visit(v, live, queue);
                break;
        }
    }
}
