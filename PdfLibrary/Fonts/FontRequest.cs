namespace PdfLibrary.Fonts;

/// <summary>A request to substitute a font the renderer could not use from the PDF itself.
/// <paramref name="Serif"/> and <paramref name="Mono"/> carry the /FontDescriptor's Serif and
/// FixedPitch flags, which for a subset name like "ABCDEF+XYZ123" are the ONLY family signal there
/// is — the name spells nothing.
///
/// <para><paramref name="ExplicitBold"/> and <paramref name="ExplicitItalic"/> are a NARROWER pair
/// than <paramref name="Bold"/> and <paramref name="Italic"/>: they carry only what the document
/// stated outright — the descriptor's style flags, and (merged in by the provider) explicit style
/// tokens in the name. They deliberately exclude the StemV >= 120 inference, which is a guess about
/// a number rather than a statement of intent. Ladder steps that are already guessing use the merged
/// pair; the step that can override an exact PostScript-name match uses this one, so a heavy stem
/// width can never swap out a face the document named.</para>
///
/// <para>One documented exception to "only what the document stated outright":
/// <see cref="SubstituteFontResolver"/>'s synthetic-standard-14 retry builds its /BaseFont from the
/// MERGED bold/italic pair, and a provider then re-derives the explicit pair from that name's own
/// "-Bold"/"-Italic" tokens — so on that one hop a StemV inference does reach the explicit pair.
/// Benign, not a leak worth plugging: the retry only fires when the first Resolve returned null, by
/// which point the whole request is synthetic and there is no document-named face left to protect.
/// (It never fires for <see cref="SystemFontLocator"/> at all — step 3 of its ladder has already
/// tried the same name.)</para>
///
/// <para><paramref name="Serif"/>, <paramref name="Mono"/>, <paramref name="ExplicitBold"/> and
/// <paramref name="ExplicitItalic"/> default to false so a provider constructing a request from a
/// bare /BaseFont keeps compiling and keeps its previous meaning. <paramref name="Bold"/> and
/// <paramref name="Italic"/> are required — they predate the rest.</para></summary>
public sealed record FontRequest(
    string BaseFont,
    bool Bold,
    bool Italic,
    bool Serif = false,
    bool Mono = false,
    bool ExplicitBold = false,
    bool ExplicitItalic = false);

/// <summary>A resolved substitute. <paramref name="FaceIndex"/> matters: on macOS the core families
/// live in .ttc collections where Regular/Bold/Italic/BoldItalic share one file, so bytes alone
/// cannot express which face was chosen.</summary>
public sealed record FontMatch(byte[] Data, int FaceIndex);
