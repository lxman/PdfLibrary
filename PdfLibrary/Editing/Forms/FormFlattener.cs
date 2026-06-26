using System.Globalization;
using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Editing.Stamping;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing.Forms;

/// <summary>
/// Bakes field appearance streams into page content and removes form interactivity.
/// ISO 32000: flattening = paint the widget's normal appearance onto the page, then remove
/// the annotation and the field from the AcroForm tree.
/// </summary>
internal static class FormFlattener
{
    /// <summary>
    /// Flattens a single field: paints each widget's /AP /N appearance onto its owning page,
    /// removes the widget from the page /Annots, and removes the field from /AcroForm /Fields.
    /// </summary>
    public static void FlattenField(PdfDocument doc, PdfFormField field)
    {
        // Build a map from object-number → page dict for all pages so we can find
        // which page owns each widget by scanning /Annots.
        List<PdfDictionary> pages = GetAllPageDicts(doc);

        foreach (PdfDictionary widget in field.Widgets)
        {
            // Find the owning page: the page whose /Annots array contains this widget.
            // We match by object identity (object number if indirect, or reference equality
            // for direct dicts that the tree walk has already resolved).
            PdfDictionary? owningPage = FindOwningPage(doc, pages, widget);
            if (owningPage is null) continue;

            // Resolve /AP /N to a Form-XObject stream.
            PdfLibrary.Core.PdfObject? apRaw = widget.Get(new PdfName("AP"));
            PdfLibrary.Core.PdfObject? apResolved = Resolve(doc, apRaw);
            if (apResolved is not PdfDictionary apDict) continue;

            PdfLibrary.Core.PdfObject? nRaw = apDict.Get(new PdfName("N"));
            if (nRaw is null) continue;

            // Resolve to ensure it is a Form-XObject stream.
            PdfLibrary.Core.PdfObject? nResolved = Resolve(doc, nRaw);
            if (nResolved is not PdfStream nStream) continue;
            // Verify /Subtype /Form
            PdfLibrary.Core.PdfObject? subtypeRaw = nStream.Dictionary.Get(new PdfName("Subtype"));
            if (subtypeRaw is not PdfName { Value: "Form" }) continue;

            // Ensure we have an indirect reference to the XObject (needed for RegisterXObject).
            PdfIndirectReference apRef = nRaw as PdfIndirectReference
                ?? doc.RegisterObject(nStream);

            // Register the XObject as a resource on the page.
            string xobjName = PageContentComposer.RegisterXObject(doc, owningPage, apRef);

            // Build the invocation using the widget /Rect to translate to the correct position.
            double rx0 = 0, ry0 = 0;
            PdfLibrary.Core.PdfObject? rectRaw = widget.Get(new PdfName("Rect"));
            PdfLibrary.Core.PdfObject? rectResolved = Resolve(doc, rectRaw);
            if (rectResolved is PdfArray rectArr && rectArr.Count >= 4)
            {
                double v0 = ToDouble(rectArr[0]);
                double v1 = ToDouble(rectArr[1]);
                double v2 = ToDouble(rectArr[2]);
                double v3 = ToDouble(rectArr[3]);
                rx0 = Math.Min(v0, v2);
                ry0 = Math.Min(v1, v3);
            }

            // q 1 0 0 1 rx0 ry0 cm /name Do Q
            string invocationStr = string.Format(
                CultureInfo.InvariantCulture,
                "q 1 0 0 1 {0:G} {1:G} cm /{2} Do Q\n",
                rx0, ry0, xobjName);
            byte[] invocationBytes = Encoding.Latin1.GetBytes(invocationStr);

            PdfArray contents = PageContentComposer.EnsureContentsArray(doc, owningPage);
            PageContentComposer.AddInvocation(doc, contents, invocationBytes, underlay: false);

            // Remove the widget from the page /Annots array.
            RemoveWidgetFromAnnots(doc, owningPage, widget);
        }

        // Remove the field dict from /AcroForm /Fields.
        RemoveFieldFromAcroForm(doc, field.Dict);
    }

    /// <summary>
    /// Flattens all terminal fields. After flattening, if /Fields is empty, removes /AcroForm
    /// from the catalog.
    /// </summary>
    public static void FlattenAll(PdfDocument doc)
    {
        // Snapshot the field list before mutating.
        List<PdfFormField> fields = FormFieldTree.Read(doc);

        foreach (PdfFormField field in fields)
            FlattenField(doc, field);

        // If /AcroForm /Fields is now empty, remove /AcroForm from the catalog.
        PruneAcroFormIfEmpty(doc);
    }

    // ─── Private helpers ────────────────────────────────────────────────────────

    private static List<PdfDictionary> GetAllPageDicts(PdfDocument doc)
    {
        var result = new List<PdfDictionary>();
        List<PdfPage> pages = doc.GetPages();
        foreach (PdfPage page in pages)
            result.Add(page.Dictionary);
        return result;
    }

