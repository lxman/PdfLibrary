namespace PdfLibrary.Fonts;

/// <summary>
/// The shape of a font program, naming exactly the cases the editor can write into a font
/// descriptor. A caller cannot hand a later embedding step bytes without saying what they are —
/// ISO 32000-2 §9.9 makes the stream key and subtype depend on it, and guessing from the caller's
/// side is how a TrueType program ends up in /FontFile3.
/// </summary>
public enum FontProgramFormat
{
    /// <summary>sfnt with TrueType outlines. Written to /FontFile2.</summary>
    TrueType,

    /// <summary>Bare CFF, non-CID. /FontFile3 with /Subtype /Type1C.</summary>
    Type1C,

    /// <summary>CID-keyed CFF. /FontFile3 with /Subtype /CIDFontType0C.</summary>
    CidFontType0C,

    /// <summary>sfnt with CFF outlines ('OTTO'). /FontFile3 with /Subtype /OpenType.</summary>
    OpenType,

    /// <summary>Classic Type 1. /FontFile with /Length1, /Length2 and /Length3.</summary>
    Type1,
}

/// <summary>A font program classified and, if it was a TrueType Collection face, extracted to a
/// standalone sfnt ready to embed.</summary>
public sealed record ClassifiedProgram(byte[] Program, FontProgramFormat Format);
