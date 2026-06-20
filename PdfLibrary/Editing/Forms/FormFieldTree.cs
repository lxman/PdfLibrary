using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing.Forms;

/// <summary>
/// Reads the AcroForm field tree from a document and returns typed terminal-field objects.
/// </summary>
internal static class FormFieldTree
{
    public static List<PdfFormField> Read(PdfDocument doc)
    {
        var result = new List<PdfFormField>();

        PdfDictionary? catalog = doc.CatalogDictionary;
        if (catalog is null) return result;

        PdfObject? acroRaw = catalog.Get(new PdfName("AcroForm"));
        if (Resolve(doc, acroRaw) is not PdfDictionary acro) return result;

        PdfObject? fieldsRaw = acro.Get(new PdfName("Fields"));
        if (Resolve(doc, fieldsRaw) is not PdfArray topFields) return result;

        foreach (PdfObject fieldRef in topFields)
        {
            if (Resolve(doc, fieldRef) is PdfDictionary fieldDict)
                WalkField(doc, fieldDict, null, null, result);
        }

        return result;
    }

    private static void WalkField(
        PdfDocument doc,
        PdfDictionary dict,
        string? parentFullName,
        InheritedValues? parentInherited,
        List<PdfFormField> result)
    {
        // /T is the partial name for logical fields; widget-only nodes may not have it
        string? partialName = GetStringValue(dict, "T");

        string fullName = partialName is null
            ? parentFullName ?? string.Empty
            : parentFullName is null ? partialName : $"{parentFullName}.{partialName}";

        // Build the effective inherited values for this node
        InheritedValues inherited = MergeInherited(parentInherited, dict);

        // Determine whether this node has /Kids
        PdfObject? kidsRaw = dict.Get(new PdfName("Kids"));
        if (Resolve(doc, kidsRaw) is PdfArray kids && kids.Count > 0)
        {
            // Inspect the first kid to decide: sub-fields (have /T) vs widget annotations
            bool kidsAreWidgets = KidsAreWidgets(doc, kids);

            if (kidsAreWidgets)
            {
                // Terminal field: kids are all widget annotations
                var widgets = new List<PdfDictionary>();
                foreach (PdfObject kidRef in kids)
                {
                    if (Resolve(doc, kidRef) is PdfDictionary w)
                        widgets.Add(w);
                }
                BuildField(doc, dict, fullName, partialName ?? string.Empty, inherited, widgets, result);
            }
            else
            {
                // Intermediate node: recurse into sub-fields
                foreach (PdfObject kidRef in kids)
                {
                    if (Resolve(doc, kidRef) is PdfDictionary kidDict)
                        WalkField(doc, kidDict, fullName, inherited, result);
                }
            }
        }
        else
        {
            // No /Kids — terminal field; the dict itself is (also) the widget
            // Only create if this node has a /T (it is a logical field)
            if (partialName is not null)
            {
                // The field dict doubles as its own widget
                BuildField(doc, dict, fullName, partialName, inherited, new List<PdfDictionary> { dict }, result);
            }
            // else: orphan widget annotation with no /T — skip
        }
    }

    private static bool KidsAreWidgets(PdfDocument doc, PdfArray kids)
    {
        foreach (PdfObject kidRef in kids)
        {
            if (Resolve(doc, kidRef) is not PdfDictionary kid) continue;
            // A widget has /Subtype /Widget; a sub-field has /T
            if (kid.TryGetValue(new PdfName("T"), out _)) return false;
            if (kid.TryGetValue(new PdfName("Subtype"), out PdfObject sub)
                && sub is PdfName { Value: "Widget" })
                return true;
        }
        // If no clear indicator, check whether any kid has /T
        return true;
    }

