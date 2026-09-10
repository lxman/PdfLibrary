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

        // RC2: a valued text/choice field may carry no /AP yet — XFA/LiveCycle forms ship widgets
        // with no appearance streams and rely on the viewer to draw the value. Generate one now so
        // there is a Form-XObject to bake below; otherwise the value would vanish when the widget
        // is removed (silent data loss).
        EnsureBakeableAppearance(doc, field);

        // Preflight the whole field before touching any page. A field may own several widgets; if a
        // later widget cannot be placed, baking an earlier sibling would leave a partially flattened
        // /Kids tree. The two plans below make the operation atomic at field scope.
        var bakePlans = new List<(
            PdfDictionary Widget,
            PdfDictionary Page,
            PdfLibrary.Core.PdfObject AppearanceEntry,
            PdfStream Appearance,
            double[] Placement)>();
        var removeOnlyPlans = new List<(PdfDictionary Widget, PdfDictionary Page)>();
        bool fieldHasValue = FieldHasValue(field);

        foreach (PdfDictionary widget in field.WidgetDicts)
        {
            PdfDictionary? owningPage = FindOwningPage(doc, pages, widget);
            if (owningPage is null) return;

            PdfLibrary.Core.PdfObject? apRaw = widget.Get(new PdfName("AP"));
            if (Resolve(doc, apRaw) is not PdfDictionary apDict)
            {
                if (fieldHasValue) return;
                removeOnlyPlans.Add((widget, owningPage));
                continue;
            }

            PdfLibrary.Core.PdfObject? nRaw = apDict.Get(new PdfName("N"));
            if (nRaw is null)
            {
                if (fieldHasValue) return;
                removeOnlyPlans.Add((widget, owningPage));
                continue;
            }

            PdfStream? nStream = null;
            PdfLibrary.Core.PdfObject? selectedEntry = null;
            switch (Resolve(doc, nRaw))
            {
                case PdfStream singleStream:
                    nStream = singleStream;
                    selectedEntry = nRaw;
                    break;
                case PdfDictionary stateDict:
                {
                    string asState = widget.Get(new PdfName("AS")) is PdfName asName ? asName.Value : "Off";
                    PdfLibrary.Core.PdfObject? stateRaw = stateDict.Get(new PdfName(asState));
                    if (stateRaw is not null && Resolve(doc, stateRaw) is PdfStream stateStream)
                    {
                        nStream = stateStream;
                        selectedEntry = stateRaw;
                    }
                    break;
                }
            }

            if (nStream is null || selectedEntry is null ||
                nStream.Dictionary.Get(new PdfName("Subtype")) is not PdfName { Value: "Form" })
            {
                if (fieldHasValue) return;
                removeOnlyPlans.Add((widget, owningPage));
                continue;
            }

            // ISO 32000-1 §12.5.5: transform the appearance /BBox by its /Matrix, fit the
            // transformed bounds to the annotation /Rect, then concatenate the two matrices.
            double[]? rect = ReadNumberArray(doc, widget.Get(new PdfName("Rect")), 4);
            double[]? bbox = ReadNumberArray(doc, nStream.Dictionary.Get(new PdfName("BBox")), 4);
            PdfLibrary.Core.PdfObject? matrixRaw = nStream.Dictionary.Get(new PdfName("Matrix"));
            double[]? matrix = matrixRaw is null
                ? [1, 0, 0, 1, 0, 0]
                : ReadNumberArray(doc, matrixRaw, 6);
            double[]? aa = rect is not null && bbox is not null && matrix is not null
                ? AppearancePlacement.ComputeAA(bbox, matrix, rect)
                : null;
            if (aa is null) return;

            bakePlans.Add((widget, owningPage, selectedEntry, nStream, aa));
        }

        foreach ((PdfDictionary widget, PdfDictionary page) in removeOnlyPlans)
            RemoveWidgetFromAnnots(doc, page, widget);

        foreach ((PdfDictionary widget, PdfDictionary page, PdfLibrary.Core.PdfObject appearanceEntry,
                     PdfStream appearance, double[] aa) in bakePlans)
        {
            PdfIndirectReference apRef = appearanceEntry as PdfIndirectReference
                                         ?? doc.RegisterObject(appearance);
            string xobjName = PageContentComposer.RegisterXObject(doc, page, apRef);
            string invocationStr = string.Format(
                CultureInfo.InvariantCulture,
                "q {0:G} {1:G} {2:G} {3:G} {4:G} {5:G} cm /{6} Do Q\n",
                aa[0], aa[1], aa[2], aa[3], aa[4], aa[5], xobjName);
            byte[] invocationBytes = Encoding.Latin1.GetBytes(invocationStr);

            PdfArray contents = PageContentComposer.EnsureContentsArray(doc, page);
            PageContentComposer.AddInvocation(doc, contents, invocationBytes, underlay: false);
            RemoveWidgetFromAnnots(doc, page, widget);
        }

        RemoveFieldFromAcroForm(doc, field.Dict);
    }

    /// <summary>
    /// True when the document is a <i>dynamic</i> XFA form: an <c>/AcroForm /XFA</c> is present but
    /// there are no positioned AcroForm widgets to bake (the form's layout/data live only in the XFA
    /// template, which PdfLibrary does not render). Such a form cannot be flattened — and must not be,
    /// since dropping /XFA would leave only the placeholder shell. Hybrid forms (XFA + a full AcroForm,
    /// e.g. the IRS W-2) return false: their AcroForm representation is bakeable.
    /// </summary>
    public static bool IsDynamicXfa(PdfDocument doc)
    {
        if (!HasXfa(doc)) return false;
        // Hybrid forms have at least one widget placed on a page; dynamic forms have none.
        return !FormFieldTree.Read(doc).Any(f => f.Widgets.Any(w => w.PageIndex >= 0));
    }

    internal static bool HasXfa(PdfDocument doc)
    {
        if (doc.CatalogDictionary is not { } catalog) return false;
        if (Resolve(doc, catalog.Get(new PdfName("AcroForm"))) is not PdfDictionary acro) return false;
        return acro.Get(new PdfName("XFA")) is not null;
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

    /// <summary>True if the field carries a value worth baking (so its widget must not be removed
    /// without painting). Empty fields have nothing to lose and can be removed during flatten.</summary>
    private static bool FieldHasValue(PdfFormField field) => field switch
    {
        PdfTextField t    => !string.IsNullOrEmpty(t.Value),
        PdfChoiceField c  => c.SelectedValues.Count > 0,
        PdfButtonField b  => b.IsChecked || b.SelectedOption is not null,
        _                 => false
    };

    /// <summary>Generates a normal appearance for a valued text/choice field that may lack one, so
    /// flatten has a Form-XObject to bake. Buttons are state-keyed and already carry their /AP.</summary>
    private static void EnsureBakeableAppearance(PdfDocument doc, PdfFormField field)
    {
        if (!FieldHasValue(field)) return;
        if (field is PdfTextField or PdfChoiceField)
            FieldAppearanceGenerator.Regenerate(doc, field);
    }

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
    internal static void RemoveWidgetFromAnnots(
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
    /// Removes the field dict from the AcroForm field tree. The W-2 (and any LiveCycle/XFA form)
    /// nests terminal fields under subforms — e.g. <c>topmostSubform[0].CopyA[0].Col_Left[0].f2_04[0]</c>
    /// — so the target dict is a kid-of-a-kid, never a direct entry of <c>/AcroForm /Fields</c>.
    /// Scanning only the top level (the old behaviour) silently no-ops on those forms, leaving every
    /// field live after flatten. This walks the tree, removes the target wherever it sits, and prunes
    /// any parent subform left with an empty <c>/Kids</c>.
    /// </summary>
    internal static void RemoveFieldFromAcroForm(PdfDocument doc, PdfDictionary fieldDict)
    {
        PdfDictionary? catalog = doc.CatalogDictionary;
        if (catalog is null) return;

        if (Resolve(doc, catalog.Get(new PdfName("AcroForm"))) is not PdfDictionary acro) return;
        if (Resolve(doc, acro.Get(new PdfName("Fields"))) is not PdfArray fields) return;

        RemoveFromFieldArray(doc, fields, fieldDict);
    }

    /// <summary>
    /// Recursively removes <paramref name="target"/> from <paramref name="container"/> (a /Fields or
    /// /Kids array). Returns true once found. After descending into a subform's /Kids, the now-empty
    /// subform is pruned from its own container.
    /// </summary>
    private static bool RemoveFromFieldArray(PdfDocument doc, PdfArray container, PdfDictionary target)
    {
        for (int i = 0; i < container.Count; i++)
        {
            PdfLibrary.Core.PdfObject entry = container[i];
            if (EntryMatches(doc, entry, target))
            {
                container.RemoveAt(i);
                return true;
            }

            if (Resolve(doc, entry) is not PdfDictionary entryDict) continue;
            if (Resolve(doc, entryDict.Get(new PdfName("Kids"))) is not PdfArray kids) continue;

            if (RemoveFromFieldArray(doc, kids, target))
            {
                // Prune the parent subform if flattening emptied its /Kids.
                if (Resolve(doc, entryDict.Get(new PdfName("Kids"))) is PdfArray { Count: 0 })
                    container.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>Matches an array entry against a field dict by object number (indirect) or reference.</summary>
    private static bool EntryMatches(PdfDocument doc, PdfLibrary.Core.PdfObject entry, PdfDictionary target)
    {
        if (entry is PdfIndirectReference ir)
        {
            if (target.IsIndirect && target.ObjectNumber == ir.ObjectNumber) return true;
            return ReferenceEquals(doc.GetObject(ir.ObjectNumber), target);
        }
        return ReferenceEquals(entry, target);
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

    private static double[]? ReadNumberArray(PdfDocument doc, PdfLibrary.Core.PdfObject? raw, int count)
    {
        if (Resolve(doc, raw) is not PdfArray array || array.Count < count) return null;

        var result = new double[count];
        for (var i = 0; i < count; i++)
        {
            PdfLibrary.Core.PdfObject? value = Resolve(doc, array[i]);
            result[i] = value switch
            {
                PdfReal r => r.Value,
                PdfInteger integer => integer.Value,
                _ => double.NaN
            };
            if (!double.IsFinite(result[i])) return null;
        }

        return result;
    }
}
