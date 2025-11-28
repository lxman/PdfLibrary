namespace PdfLibrary.Security;

/// <summary>
/// Represents PDF document permissions as defined in ISO 32000-1:2008 Table 22.
/// These permissions are set by the document creator and enforced by PDF readers.
/// Note: These are "honor system" restrictions - the content is still decryptable.
/// </summary>
[Flags]
public enum PdfPermissionFlags
{
    /// <summary>No permissions granted</summary>
    None = 0,

    /// <summary>Bit 3: Print the document</summary>
    Print = 1 << 2,

    /// <summary>Bit 4: Modify contents (other than annotations, form fields, etc.)</summary>
    ModifyContents = 1 << 3,

    /// <summary>Bit 5: Copy or extract text and graphics</summary>
    CopyContent = 1 << 4,

    /// <summary>Bit 6: Add or modify annotations and form fields</summary>
    ModifyAnnotations = 1 << 5,

    /// <summary>Bit 9: Fill in form fields (even if bit 6 is clear)</summary>
    FillForms = 1 << 8,

    /// <summary>Bit 10: Extract text/graphics for accessibility</summary>
    ExtractForAccessibility = 1 << 9,

    /// <summary>Bit 11: Assemble document (insert, rotate, delete pages, bookmarks)</summary>
    AssembleDocument = 1 << 10,

    /// <summary>Bit 12: Print high quality (degraded printing if clear)</summary>
    PrintHighQuality = 1 << 11,

    /// <summary>All permissions granted</summary>
    All = Print | ModifyContents | CopyContent | ModifyAnnotations |
          FillForms | ExtractForAccessibility | AssembleDocument | PrintHighQuality
}

/// <summary>
/// Provides a friendly API for checking PDF document permissions.
/// </summary>
public class PdfPermissions
{
    private readonly PdfPermissionFlags _flags;
    private readonly int _rawValue;

    /// <summary>
    /// Creates permissions from the raw /P value in the encryption dictionary.
    /// </summary>
    /// <param name="pValue">The /P value (32-bit signed integer)</param>
    public PdfPermissions(int pValue)
    {
        _rawValue = pValue;
        // The /P value has bits 1-2 and 13-32 reserved (must be 1)
        // We only care about bits 3-12
        _flags = (PdfPermissionFlags)(pValue & 0x0F3C);
    }

    /// <summary>
    /// Creates permissions with all flags granted (unencrypted document).
    /// </summary>
    public static PdfPermissions AllPermissions => new(unchecked((int)0xFFFFFFFF));

    /// <summary>Gets the raw /P value</summary>
    public int RawValue => _rawValue;

    /// <summary>Gets the parsed permission flags</summary>
    public PdfPermissionFlags Flags => _flags;

    /// <summary>Can print the document (may be low quality if PrintHighQuality is false)</summary>
    public bool CanPrint => _flags.HasFlag(PdfPermissionFlags.Print);

    /// <summary>Can print at full resolution</summary>
    public bool CanPrintHighQuality => _flags.HasFlag(PdfPermissionFlags.PrintHighQuality);

    /// <summary>Can modify document contents</summary>
    public bool CanModifyContents => _flags.HasFlag(PdfPermissionFlags.ModifyContents);

    /// <summary>Can copy text and graphics</summary>
    public bool CanCopyContent => _flags.HasFlag(PdfPermissionFlags.CopyContent);

    /// <summary>Can add or modify annotations and form fields</summary>
    public bool CanModifyAnnotations => _flags.HasFlag(PdfPermissionFlags.ModifyAnnotations);

    /// <summary>Can fill in form fields</summary>
    public bool CanFillForms => _flags.HasFlag(PdfPermissionFlags.FillForms);

    /// <summary>Can extract content for accessibility purposes</summary>
    public bool CanExtractForAccessibility => _flags.HasFlag(PdfPermissionFlags.ExtractForAccessibility);

    /// <summary>Can assemble document (insert, rotate, delete pages)</summary>
    public bool CanAssembleDocument => _flags.HasFlag(PdfPermissionFlags.AssembleDocument);

    public override string ToString()
    {
        var allowed = new List<string>();
        if (CanPrint) allowed.Add("Print");
        if (CanPrintHighQuality) allowed.Add("PrintHQ");
        if (CanModifyContents) allowed.Add("Modify");
        if (CanCopyContent) allowed.Add("Copy");
        if (CanModifyAnnotations) allowed.Add("Annotate");
        if (CanFillForms) allowed.Add("FillForms");
        if (CanExtractForAccessibility) allowed.Add("Accessibility");
        if (CanAssembleDocument) allowed.Add("Assemble");

        return allowed.Count > 0 ? string.Join(", ", allowed) : "None";
    }
}