    private static void BuildField(
        PdfDocument doc,
        PdfDictionary dict,
        string fullName,
        string partialName,
        InheritedValues inherited,
        IReadOnlyList<PdfDictionary> widgets,
        List<PdfFormField> result)
    {
        // Effective /FT: own dict wins over inherited
        string? ftStr = GetNameValue(dict, "FT") ?? inherited.Ft;
        int ff = GetFf(dict, inherited);

        PdfFormFieldType type = ftStr switch
        {
            "Tx"  => PdfFormFieldType.Text,
            "Btn" => FieldFlags.Has(ff, FieldFlags.Pushbutton)
                         ? PdfFormFieldType.PushButton
                         : FieldFlags.Has(ff, FieldFlags.Radio)
                             ? PdfFormFieldType.Radio
                             : PdfFormFieldType.Checkbox,
            "Ch"  => FieldFlags.Has(ff, FieldFlags.Combo)
                         ? PdfFormFieldType.ComboBox
                         : PdfFormFieldType.ListBox,
            "Sig" => PdfFormFieldType.Signature,
            _     => PdfFormFieldType.Unknown
        };

        bool isReadOnly  = FieldFlags.Has(ff, FieldFlags.ReadOnly);
        bool isRequired  = FieldFlags.Has(ff, FieldFlags.Required);

        PdfFormField field = type switch
        {
            PdfFormFieldType.Text      => BuildTextField(doc, dict, inherited, ff),
            PdfFormFieldType.Checkbox  => BuildButtonField(doc, dict, inherited, widgets, ff, ButtonKind.Checkbox),
            PdfFormFieldType.Radio     => BuildButtonField(doc, dict, inherited, widgets, ff, ButtonKind.Radio),
            PdfFormFieldType.PushButton => BuildButtonField(doc, dict, inherited, widgets, ff, ButtonKind.Push),
            PdfFormFieldType.ComboBox  => BuildChoiceField(doc, dict, inherited, ff, isCombo: true),
            PdfFormFieldType.ListBox   => BuildChoiceField(doc, dict, inherited, ff, isCombo: false),
            PdfFormFieldType.Signature => BuildSignatureField(doc, dict),
            _                          => BuildUnknownField()
        };

        field.FullName    = fullName;
        field.PartialName = partialName;
        field.Type        = type;
        field.IsReadOnly  = isReadOnly;
        field.IsRequired  = isRequired;
        field.Dict        = dict;
        field.Doc         = doc;
        field.Widgets     = widgets;

        result.Add(field);
    }

    private static PdfTextField BuildTextField(
        PdfDocument doc,
        PdfDictionary dict,
        InheritedValues inherited,
        int ff)
    {
        string? valueStr = GetEffectiveStringValue(doc, dict, inherited);
        int? maxLen = null;
        PdfObject? mlRaw = dict.Get(new PdfName("MaxLen"));
        if (mlRaw is PdfInteger mlInt)
            maxLen = (int)mlInt.Value;

        int q = 0;
        PdfObject? qRaw = dict.Get(new PdfName("Q"));
        if (qRaw is PdfInteger qInt)
            q = (int)qInt.Value;
        else if (inherited.Q.HasValue)
            q = inherited.Q.Value;

        var tf = new PdfTextField
        {
            MaxLength   = maxLen,
            IsMultiline = FieldFlags.Has(ff, FieldFlags.Multiline),
            IsComb      = FieldFlags.Has(ff, FieldFlags.Comb),
            IsPassword  = FieldFlags.Has(ff, FieldFlags.Password),
            Quadding    = q
        };
        tf.SetValueInternal(valueStr);
        return tf;
    }