    /// <summary>
    /// Scans each page's /Annots to find the one that contains <paramref name="widget"/>.
    /// Matching is by object number (if the widget is an indirect object) or by reference equality.
    /// </summary>
    private static PdfDictionary? FindOwningPage(
        PdfDocument doc,
        List<PdfDictionary> pages,
        PdfDictionary widget)
    {
        foreach (PdfDictionary page in pages)
        {
            PdfLibrary.Core.PdfObject? annotsRaw = page.Get(new PdfName("Annots"));
            PdfLibrary.Core.PdfObject? annotsResolved = Resolve(doc, annotsRaw);
            if (annotsResolved is not PdfArray annots) continue;

            foreach (PdfLibrary.Core.PdfObject entry in annots)
            {
                // The entry in /Annots can be an indirect reference or a direct dict.
                if (entry is PdfIndirectReference ir)
                {
                    // Match by object number.
                    if (widget.IsIndirect && widget.ObjectNumber == ir.ObjectNumber)
                        return page;
                    // Or if the resolved dict is the same instance.
                    PdfLibrary.Core.PdfObject? resolved = doc.GetObject(ir.ObjectNumber);
                    if (ReferenceEquals(resolved, widget))
                        return page;
                }
                else if (ReferenceEquals(entry, widget))
                {
                    return page;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Removes the widget from the /Annots array of the given page.
    /// Matches by object number (indirect) or reference equality (direct).
    /// </summary>
    private static void RemoveWidgetFromAnnots(
        PdfDocument doc,
        PdfDictionary page,
        PdfDictionary widget)
    {
        PdfLibrary.Core.PdfObject? annotsRaw = page.Get(new PdfName("Annots"));
        PdfLibrary.Core.PdfObject? annotsResolved = Resolve(doc, annotsRaw);
        if (annotsResolved is not PdfArray annots) return;

        // Collect indices to remove (iterate backwards to preserve indices).
        var toRemove = new List<int>();
        for (int i = 0; i < annots.Count; i++)
        {
            PdfLibrary.Core.PdfObject entry = annots[i];
            bool match = false;
            if (entry is PdfIndirectReference ir)
            {
                if (widget.IsIndirect && widget.ObjectNumber == ir.ObjectNumber)
                    match = true;
                else if (ReferenceEquals(doc.GetObject(ir.ObjectNumber), widget))
                    match = true;
            }
            else if (ReferenceEquals(entry, widget))
            {
                match = true;
            }
            if (match) toRemove.Add(i);
        }

        for (int i = toRemove.Count - 1; i >= 0; i--)
            annots.RemoveAt(toRemove[i]);
    }

    /// <summary>
    /// Removes the field dict from /AcroForm /Fields (top-level entries only, by identity/object number).
    /// </summary>
    private static void RemoveFieldFromAcroForm(PdfDocument doc, PdfDictionary fieldDict)
    {
        PdfDictionary? catalog = doc.CatalogDictionary;
        if (catalog is null) return;

        PdfLibrary.Core.PdfObject? acroRaw = catalog.Get(new PdfName("AcroForm"));
        PdfLibrary.Core.PdfObject? acroResolved = Resolve(doc, acroRaw);
        if (acroResolved is not PdfDictionary acro) return;

        PdfLibrary.Core.PdfObject? fieldsRaw = acro.Get(new PdfName("Fields"));
        PdfLibrary.Core.PdfObject? fieldsResolved = Resolve(doc, fieldsRaw);
        if (fieldsResolved is not PdfArray fields) return;

        var toRemove = new List<int>();
        for (int i = 0; i < fields.Count; i++)
        {
            PdfLibrary.Core.PdfObject entry = fields[i];
            bool match = false;
            if (entry is PdfIndirectReference ir)
            {
                if (fieldDict.IsIndirect && fieldDict.ObjectNumber == ir.ObjectNumber)
                    match = true;
                else if (ReferenceEquals(doc.GetObject(ir.ObjectNumber), fieldDict))
                    match = true;
            }
            else if (ReferenceEquals(entry, fieldDict))
            {
                match = true;
            }
            if (match) toRemove.Add(i);
        }

        for (int i = toRemove.Count - 1; i >= 0; i--)
            fields.RemoveAt(toRemove[i]);
    }

    /// <summary>
    /// If /AcroForm /Fields is empty (or absent), removes /AcroForm from the catalog
    /// and also removes /NeedAppearances.
    /// </summary>
    internal static void PruneAcroFormIfEmpty(PdfDocument doc)
    {
        PdfDictionary? catalog = doc.CatalogDictionary;
        if (catalog is null) return;

        PdfLibrary.Core.PdfObject? acroRaw = catalog.Get(new PdfName("AcroForm"));
        PdfLibrary.Core.PdfObject? acroResolved = Resolve(doc, acroRaw);
        if (acroResolved is not PdfDictionary acro) return;

        PdfLibrary.Core.PdfObject? fieldsRaw = acro.Get(new PdfName("Fields"));
        PdfLibrary.Core.PdfObject? fieldsResolved = Resolve(doc, fieldsRaw);
        bool fieldsEmpty = fieldsResolved is not PdfArray fields || fields.Count == 0;

        if (fieldsEmpty)
        {
            catalog.Remove(new PdfName("AcroForm"));
            acro.Remove(new PdfName("NeedAppearances"));
        }
    }

    private static PdfLibrary.Core.PdfObject? Resolve(PdfDocument doc, PdfLibrary.Core.PdfObject? obj) =>
        obj is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : obj;

    private static double ToDouble(PdfLibrary.Core.PdfObject obj) => obj switch
    {
        PdfReal r   => r.Value,
        PdfInteger i => i.Value,
        _            => 0.0
    };
}
