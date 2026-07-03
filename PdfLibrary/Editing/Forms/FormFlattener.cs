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

        foreach (PdfDictionary widget in field.WidgetDicts)
        {
            // Find the owning page: the page whose /Annots array contains this widget.
            // We match by object identity (object number if indirect, or reference equality
            // for direct dicts that the tree walk has already resolved).
            PdfDictionary? owningPage = FindOwningPage(doc, pages, widget);
            if (owningPage is null) continue;

            // Resolve /AP /N to a Form-XObject stream.
            PdfLibrary.Core.PdfObject? apRaw = widget.Get(new PdfName("AP"));
            PdfLibrary.Core.PdfObject? apResolved = Resolve(doc, apRaw);
            if (apResolved is not PdfDictionary apDict)
            {
                // No appearance at all. A full flatten must not leave the widget behind, but we must
                // also not drop a value we failed to render — remove only when there is nothing to
                // bake (RC2 clean-up / RC3 no-data-loss).
                if (!FieldHasValue(field)) RemoveWidgetFromAnnots(doc, owningPage, widget);
                continue;
            }

            PdfLibrary.Core.PdfObject? nRaw = apDict.Get(new PdfName("N"));
            if (nRaw is null)
            {
                if (!FieldHasValue(field)) RemoveWidgetFromAnnots(doc, owningPage, widget);
                continue;
            }

            PdfLibrary.Core.PdfObject? nResolved = Resolve(doc, nRaw);

            // /AP /N is either a single Form-XObject stream (text fields, push-buttons) or a
            // state-keyed sub-dictionary (check boxes, radio buttons):
            //   << /<onState> <stream> /Off <stream> >>
            // For the state-keyed case, bake the stream named by the widget's /AS — the appearance
            // that is currently visible. Falling through without handling this (the old behaviour)
            // left the widget un-painted AND un-removed: an orphaned /Widget whose /Parent points at
            // a now-deleted field, which Adobe prunes on resave (the radios "disappear").
            PdfStream? nStream = null;
            PdfIndirectReference? apRef = null;

            if (nResolved is PdfStream singleStream)
            {
                nStream = singleStream;
                apRef = nRaw as PdfIndirectReference ?? doc.RegisterObject(singleStream);
            }
            else if (nResolved is PdfDictionary stateDict)
            {
                string asState = widget.Get(new PdfName("AS")) is PdfName asName ? asName.Value : "Off";
                PdfLibrary.Core.PdfObject? stateRaw = stateDict.Get(new PdfName(asState));
                if (stateRaw is not null && Resolve(doc, stateRaw) is PdfStream stateStream)
                {
                    nStream = stateStream;
                    apRef = stateRaw as PdfIndirectReference ?? doc.RegisterObject(stateStream);
                }
            }

            // Verify we resolved a Form-XObject to paint. If not (e.g. the /Off state has no stream),
            // still remove the widget so it does not linger as an orphan.
            if (nStream is null || apRef is null ||
                nStream.Dictionary.Get(new PdfName("Subtype")) is not PdfName { Value: "Form" })
            {
                // Could not resolve a Form to paint (e.g. an /Off state with no stream). Remove only
                // when there is no value to lose, mirroring the no-/AP case above (RC3).
                if (!FieldHasValue(field)) RemoveWidgetFromAnnots(doc, owningPage, widget);
                continue;
            }

            // Register the XObject as a resource on the page.
            string xobjName = PageContentComposer.RegisterXObject(doc, owningPage, apRef);

            // Build the invocation using the widget /Rect to translate to the correct position.
            double rx0 = 0, ry0 = 0;
            PdfLibrary.Core.PdfObject? rectRaw = widget.Get(new PdfName("Rect"));
            PdfLibrary.Core.PdfObject? rectResolved = Resolve(doc, rectRaw);
            if (rectResolved is PdfArray { Count: >= 4 } rectArr)
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

    private static bool HasXfa(PdfDocument doc)
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

    private static double ToDouble(PdfLibrary.Core.PdfObject obj) => obj switch
    {
        PdfReal r   => r.Value,
        PdfInteger i => i.Value,
        _            => 0.0
    };
}
