namespace PdfLibrary.Fonts;

/// <summary>
/// Splits a PFB-formatted Type 1 program into its bare concatenated bytes plus the clear-text
/// (<c>/Length1</c>), encrypted (<c>/Length2</c>) and trailer (<c>/Length3</c>) segment lengths a PDF
/// <c>/FontFile</c> stream requires — the PFB format's own 6-byte segment headers carry those
/// boundaries, so this recovers them exactly rather than guessing.
///
/// <para>Shared by <see cref="Editing.PdfDocumentEditor.EmbedProgram"/> (which writes the split
/// result) and <see cref="Remediation.FontRemediationPlanner"/> (which must decline a program that
/// would fail here BEFORE it ever reaches <c>EmbedProgram</c>, so a proposal never survives to throw
/// during a user's Save) — extracted so the two validations cannot diverge. Was previously a private
/// method duplicated in intent (not code) between the two call sites; a caller that only mirrored the
/// leading-byte check risked missing a corrupt segment table or a missing binary segment that only
/// this full walk detects.</para>
/// </summary>
internal static class Type1PfbSegments
{
    /// <summary>
    /// Splits <paramref name="program"/>. Throws <see cref="NotSupportedException"/> when the
    /// segment lengths cannot be determined: a bare PFA program (no leading <c>0x80</c> segment
    /// marker — the ASCII format carries no embedded segment boundaries), a corrupt segment table (a
    /// segment claims more bytes than the buffer contains, or names an unrecognised segment type), or
    /// a PFB program with no binary (<c>eexec</c>-encrypted) segment at all.
    /// </summary>
    public static (byte[] Data, int Length1, int Length2, int Length3) Split(byte[] program)
    {
        if (program.Length == 0 || program[0] != 0x80)
        {
            throw new NotSupportedException(
                "Cannot determine Type 1 /Length1, /Length2 and /Length3 for a bare PFA program: " +
                "the ASCII format carries no embedded segment markers. Embed a PFB-formatted " +
                "program instead, or decline it before calling EmbedProgram.");
        }

        var data = new List<byte>(program.Length);
        int length1 = 0, length2 = 0, length3 = 0;
        var sawBinary = false;
        var offset = 0;

        while (offset + 6 <= program.Length && program[offset] == 0x80)
        {
            byte segmentType = program[offset + 1];
            if (segmentType == 3) break; // EOF marker — no length field follows.

            int segmentLength = program[offset + 2]
                | (program[offset + 3] << 8)
                | (program[offset + 4] << 16)
                | (program[offset + 5] << 24);
            offset += 6;

            if (segmentLength < 0 || offset + segmentLength > program.Length)
            {
                throw new NotSupportedException(
                    "Type 1 PFB segment table is corrupt: a segment claims more bytes than the program contains.");
            }

            for (var i = 0; i < segmentLength; i++)
                data.Add(program[offset + i]);

            switch (segmentType)
            {
                case 1 when !sawBinary:
                    length1 += segmentLength;
                    break;
                case 2:
                    length2 += segmentLength;
                    sawBinary = true;
                    break;
                case 1:
                    length3 += segmentLength;
                    break;
                default:
                    throw new NotSupportedException($"Unrecognised Type 1 PFB segment type {segmentType}.");
            }

            offset += segmentLength;
        }

        if (length2 == 0)
        {
            throw new NotSupportedException(
                "Cannot determine Type 1 segment lengths: the PFB program has no binary (eexec-encrypted) segment.");
        }

        return (data.ToArray(), length1, length2, length3);
    }
}
