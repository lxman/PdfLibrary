namespace PdfLibrary.Editing.Forms;

/// <summary>AcroForm field types (ISO 32000 §12.7.3.1 Table 220 /FT values).</summary>
public enum PdfFormFieldType
{
    Text,
    Checkbox,
    Radio,
    PushButton,
    ComboBox,
    ListBox,
    Signature,
    Unknown
}

/// <summary>Discriminates the three button sub-types.</summary>
public enum ButtonKind
{
    Checkbox,
    Radio,
    Push
}
