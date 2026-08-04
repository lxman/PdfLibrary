namespace PdfLibrary.Fonts;

/// <summary>A request to substitute a font the renderer could not use from the PDF itself.
/// <paramref name="Serif"/> and <paramref name="Mono"/> carry the /FontDescriptor's Serif and
/// FixedPitch flags, which for a subset name like "ABCDEF+XYZ123" are the ONLY family signal there
/// is — the name spells nothing. They default to false so a provider constructing a request from a
/// bare /BaseFont keeps compiling and keeps its previous meaning.</summary>
public sealed record FontRequest(string BaseFont, bool Bold, bool Italic, bool Serif = false, bool Mono = false);

/// <summary>A resolved substitute. <paramref name="FaceIndex"/> matters: on macOS the core families
/// live in .ttc collections where Regular/Bold/Italic/BoldItalic share one file, so bytes alone
/// cannot express which face was chosen.</summary>
public sealed record FontMatch(byte[] Data, int FaceIndex);