    private static PdfButtonField BuildButtonField(
        PdfDocument doc,
        PdfDictionary dict,
        InheritedValues inherited,
        IReadOnlyList<PdfDictionary> widgets,
        int ff,
        ButtonKind kind)
    {
        bool isChecked = false;
        string? selectedOption = null;
        List<string> options = new();

        if (kind == ButtonKind.Radio)
        {
            // Options = union of each widget's /AP /N keys except /Off
            var optSet = new LinkedList<string>();
            foreach (PdfDictionary w in widgets)
            {
                foreach (string opt in GetWidgetOnStateNames(doc, w))
                {
                    if (!optSet.Contains(opt))
                        optSet.AddLast(opt);
                }
            }
            options.AddRange(optSet);

            // Selected option = parent /V
            PdfObject? vRaw = dict.Get(new PdfName("V")) ?? inherited.V;
            if (vRaw is PdfName vn && vn.Value != "Off")
                selectedOption = vn.Value;
            else if (vRaw is PdfString vs && vs.Value != "Off")
                selectedOption = vs.Value;
        }
        else if (kind == ButtonKind.Checkbox)
        {
            // IsChecked: /V or /AS != /Off
            PdfObject? vRaw = dict.Get(new PdfName("V")) ?? inherited.V;
            if (vRaw is PdfName vn)
                isChecked = vn.Value != "Off";
            else if (vRaw is PdfString vs)
                isChecked = vs.Value != "Off";

            // Also check /AS on the first widget
            if (!isChecked && widgets.Count > 0)
            {
                PdfObject? asRaw = widgets[0].Get(new PdfName("AS"));
                if (asRaw is PdfName asn)
                    isChecked = asn.Value != "Off";
            }

            // Export value options for checkbox (the on-state names)
            foreach (PdfDictionary w in widgets)
            {
                foreach (string opt in GetWidgetOnStateNames(doc, w))
                {
                    if (!options.Contains(opt))
                        options.Add(opt);
                }
            }
        }

        var buttonField = new PdfButtonField
        {
            Kind      = kind,
            IsChecked = isChecked,
            Options   = options
        };
        buttonField.SetSelectedOptionInternal(selectedOption);
        return buttonField;
    }

    private static PdfChoiceField BuildChoiceField(
        PdfDocument doc,
        PdfDictionary dict,
        InheritedValues inherited,
        int ff,
        bool isCombo)
    {
        // /Opt — array of strings or 2-element arrays
        var options = new List<(string Export, string Display)>();
        PdfObject? optRaw = dict.Get(new PdfName("Opt"));
        if (Resolve(doc, optRaw) is PdfArray optArr)
        {
            foreach (PdfObject item in optArr)
            {
                PdfObject resolved = Resolve(doc, item) ?? item;
                if (resolved is PdfArray pair && pair.Count >= 2)
                {
                    string export  = StringFrom(Resolve(doc, pair[0]));
                    string display = StringFrom(Resolve(doc, pair[1]));
                    options.Add((export, display));
                }
                else
                {
                    string val = StringFrom(resolved);
                    options.Add((val, val));
                }
            }
        }

        // /V — selected value(s)
        var selectedValues = new List<string>();
        PdfObject? vRaw = dict.Get(new PdfName("V")) ?? inherited.V;
        if (vRaw is not null)
        {
            PdfObject vResolved = Resolve(doc, vRaw) ?? vRaw;
            if (vResolved is PdfArray vArr)
            {
                foreach (PdfObject v in vArr)
                {
                    string sv = StringFrom(Resolve(doc, v));
                    if (!string.IsNullOrEmpty(sv)) selectedValues.Add(sv);
                }
            }
            else
            {
                string sv = StringFrom(vResolved);
                if (!string.IsNullOrEmpty(sv)) selectedValues.Add(sv);
            }
        }

        // /I — selected indices
        var selectedIndices = new List<int>();
        PdfObject? iRaw = dict.Get(new PdfName("I"));
        if (Resolve(doc, iRaw) is PdfArray iArr)
        {
            foreach (PdfObject idx in iArr)
            {
                if (idx is PdfInteger pi) selectedIndices.Add((int)pi.Value);
            }
        }

        var choiceField = new PdfChoiceField
        {
            Options       = options,
            IsCombo       = isCombo,
            IsMultiSelect = FieldFlags.Has(ff, FieldFlags.MultiSelect),
        };
        choiceField.SetSelectedValuesInternal(selectedValues);
        choiceField.SetSelectedIndicesInternal(selectedIndices);
        return choiceField;
    }

