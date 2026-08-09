using FontParser;
using FontParser.Tables.Cff.Type1;

namespace PdfLibrary.Fonts;

/// <summary>
/// Decides which <c>/Subtype</c> a SIMPLE font dictionary must carry once a program of a given
/// <see cref="FontProgramFormat"/> is embedded in its descriptor — and refuses the combinations ISO
/// 32000-2 §9.9.1 Table 124 does not permit at all.
///
/// <para>Table 124 constrains the PAIR, not the stream key alone: <c>/FontFile2</c> "may appear in
/// the font descriptor for a TrueType font dictionary or … a CIDFontType2 CIDFont"; <c>/FontFile3</c>
/// with <c>/Subtype /Type1C</c> "for a Type1 or MMType1 font dictionary"; <c>/CIDFontType0C</c> only
/// for a CIDFontType0 CIDFont; <c>/OpenType</c> for a TrueType dictionary when the program has a
/// <c>glyf</c> table, and for a Type1 dictionary when it has a <c>CFF&#160;</c> table WITHOUT CIDFont
/// operators. Choosing the stream key from the program's format alone therefore closes the
/// <c>font-embedded</c> finding while opening a new violation whenever the resolved substitute's
/// format disagrees with the dictionary's declared subtype — the commonest real case, since a
/// <c>/Type1 /BaseFont /Helvetica</c> dictionary routinely resolves to a TrueType substitute.</para>
///
/// <para>Rewriting the dictionary's subtype is legal precisely because a simple font dictionary's
/// other entries do not depend on it: <c>/BaseFont</c>, <c>/FirstChar</c>, <c>/LastChar</c>,
/// <c>/Widths</c>, <c>/FontDescriptor</c> and <c>/Encoding</c> are the same entries in the same
/// meanings for Type1, MMType1 and TrueType (Table 109 / Table 111), so no other entry has to move
/// with it.</para>
///
/// <para>Shared by <see cref="Editing.PdfDocumentEditor.EmbedProgram"/> (which applies the answer)
/// and <see cref="Remediation.FontRemediationPlanner"/> (which must DECLINE a program this would
/// refuse, before a proposal survives to throw during a user's Save) — extracted for the same reason
/// <see cref="Type1PfbSegments"/> was: so the planner's prediction and the editor's validation
/// cannot diverge.</para>
///
/// <para>Composite fonts are out of scope; callers must not reach here for a Type0 wrapper or a
/// CIDFont dictionary.</para>
/// </summary>
internal static class SimpleFontProgramSubtype
{
    /// <summary>
    /// The <c>/Subtype</c> value a simple font dictionary currently declaring
    /// <paramref name="currentSubtype"/> must carry once <paramref name="program"/> is embedded under
    /// <paramref name="format"/>. <paramref name="currentSubtype"/> is consulted only to preserve
    /// <c>/MMType1</c> where Table 124 permits it equally with <c>/Type1</c>; whether this THROWS
    /// never depends on it, which is what lets the planner ask the question without having resolved a
    /// dictionary.
    /// </summary>
    /// <exception cref="NotSupportedException">No permitted Table 124 pair exists for this program in
    /// a simple font dictionary, or the program's own shape cannot be determined well enough to pick
    /// one. A decline is the honest outcome there; writing an out-of-spec pair is not.</exception>
    public static string Resolve(FontProgramFormat format, byte[] program, string? currentSubtype) =>
        format switch
        {
            // A TrueType program is permitted only in a TrueType font dictionary (Table 124,
            // /FontFile2). This is the rewrite the common Helvetica-to-a-system-TrueType case needs.
            FontProgramFormat.TrueType => "TrueType",

            // /FontFile (Type1) and /FontFile3 /Type1C are both permitted for "a Type1 or MMType1
            // font dictionary" — so an MMType1 dictionary keeps its subtype and everything else
            // becomes /Type1.
            FontProgramFormat.Type1 or FontProgramFormat.Type1C => Type1Flavoured(currentSubtype),

            FontProgramFormat.OpenType => ResolveOpenType(program, currentSubtype),

            // "CIDFontType0C … may appear in the font descriptor for a CIDFontType0 CIDFont
            // dictionary" — and nowhere else. A CID-keyed program in a simple font has no permitted
            // pair at all, and the CIDs it is keyed by are not character codes.
            FontProgramFormat.CidFontType0C => throw new NotSupportedException(
                "the program is a CID-keyed font, which ISO 32000-2 Table 124 permits only for a "
                + "composite (Type0) font, never for a simple one."),

            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown font program format."),
        };

    private static string Type1Flavoured(string? currentSubtype) =>
        currentSubtype == "MMType1" ? "MMType1" : "Type1";

    /// <summary>
    /// Table 124's OpenType row is the only one whose answer depends on the program's CONTENTS: a
    /// <c>glyf</c> table makes it a TrueType dictionary's program, a <c>CFF&#160;</c> table without
    /// CIDFont operators makes it a Type1 dictionary's, and a CID-keyed <c>CFF&#160;</c> is composite-only.
    /// Anything this cannot read is refused rather than guessed.
    /// </summary>
    private static string ResolveOpenType(byte[] program, string? currentSubtype)
    {
        SfntFont sfnt;
        try { sfnt = new SfntFont(program, 0); }
        catch (Exception ex)
        {
            throw new NotSupportedException(
                "the OpenType program's table directory could not be read, so it is not possible to "
                + $"tell which font dictionary ISO 32000-2 Table 124 permits it in ({ex.Message}).", ex);
        }

        var tags = new HashSet<string>(sfnt.TableTags, StringComparer.Ordinal);

        // glyf first: a program carrying outlines in glyf is a TrueType dictionary's program
        // regardless of anything else it happens to also carry.
        if (tags.Contains("glyf"))
            return "TrueType";

        byte[]? cff = tags.Contains("CFF ") ? sfnt.GetTableBytes("CFF ") : null;
        if (cff is null)
        {
            throw new NotSupportedException(
                "the OpenType program has neither a 'glyf' nor a 'CFF ' table, so ISO 32000-2 Table "
                + "124 permits it in no font dictionary.");
        }

        bool isCid;
        try { isCid = new Type1Table(cff).IsCid; }
        catch (Exception ex)
        {
            throw new NotSupportedException(
                "the OpenType program's 'CFF ' table could not be parsed, so it is not possible to "
                + $"tell whether it uses CIDFont operators ({ex.Message}).", ex);
        }

        if (isCid)
        {
            throw new NotSupportedException(
                "the OpenType program's 'CFF ' table uses CIDFont operators, which ISO 32000-2 Table "
                + "124 permits only for a composite (CIDFontType0) font, never for a simple one.");
        }

        return Type1Flavoured(currentSubtype);
    }
}
