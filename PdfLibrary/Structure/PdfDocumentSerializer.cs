using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Structure;

/// <summary>
/// Phase 0 full-rewrite serializer: writes a loaded <see cref="PdfDocument"/>'s object graph
/// back to a valid (unencrypted) PDF. Original object numbers are preserved so indirect
/// references stay valid without remapping.
/// </summary>
internal static class PdfDocumentSerializer
{
    private static readonly byte[] BinaryMarker = [0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]; // "%âãÏÓ\n"

    /// <summary>
    /// Serializes one indirect object as "N G obj ... endobj\n".
    /// Streams go through <see cref="PdfStream.ToBytes"/>; everything else through ToPdfString().
    /// (PdfStream.ToPdfString() is only a human-readable placeholder, so it must NOT be used here.)
    /// </summary>
    internal static byte[] SerializeIndirectObject(int objectNumber, int generationNumber, PdfObject obj)
    {
        if (obj is PdfStream s)
        {
            using var ms = new MemoryStream();
            ms.Write(Encoding.ASCII.GetBytes($"{objectNumber} {generationNumber} obj\n"));
            ms.Write(s.ToBytes());
            ms.Write(Encoding.ASCII.GetBytes("\nendobj\n"));
            return ms.ToArray();
        }

        return Encoding.ASCII.GetBytes(
            PdfIndirectReference.ToIndirectObjectDefinition(objectNumber, generationNumber, obj) + "\n");
    }

    public static void Write(PdfDocument document, Stream stream, ISet<int>? liveObjects = null)
    {
        if (document.IsEncrypted)
            throw new NotSupportedException(
                "Saving encrypted documents is not yet supported (Phase 0 rewrites unencrypted PDFs only).");
        if (document.Trailer.Root is null)
            throw new InvalidOperationException("Document has no /Root catalog; cannot serialize.");

        document.MaterializeAllObjects();

        // Header
        stream.Write(Encoding.ASCII.GetBytes($"%PDF-{document.Version}\n"));
        stream.Write(BinaryMarker);

        // Body — preserve original object numbers; record byte offsets AND generations. The
        // generation is captured here rather than defaulted in the xref, because the object header
        // written on the next line uses the object's real GenerationNumber: recording anything else
        // would make the table contradict the body (issue 80).
        var offsets = new Dictionary<int, (long Offset, int Generation)>();
        foreach (KeyValuePair<int, PdfObject> kvp in document.Objects.OrderBy(p => p.Key))
        {
            if (liveObjects is not null && !liveObjects.Contains(kvp.Key)) continue; // GC: skip dead objects
            offsets[kvp.Key] = (stream.Position, kvp.Value.GenerationNumber);
            stream.Write(SerializeIndirectObject(kvp.Key, kvp.Value.GenerationNumber, kvp.Value));
        }

        // Cross-reference table
        long xrefOffset = stream.Position;
        int size = (offsets.Count == 0 ? 0 : offsets.Keys.Max()) + 1;
        stream.Write(BuildXrefTable(offsets, size));

        // Trailer
        var t = new StringBuilder();
        t.Append("trailer\n<<\n");
        t.Append($"  /Size {size}\n");
        t.Append($"  /Root {document.Trailer.Root!.ToPdfString()}\n");
        if (document.Trailer.Info is { } info) t.Append($"  /Info {info.ToPdfString()}\n");
        if (document.Trailer.Id is { } id) t.Append($"  /ID {id.ToPdfString()}\n");
        t.Append(">>\n");
        t.Append($"startxref\n{xrefOffset}\n%%EOF\n");
        stream.Write(Encoding.ASCII.GetBytes(t.ToString()));
    }

    /// <summary>The classic cross-reference table. Every in-use entry carries the SAME generation as
    /// the <c>N G obj</c> header <see cref="Write"/> emitted for that object (ISO 32000-1 7.5.4).
    ///
    /// <para>Issue 80: this used to hardcode <c>00000</c>. Objects at a non-zero generation are
    /// ordinary in an incrementally-updated source file, so the table then contradicted the body it
    /// described. Pellucid's own reader and pypdf both tolerate the mismatch; PDFBox does not, and
    /// veraPDF consequently reported such a save as "doesn't appear to be a valid PDF" / "appears to
    /// be an encrypted PDF file" and could not check it at all — six of one corpus family's twelve
    /// documents. Normalising every object down to generation 0 would have been the other way to make
    /// the two agree, but it is NOT safe here: live <c>N 1 R</c> references elsewhere in the document
    /// would stop resolving unless every reference were rewritten too.</para></summary>
    private static byte[] BuildXrefTable(Dictionary<int, (long Offset, int Generation)> offsets, int size)
    {
        var sb = new StringBuilder();
        sb.Append("xref\n");
        sb.Append($"0 {size}\n");
        sb.Append("0000000000 65535 f \n"); // object 0: head of free list
        for (var n = 1; n < size; n++)
        {
            if (!offsets.TryGetValue(n, out (long Offset, int Generation) e))
            {
                sb.Append("0000000000 00000 f \n"); // gap -> free entry
                continue;
            }

            // Every entry is exactly 20 bytes, so the generation field must stay five digits. 65535
            // is the maximum a generation can legally reach (ISO 32000-1 7.5.4); clamping a malformed
            // larger value keeps the table's fixed width rather than shifting every following entry's
            // byte position and corrupting the whole file.
            int generation = Math.Clamp(e.Generation, 0, 65535);
            sb.Append($"{e.Offset:D10} {generation:D5} n \n");
        }
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
