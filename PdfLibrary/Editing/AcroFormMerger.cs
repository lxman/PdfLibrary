using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing;

/// <summary>
/// After a page is cloned into a target document, registers the page's form-field widgets in the
/// target's /AcroForm /Fields (creating /AcroForm if absent), qualifies colliding field names, and
/// carries over /DR default resources. The field objects themselves are already cloned by the page cloner.
/// </summary>
internal static class AcroFormMerger
{
    public static void MergeImportedFields(PdfDocument target, PdfDocument source, PdfDictionary clonedPage)
    {
        if (Resolve(target, clonedPage.Get(new PdfName("Annots"))) is not PdfArray annots) return;

        var topFieldRefs = new List<PdfIndirectReference>();
        foreach (PdfObject a in annots)
        {
            if (Resolve(target, a) is not PdfDictionary widget || !IsWidget(widget)) continue;
            PdfIndirectReference? top = TopFieldRef(target, a, widget);
            if (top is not null && topFieldRefs.All(f => f.ObjectNumber != top.ObjectNumber))
                topFieldRefs.Add(top);
        }
        if (topFieldRefs.Count == 0) return;

        PdfArray fields = EnsureAcroFormFields(target);
        HashSet<string> existingNames = ExistingTopFieldNames(target, fields);
        foreach (PdfIndirectReference fieldRef in topFieldRefs)
        {
            if (target.GetObject(fieldRef.ObjectNumber) is PdfDictionary field)
                QualifyName(field, existingNames);
            fields.Add(fieldRef);
        }
        CarryDefaultResources(target, source);
    }

    private static bool IsWidget(PdfDictionary annot) =>
        annot.TryGetValue(PdfName.Subtype, out PdfObject s) && s is PdfName { Value: "Widget" };

    private static PdfIndirectReference? TopFieldRef(PdfDocument doc, PdfObject widgetRef, PdfDictionary widget)
    {
        PdfObject currentRef = widgetRef;
        PdfDictionary current = widget;
        var guard = 0;
        while (current.TryGetValue(new PdfName("Parent"), out PdfObject parent) && guard++ < 64)
        {
            currentRef = parent;
            if (Resolve(doc, parent) is not PdfDictionary parentDict) break;
            current = parentDict;
        }
        return currentRef as PdfIndirectReference;
    }

    private static PdfArray EnsureAcroFormFields(PdfDocument doc)
    {
        PdfDictionary catalog = doc.CatalogDictionary
            ?? throw new InvalidOperationException("Document has no catalog.");
        PdfDictionary acro;
        if (Resolve(doc, catalog.Get(new PdfName("AcroForm"))) is PdfDictionary existing)
            acro = existing;
        else
        {
            acro = new PdfDictionary
            {
                [new PdfName("Fields")] = new PdfArray()
            };
            catalog[new PdfName("AcroForm")] = doc.RegisterObject(acro);
        }
        if (Resolve(doc, acro.Get(new PdfName("Fields"))) is PdfArray fields) return fields;
        var created = new PdfArray();
        acro[new PdfName("Fields")] = created;
        return created;
    }

    private static HashSet<string> ExistingTopFieldNames(PdfDocument doc, PdfArray fields)
    {
        var names = new HashSet<string>();
        foreach (PdfObject f in fields)
            if (Resolve(doc, f) is PdfDictionary field && field.Get(new PdfName("T")) is PdfString t)
                names.Add(t.Value);
        return names;
    }

    private static void QualifyName(PdfDictionary field, HashSet<string> existingNames)
    {
        if (field.Get(new PdfName("T")) is not PdfString name) return;
        string baseName = name.Value;
        if (!existingNames.Contains(baseName))
        {
            existingNames.Add(baseName);
            return;
        }
        var n = 2;
        while (existingNames.Contains($"{baseName}#{n}")) n++;
        var qualified = $"{baseName}#{n}";
        existingNames.Add(qualified);
        field[new PdfName("T")] = new PdfString(qualified);
    }

    private static void CarryDefaultResources(PdfDocument target, PdfDocument source)
    {
        if (Resolve(target, target.CatalogDictionary?.Get(new PdfName("AcroForm"))) is not PdfDictionary targetAcro)
            return;
        if (targetAcro.ContainsKey(new PdfName("DR"))) return;
        if (Resolve(source, source.CatalogDictionary?.Get(new PdfName("AcroForm"))) is not PdfDictionary srcAcro)
            return;
        if (!srcAcro.TryGetValue(new PdfName("DR"), out PdfObject dr)) return;
        targetAcro[new PdfName("DR")] = ObjectGraphCloner.CloneValue(target, source, dr);
    }

    private static PdfObject? Resolve(PdfDocument doc, PdfObject? obj) =>
        obj is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : obj;
}
