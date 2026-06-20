using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing.Forms;

/// <summary>
/// Abstract base for all AcroForm field types.
/// </summary>
public abstract class PdfFormField
{
    /// <summary>Fully-qualified name (ancestor /T values joined by ".").</summary>
    public string FullName { get; internal set; } = string.Empty;

    /// <summary>Partial name — the field's own /T value.</summary>
    public string PartialName { get; internal set; } = string.Empty;

    /// <summary>Field type derived from /FT.</summary>
    public PdfFormFieldType Type { get; internal set; }

    /// <summary>/Ff bit 1 — read-only in interactive viewers.</summary>
    public bool IsReadOnly { get; internal set; }

    /// <summary>/Ff bit 2 — required for submission.</summary>
    public bool IsRequired { get; internal set; }

    /// <summary>The field's own dictionary (may also be a merged/effective dict).</summary>
    internal PdfDictionary Dict { get; set; } = null!;

    /// <summary>The document that owns this field.</summary>
    internal PdfDocument Doc { get; set; } = null!;

    /// <summary>Widget annotation dictionaries (the visual representations).</summary>
    internal IReadOnlyList<PdfDictionary> Widgets { get; set; } = Array.Empty<PdfDictionary>();
}

/// <summary>A /Tx (text) field.</summary>
public sealed class PdfTextField : PdfFormField
{
    private string? _value;

    /// <summary>Current value (/V). Setting regenerates the field's appearance stream.</summary>
    public string? Value
    {
        get => _value;
        set
        {
            _value = value;
            // Write /V into the field dict
            Dict[new PdfName("V")] = value is null
                ? (PdfObject)PdfNull.Instance
                : PdfString.FromText(value);
            // Regenerate the appearance stream
            FieldAppearanceGenerator.Regenerate(Doc, this);
        }
    }

    /// <summary>Internal setter used by the field-tree builder (does not trigger AP regeneration).</summary>
    internal void SetValueInternal(string? value) => _value = value;

    /// <summary>/MaxLen, or null if not set.</summary>
    public int? MaxLength { get; internal set; }

    /// <summary>/Ff bit 13.</summary>
    public bool IsMultiline { get; internal set; }

    /// <summary>/Ff bit 25 (requires /MaxLen).</summary>
    public bool IsComb { get; internal set; }

    /// <summary>/Ff bit 14 — value is settable; AP shows bullets.</summary>
    public bool IsPassword { get; internal set; }

    /// <summary>/Q: 0=left, 1=centre, 2=right.</summary>
    public int Quadding { get; internal set; }
}

/// <summary>A /Btn (button) field — checkbox, radio, or push button.</summary>
public sealed class PdfButtonField : PdfFormField
{
    /// <summary>Checkbox, Radio, or Push.</summary>
    public ButtonKind Kind { get; internal set; }

    /// <summary>For checkboxes: whether the current /V or /AS is not /Off.</summary>
    public bool IsChecked { get; internal set; }

    /// <summary>For radio groups: union of non-Off on-state names across all widgets.</summary>
    public IReadOnlyList<string> Options { get; internal set; } = Array.Empty<string>();

    private string? _selectedOption;

    /// <summary>For radio groups: the current /V (selected option), or null.</summary>
    public string? SelectedOption
    {
        get => _selectedOption;
        set
        {
            switch (Kind)
            {
                case ButtonKind.Radio:
                    ButtonStateWriter.SetRadio(Doc, this, value
                        ?? throw new ArgumentNullException(nameof(value)));
                    _selectedOption = value;
                    break;
                case ButtonKind.Checkbox:
                    throw new InvalidOperationException(
                        "Use Check() / Uncheck() for checkbox fields; SelectedOption is radio-only.");
                default:
                    throw new InvalidOperationException("Push buttons hold no value.");
            }
        }
    }

    internal void SetSelectedOptionInternal(string? value) => _selectedOption = value;

    /// <summary>Sets the checkbox to its on-state.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the field is not a checkbox.</exception>
    public void Check()
    {
        if (Kind != ButtonKind.Checkbox)
            throw new InvalidOperationException("Check() is only valid for checkbox fields.");
        ButtonStateWriter.SetCheckbox(Doc, this, true);
        IsChecked = true;
    }

    /// <summary>Sets the checkbox to /Off.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the field is not a checkbox.</exception>
    public void Uncheck()
    {
        if (Kind != ButtonKind.Checkbox)
            throw new InvalidOperationException("Uncheck() is only valid for checkbox fields.");
        ButtonStateWriter.SetCheckbox(Doc, this, false);
        IsChecked = false;
    }
}

/// <summary>A /Ch (choice) field — combo box or list box.</summary>
public sealed class PdfChoiceField : PdfFormField
{
    /// <summary>Options from /Opt (export value, display text pairs).</summary>
    public IReadOnlyList<(string Export, string Display)> Options { get; internal set; }
        = Array.Empty<(string, string)>();

    /// <summary>/Ff bit 18 — combo box vs list box.</summary>
    public bool IsCombo { get; internal set; }

    /// <summary>/Ff bit 22.</summary>
    public bool IsMultiSelect { get; internal set; }

    /// <summary>Currently selected export values.</summary>
    public IReadOnlyList<string> SelectedValues { get; internal set; } = Array.Empty<string>();

    /// <summary>Currently selected indices (/I).</summary>
    public IReadOnlyList<int> SelectedIndices { get; internal set; } = Array.Empty<int>();
}

/// <summary>A /Sig (signature) field.</summary>
public sealed class PdfSignatureField : PdfFormField
{
    /// <summary>True when /V is present (field has been signed).</summary>
    public bool IsSigned { get; internal set; }
}
