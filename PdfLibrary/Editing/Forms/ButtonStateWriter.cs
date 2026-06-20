using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing.Forms;

/// <summary>
/// Writes button field state (AS / V) per ISO 32000 §12.7.4.2.3 (checkboxes) and §12.7.4.2.4 (radio buttons).
/// </summary>
internal static class ButtonStateWriter
{
    /// <summary>
    /// Sets a checkbox field to its on-state or to /Off.
    /// Sets /V on the field dict and /AS on every widget.
    /// </summary>
    public static void SetCheckbox(PdfDocument doc, PdfButtonField f, bool on)
    {
        string state = on ? CheckboxOnState(doc, f) : "Off";
        f.Dict[new PdfName("V")] = new PdfName(state);
        foreach (PdfDictionary widget in f.Widgets)
            widget[new PdfName("AS")] = new PdfName(state);
    }

    /// <summary>
    /// Resolves the on-state name for a checkbox field.
    /// Priority: /AP /N key, then Options list, then current /V if not "Off", then "Yes".
    /// </summary>
    private static string CheckboxOnState(PdfDocument doc, PdfButtonField f)
    {
        // 1. Try /AP /N on the first widget
        if (f.Widgets.Count > 0)
        {
            try { return OnState(doc, f.Widgets[0]); }
            catch (InvalidOperationException) { /* no AP — fall through */ }
        }

        // 2. Options list (populated from /AP /N during read)
        if (f.Options.Count > 0)
            return f.Options[0];

        // 3. Current /V on the field dict — if it is not Off, it IS the on-state export value
        PdfObject? vRaw = f.Dict.Get(new PdfName("V"));
        if (vRaw is PdfName vn && vn.Value != "Off")
            return vn.Value;

        // 4. Conventional default
        return "Yes";
    }

    /// <summary>
    /// Sets a radio group field to the named option.
    /// Sets /V on the field dict and /AS on each widget (on-state for matching widget, /Off for others).
    /// </summary>
    public static void SetRadio(PdfDocument doc, PdfButtonField f, string option)
    {
        if (!f.Options.Contains(option))
            throw new ArgumentException($"Option '{option}' is not among the radio group's options.", nameof(option));

        f.Dict[new PdfName("V")] = new PdfName(option);
        foreach (PdfDictionary widget in f.Widgets)
        {
            string widgetOnState = OnState(doc, widget);
            string asState = widgetOnState == option ? option : "Off";
            widget[new PdfName("AS")] = new PdfName(asState);
        }
    }

    /// <summary>
    /// Returns the on-state name for a widget by reading /AP /N and returning the single key that is not "Off".
    /// Throws <see cref="InvalidOperationException"/> if no on-state appearance is found.
    /// </summary>
    private static string OnState(PdfDocument doc, PdfDictionary widget)
    {
        PdfObject? apRaw = widget.Get(new PdfName("AP"));
        if (Resolve(doc, apRaw) is PdfDictionary ap)
        {
            PdfObject? nRaw = ap.Get(new PdfName("N"));
            if (Resolve(doc, nRaw) is PdfDictionary nDict)
            {
                foreach (KeyValuePair<PdfName, PdfObject> kvp in nDict)
                {
                    if (kvp.Key.Value != "Off")
                        return kvp.Key.Value;
                }
            }
        }

        throw new InvalidOperationException("Field widget has no on-state appearance in /AP /N.");
    }

    private static PdfObject? Resolve(PdfDocument doc, PdfObject? obj) =>
        obj is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : obj;
}
