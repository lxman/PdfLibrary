namespace PdfLibrary.Fonts;

/// <summary>A request to substitute a font the renderer could not use from the PDF itself.</summary>
public sealed record FontRequest(string BaseFont, bool Bold, bool Italic);

/// <summary>A resolved substitute. <paramref name="FaceIndex"/> matters: on macOS the core families
/// live in .ttc collections where Regular/Bold/Italic/BoldItalic share one file, so bytes alone
/// cannot express which face was chosen.</summary>
public sealed record FontMatch(byte[] Data, int FaceIndex);
