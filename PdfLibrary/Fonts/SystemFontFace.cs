namespace PdfLibrary.Fonts;

/// <summary>
/// One face of one installed font file, exposed publicly for the manual substitute-face picker
/// ("list what you have"). A read-only projection of the locator's internal metadata index — no
/// new scanning happens to produce it.
/// </summary>
/// <param name="Family">English family name, display-ready.</param>
/// <param name="PostScriptName">The face's PostScript name (name ID 6).</param>
/// <param name="Bold">Whether the face's own metadata marks it bold.</param>
/// <param name="Italic">Whether the face's own metadata marks it italic.</param>
/// <param name="Path">File path the picker loads bytes from. May point at a <c>.ttc</c> collection,
/// in which case <paramref name="FaceIndex"/> identifies which member face this record describes.</param>
/// <param name="FaceIndex">Face index within a <c>.ttc</c> collection; 0 for a bare sfnt.</param>
public sealed record SystemFontFace(
    string Family,
    string PostScriptName,
    bool Bold, bool Italic,
    string Path,
    int FaceIndex);
