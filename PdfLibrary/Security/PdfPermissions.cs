namespace PdfLibrary.Security;

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
