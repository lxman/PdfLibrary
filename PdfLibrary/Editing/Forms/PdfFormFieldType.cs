namespace PdfLibrary.Editing.Forms;

/// <summary>AcroForm field types (ISO 32000 §12.7.3.1 Table 220 /FT values).</summary>
public enum PdfFormFieldType
{
    /// <summary>A text-entry field (/FT /Tx).</summary>
    Text,
    /// <summary>A checkbox button (/FT /Btn).</summary>
    Checkbox,
    /// <summary>A radio-button group (/FT /Btn).</summary>
    Radio,
    /// <summary>A push button (/FT /Btn).</summary>
    PushButton,
    /// <summary>An (optionally editable) drop-down choice field (/FT /Ch).</summary>
    ComboBox,
    /// <summary>A scrollable list choice field (/FT /Ch).</summary>
    ListBox,
    /// <summary>A signature field (/FT /Sig).</summary>
    Signature,
    /// <summary>An unrecognised field type.</summary>
    Unknown
}

/// <summary>Discriminates the three button sub-types.</summary>
public enum ButtonKind
{
    /// <summary>A checkbox.</summary>
    Checkbox,
    /// <summary>A radio button.</summary>
    Radio,
    /// <summary>A push button.</summary>
    Push
}
