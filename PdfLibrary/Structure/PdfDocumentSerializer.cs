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

    public static void Write(PdfDocument document, Stream stream)
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

        // Body — preserve original object numbers; record byte offsets.
        var offsets = new Dictionary<int, long>();
        foreach (KeyValuePair<int, PdfObject> kvp in document.Objects.OrderBy(p => p.Key))
        {
            offsets[kvp.Key] = stream.Position;
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

    private static byte[] BuildXrefTable(Dictionary<int, long> offsets, int size)
    {
        var sb = new StringBuilder();
        sb.Append("xref\n");
        sb.Append($"0 {size}\n");
        sb.Append("0000000000 65535 f \n"); // object 0: head of free list
        for (var n = 1; n < size; n++)
        {
            sb.Append(offsets.TryGetValue(n, out long off)
                ? $"{off:D10} 00000 n \n"
                : "0000000000 00000 f \n"); // gap -> free entry
        }
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
