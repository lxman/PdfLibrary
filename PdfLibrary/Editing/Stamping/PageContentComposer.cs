using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing.Stamping;

/// <summary>Wires a compiled stamp XObject into a page: resource registration + /Contents splicing.</summary>
internal static class PageContentComposer
{
    internal static string RegisterXObject(PdfDocument doc, PdfDictionary page, PdfIndirectReference xobjRef)
    {
        PdfDictionary xobjects = EnsureResourceSubDict(doc, page, "XObject");
        string name = UniqueName(xobjects, "Stamp");
        xobjects[new PdfName(name)] = xobjRef;
        return name;
    }

    internal static string RegisterOpacity(PdfDocument doc, PdfDictionary page, double alpha)
    {
        PdfDictionary gstates = EnsureResourceSubDict(doc, page, "ExtGState");
        var gs = new PdfDictionary();
        gs[PdfName.TypeName] = new PdfName("ExtGState");
        gs[new PdfName("ca")] = new PdfReal(alpha);
        gs[new PdfName("CA")] = new PdfReal(alpha);
        PdfIndirectReference gsRef = doc.RegisterObject(gs);
        string name = UniqueName(gstates, "GsStamp");
        gstates[new PdfName(name)] = gsRef;
        return name;
    }

    internal static PdfArray EnsureContentsArray(PdfDocument doc, PdfDictionary page)
    {
        PdfObject? c = page.Get(new PdfName("Contents"));
        switch (c)
        {
            case PdfIndirectReference r when doc.GetObject(r.ObjectNumber) is PdfArray existing:
                return existing;
            case PdfIndirectReference r:
            {
                var wrap = new PdfArray();
                wrap.Add(r);
                page[new PdfName("Contents")] = wrap;
                return wrap;
            }
            case PdfArray array:
                return array;
            case PdfStream stream:
            {
                var wrap = new PdfArray();
                wrap.Add(doc.RegisterObject(stream));
                page[new PdfName("Contents")] = wrap;
                return wrap;
            }
            default:
            {
                var empty = new PdfArray();
                page[new PdfName("Contents")] = empty;
                return empty;
            }
        }
    }

    /// <summary>Bracket the existing content streams in q…Q so prior unbalanced state can't leak into stamps.</summary>
    internal static void WrapExisting(PdfDocument doc, PdfArray contents)
    {
        PdfIndirectReference q = doc.RegisterObject(new PdfStream(new PdfDictionary(), "q\n"u8.ToArray()));
        PdfIndirectReference qq = doc.RegisterObject(new PdfStream(new PdfDictionary(), "\nQ\n"u8.ToArray()));
        contents.Insert(0, q);
        contents.Add(qq);
    }

    internal static void AddInvocation(PdfDocument doc, PdfArray contents, byte[] invocation, bool underlay)
    {
        PdfIndirectReference streamRef = doc.RegisterObject(new PdfStream(new PdfDictionary(), invocation));
        if (underlay) contents.Insert(0, streamRef);
        else contents.Add(streamRef);
    }

    private static PdfDictionary EnsureResourceSubDict(PdfDocument doc, PdfDictionary page, string key)
    {
        var resourcesName = new PdfName("Resources");
        PdfObject? resObj = page.Get(resourcesName);

        // Resolve indirect reference and promote it to a direct value on the page
        // so that callers can reliably read Resources directly from the page dictionary.
        if (resObj is PdfIndirectReference rr)
        {
            resObj = doc.GetObject(rr.ObjectNumber);
        }

        PdfDictionary resources;
        if (resObj is PdfDictionary rd)
        {
            resources = rd;
            // Ensure the page holds a direct reference to this dict (so tests can read it back directly)
            page[resourcesName] = resources;
        }
        else
        {
            resources = new PdfDictionary();
            page[resourcesName] = resources;
        }

        PdfObject? subObj = resources.Get(new PdfName(key));
        if (subObj is PdfIndirectReference sr) subObj = doc.GetObject(sr.ObjectNumber);
        if (subObj is PdfDictionary sd) return sd;

        var sub = new PdfDictionary();
        resources[new PdfName(key)] = sub;
        return sub;
    }

    private static string UniqueName(PdfDictionary dict, string prefix)
    {
        var n = 0;
        while (dict.ContainsKey(new PdfName($"{prefix}{n}"))) n++;
        return $"{prefix}{n}";
    }
}
