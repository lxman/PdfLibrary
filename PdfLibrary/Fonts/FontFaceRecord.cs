namespace PdfLibrary.Fonts;

/// <summary>One face of one installed font file, identified by the fields a substitution decision
/// actually needs. <paramref name="PostScriptName"/> (name ID 6) is the primary key: ASCII by
/// specification, free of language variants, and exactly what a PDF's /BaseFont derives from.
/// <paramref name="Families"/> holds EVERY localized ID 1 / ID 16 record so a document naming a font
/// by its localized family still resolves; <paramref name="EnglishFamily"/> is only for
/// canonicalisation and deterministic tie-breaking.</summary>
internal sealed record FontFaceRecord(
    string Path,
    int FaceIndex,
    string PostScriptName,
    IReadOnlyCollection<string> Families,
    string EnglishFamily,
    string Subfamily,
    bool Italic,
    bool Bold);
