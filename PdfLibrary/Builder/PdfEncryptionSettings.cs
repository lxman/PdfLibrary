using PdfLibrary.Security;

namespace PdfLibrary.Builder;

/// <summary>
/// Configuration for PDF document encryption.
/// </summary>
public class PdfEncryptionSettings
{
    /// <summary>
    /// Password required to open the document.
    /// Can be empty for documents that don't require a password to open
    /// but still have restricted permissions.
    /// </summary>
    public string UserPassword { get; private set; } = "";

    /// <summary>
    /// Password for full document access (owner password).
    /// If not set, defaults to the user password.
    /// </summary>
    public string OwnerPassword { get; private set; } = "";

    /// <summary>
    /// Encryption method to use.
    /// </summary>
    public PdfEncryptionMethod Method { get; private set; } = PdfEncryptionMethod.Aes256;

    /// <summary>
    /// Document permissions.
    /// </summary>
    public PdfPermissionFlags PermissionFlags { get; private set; } = PdfPermissionFlags.All;

    /// <summary>
    /// Sets the user password (required to open the document).
    /// </summary>
    public PdfEncryptionSettings WithUserPassword(string password)
    {
        UserPassword = password ?? "";
        return this;
    }

    /// <summary>
    /// Sets the owner password (for full access).
    /// If not set, defaults to the user password.
    /// </summary>
    public PdfEncryptionSettings WithOwnerPassword(string password)
    {
        OwnerPassword = password ?? "";
        return this;
    }

    /// <summary>
    /// Sets the encryption method.
    /// </summary>
    public PdfEncryptionSettings WithMethod(PdfEncryptionMethod method)
    {
        Method = method;
        return this;
    }

    /// <summary>
    /// Sets the document permissions.
    /// </summary>
    public PdfEncryptionSettings WithPermissions(PdfPermissionFlags permissions)
    {
        PermissionFlags = permissions;
        return this;
    }

    /// <summary>
    /// Allows printing the document.
    /// </summary>
    public PdfEncryptionSettings AllowPrinting(bool highQuality = true)
    {
        PermissionFlags |= PdfPermissionFlags.Print;
        if (highQuality)
            PermissionFlags |= PdfPermissionFlags.PrintHighQuality;
        return this;
    }

    /// <summary>
    /// Allows copying content from the document.
    /// </summary>
    public PdfEncryptionSettings AllowCopying()
    {
        PermissionFlags |= PdfPermissionFlags.CopyContent;
        return this;
    }

    /// <summary>
    /// Allows modifying document contents.
    /// </summary>
    public PdfEncryptionSettings AllowModifying()
    {
        PermissionFlags |= PdfPermissionFlags.ModifyContents;
        return this;
    }

    /// <summary>
    /// Allows adding/modifying annotations.
    /// </summary>
    public PdfEncryptionSettings AllowAnnotations()
    {
        PermissionFlags |= PdfPermissionFlags.ModifyAnnotations;
        return this;
    }

    /// <summary>
    /// Allows filling in form fields.
    /// </summary>
    public PdfEncryptionSettings AllowFormFilling()
    {
        PermissionFlags |= PdfPermissionFlags.FillForms;
        return this;
    }

    /// <summary>
    /// Allows content extraction for accessibility.
    /// </summary>
    public PdfEncryptionSettings AllowAccessibility()
    {
        PermissionFlags |= PdfPermissionFlags.ExtractForAccessibility;
        return this;
    }

    /// <summary>
    /// Allows document assembly (insert, rotate, delete pages).
    /// </summary>
    public PdfEncryptionSettings AllowAssembly()
    {
        PermissionFlags |= PdfPermissionFlags.AssembleDocument;
        return this;
    }

    /// <summary>
    /// Denies all permissions (most restrictive).
    /// </summary>
    public PdfEncryptionSettings DenyAll()
    {
        PermissionFlags = PdfPermissionFlags.None;
        return this;
    }

    /// <summary>
    /// Allows all permissions (least restrictive).
    /// </summary>
    public PdfEncryptionSettings AllowAll()
    {
        PermissionFlags = PdfPermissionFlags.All;
        return this;
    }
}