    private static PdfSignatureField BuildSignatureField(PdfDocument doc, PdfDictionary dict)
    {
        bool isSigned = dict.TryGetValue(new PdfName("V"), out PdfObject? vRaw)
                        && vRaw is not PdfNull;
        return new PdfSignatureField { IsSigned = isSigned };
    }

    private static PdfFormField BuildUnknownField() => new PdfUnknownField();

    // ── Helpers ────────────────────────────────────────────────────────────────

    internal static PdfObject? Resolve(PdfDocument doc, PdfObject? obj) =>
        obj is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : obj;

    private static string? GetStringValue(PdfDictionary dict, string key)
    {
        PdfObject? raw = dict.Get(new PdfName(key));
        return raw switch
        {
            PdfString s => s.Value,
            PdfName n   => n.Value,
            _           => null
        };
    }

    private static string? GetNameValue(PdfDictionary dict, string key)
    {
        PdfObject? raw = dict.Get(new PdfName(key));
        return raw is PdfName n ? n.Value : null;
    }

    private static int GetFf(PdfDictionary dict, InheritedValues inherited)
    {
        PdfObject? ffRaw = dict.Get(new PdfName("Ff"));
        if (ffRaw is PdfInteger ffInt) return (int)ffInt.Value;
        return inherited.Ff;
    }

    private static string? GetEffectiveStringValue(PdfDocument doc, PdfDictionary dict, InheritedValues inherited)
    {
        PdfObject? vRaw = dict.Get(new PdfName("V")) ?? inherited.V;
        if (vRaw is null) return null;
        PdfObject? resolved = Resolve(doc, vRaw);
        return resolved switch
        {
            PdfString s => s.GetText(),
            PdfName n   => n.Value == "Off" ? null : n.Value,
            _           => null
        };
    }

    private static IEnumerable<string> GetWidgetOnStateNames(PdfDocument doc, PdfDictionary widget)
    {
        PdfObject? apRaw = widget.Get(new PdfName("AP"));
        if (Resolve(doc, apRaw) is not PdfDictionary ap) yield break;

        PdfObject? nRaw = ap.Get(new PdfName("N"));
        PdfObject? nResolved = Resolve(doc, nRaw);
        if (nResolved is not PdfDictionary nDict) yield break;

        foreach (KeyValuePair<PdfName, PdfObject> kvp in nDict)
        {
            if (kvp.Key.Value != "Off")
                yield return kvp.Key.Value;
        }
    }

    private static string StringFrom(PdfObject? obj) => obj switch
    {
        PdfString s => s.GetText(),
        PdfName n   => n.Value,
        _           => string.Empty
    };

    // ── Inherited value tracking ────────────────────────────────────────────────

    private static InheritedValues MergeInherited(InheritedValues? parent, PdfDictionary dict)
    {
        // Child dict values override parent; only fields that are actually present override
        string? ft  = GetNameValue(dict, "FT")  ?? parent?.Ft;
        string? da  = GetStringValue(dict, "DA") ?? parent?.Da;
        int     ff  = dict.TryGetValue(new PdfName("Ff"), out PdfObject? ffRaw) && ffRaw is PdfInteger ffInt
                          ? (int)ffInt.Value
                          : parent?.Ff ?? 0;
        int?    q   = dict.TryGetValue(new PdfName("Q"), out PdfObject? qRaw) && qRaw is PdfInteger qInt
                          ? (int)qInt.Value
                          : parent?.Q;
        PdfObject? v = dict.TryGetValue(new PdfName("V"), out PdfObject? vOwn) ? vOwn : parent?.V;

        return new InheritedValues(ft, da, ff, q, v);
    }

    private sealed record InheritedValues(string? Ft, string? Da, int Ff, int? Q, PdfObject? V);
}

/// <summary>Field with an unrecognised /FT — placeholder so the tree walk still yields an entry.</summary>
internal sealed class PdfUnknownField : PdfFormField { }
