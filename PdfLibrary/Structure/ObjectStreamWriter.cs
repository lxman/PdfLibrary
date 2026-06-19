using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Optimization;

namespace PdfLibrary.Structure;

/// <summary>
/// Writes a PDF document using object streams (/ObjStm) and a cross-reference stream (/XRef).
/// Mirrors the reader's parsers exactly:
///   - <see cref="PdfDocument.ExtractObjectsFromStream"/> for /ObjStm format
///   - <see cref="PdfLibrary.Parsing.PdfXrefParser"/> ParseXRefStream for /XRef format
/// PDF 1.5+ feature; emits %PDF-1.5 minimum.
/// </summary>
internal static class ObjectStreamWriter
{
    private static readonly byte[] BinaryMarker = [0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]; // "%âãÏÓ\n"

    /// <summary>
    /// Packs (non-stream, gen-0) objects into one /ObjStm.
    /// Returns the stream object and a map of objectNumber → index-within-stream.
    /// Mirrors PdfDocument.ExtractObjectsFromStream.
    /// </summary>
    public static (PdfStream objStm, Dictionary<int, int> indexInStream) BuildObjectStream(
        IReadOnlyList<(int num, PdfObject obj)> eligible)
    {
        var header = new StringBuilder();
        var body = new StringBuilder();
        var indexInStream = new Dictionary<int, int>();

        for (var i = 0; i < eligible.Count; i++)
        {
            (int num, PdfObject obj) = eligible[i];
            indexInStream[num] = i;
            // offset is body position at the time this object starts (relative to /First)
            header.Append($"{num} {body.Length} ");
            body.Append(obj.ToPdfString()).Append('\n');
        }

        byte[] headerBytes = Encoding.ASCII.GetBytes(header.ToString());
        byte[] bodyBytes = Encoding.ASCII.GetBytes(body.ToString());
        byte[] payload = headerBytes.Concat(bodyBytes).ToArray();

        var dict = new PdfDictionary();
        dict[new PdfName("Type")] = new PdfName("ObjStm");
        dict[new PdfName("N")] = new PdfInteger(eligible.Count);
        dict[new PdfName("First")] = new PdfInteger(headerBytes.Length);

        // PdfStream constructor sets /Length from raw data; SetEncodedData replaces with compressed
        var stream = new PdfStream(dict, payload);
        stream.SetEncodedData(payload, "FlateDecode");
        return (stream, indexInStream);
    }

    /// <summary>
    /// Builds a /XRef stream over entries[objectNumber] = (type, field2, field3).
    /// Mirrors PdfXrefParser.ParseXRefStream / ParseXRefStreamEntries.
    /// </summary>
    public static PdfStream BuildXrefStream(
        (int type, long f2, long f3)[] entries,
        PdfIndirectReference root,
        PdfIndirectReference? info)
    {
        // w1: bytes needed for the largest offset or objStmNum
        int w1 = BytesFor(entries.Length == 0 ? 0 : entries.Max(e => e.f2));
        if (w1 == 0) w1 = 1;
        // w2: bytes for the largest generation / index-in-stream value
        int w2 = BytesFor(entries.Length == 0 ? 0 : entries.Max(e => Math.Max(e.f3, 0)));
        if (w2 == 0) w2 = 1;
        const int w0 = 1; // type field is always 1 byte

        var data = new byte[entries.Length * (w0 + w1 + w2)];
        var p = 0;
        foreach ((int type, long f2, long f3) in entries)
        {
            WriteBigEndian(data, ref p, type, w0);
            WriteBigEndian(data, ref p, f2, w1);
            WriteBigEndian(data, ref p, f3, w2);
        }

        var dict = new PdfDictionary();
        dict[new PdfName("Type")] = new PdfName("XRef");
        dict[new PdfName("Size")] = new PdfInteger(entries.Length);
        dict[new PdfName("W")] = new PdfArray { new PdfInteger(w0), new PdfInteger(w1), new PdfInteger(w2) };
        dict[new PdfName("Root")] = root;
        if (info is not null) dict[new PdfName("Info")] = info;

        var stream = new PdfStream(dict, data);
        stream.SetEncodedData(data, "FlateDecode");
        return stream;
    }

    /// <summary>
    /// Writes the document using object streams + a cross-reference stream (PDF 1.5+).
    /// Unencrypted documents only.
    /// </summary>
    public static void Write(PdfDocument document, Stream stream, ISet<int>? liveObjects = null)
    {
        if (document.IsEncrypted)
            throw new NotSupportedException("Encrypted documents are not supported.");
        if (document.Trailer.Root is null)
            throw new InvalidOperationException("Document has no /Root catalog; cannot serialize.");

        document.MaterializeAllObjects();

        // Collect objects (filtered by live set if provided)
        List<(int num, PdfObject obj)> objects = document.Objects
            .Where(kv => liveObjects is null || liveObjects.Contains(kv.Key))
            .OrderBy(kv => kv.Key)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();

        if (objects.Count == 0)
            throw new InvalidOperationException("Document has no objects to serialize.");

        int maxNum = objects.Max(o => o.num);
        int objStmNum = maxNum + 1;   // ObjStm object number
        int xrefNum = maxNum + 2;     // XRef stream object number
        int size = xrefNum + 1;       // /Size = total number of object slots (0..size-1)

        // Split: eligible for packing = non-stream objects with gen 0
        // Streams (content/images/etc.) and gen>0 objects stay as regular indirect objects
        var eligible = objects.Where(o => o.obj is not PdfStream && o.obj.GenerationNumber == 0).ToList();
        var regular = objects.Where(o => o.obj is PdfStream || o.obj.GenerationNumber != 0).ToList();

        // Build the ObjStm (may be empty if no eligible objects, but that's valid)
        (PdfStream objStm, Dictionary<int, int> idxInStm) = BuildObjectStream(eligible);

        // Initialize the xref entries: all free (type 0) by default
        // obj 0 is always free with generation 65535 per PDF spec
        var entries = new (int type, long f2, long f3)[size];
        entries[0] = (0, 0, 65535); // obj 0: head of free list
        for (var i = 1; i < size; i++) entries[i] = (0, 0, 0); // other gaps: free

        // Write PDF header (object/xref streams require ≥ PDF 1.5)
        string versionStr = document.Version.ToString();
        bool needsBump = string.CompareOrdinal(versionStr, "1.5") < 0;
        stream.Write(Encoding.ASCII.GetBytes($"%PDF-{(needsBump ? "1.5" : versionStr)}\n"));
        stream.Write(BinaryMarker);

        // Write regular (non-packable) objects as direct indirect objects
        foreach ((int num, PdfObject obj) in regular)
        {
            entries[num] = (1, stream.Position, obj.GenerationNumber);
            stream.Write(PdfDocumentSerializer.SerializeIndirectObject(num, obj.GenerationNumber, obj));
        }

        // Write the ObjStm itself as a regular indirect object
        entries[objStmNum] = (1, stream.Position, 0);
        stream.Write(PdfDocumentSerializer.SerializeIndirectObject(objStmNum, 0, objStm));

        // Mark all packed objects as type 2 (compressed), pointing to objStmNum
        foreach ((int num, _) in eligible)
            entries[num] = (2, objStmNum, idxInStm[num]);

        // Write the XRef stream
        long xrefOffset = stream.Position;
        entries[xrefNum] = (1, xrefOffset, 0);
        PdfStream xref = BuildXrefStream(entries, document.Trailer.Root!, document.Trailer.Info);
        if (document.Trailer.Id is { } id) xref.Dictionary[new PdfName("ID")] = id;
        stream.Write(PdfDocumentSerializer.SerializeIndirectObject(xrefNum, 0, xref));

        // startxref + %%EOF
        stream.Write(Encoding.ASCII.GetBytes($"\nstartxref\n{xrefOffset}\n%%EOF\n"));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Returns the minimum number of bytes needed to represent <paramref name="v"/>.</summary>
    private static int BytesFor(long v)
    {
        if (v <= 0) return 1;
        var n = 0;
        while (v > 0) { v >>= 8; n++; }
        return n;
    }

    private static void WriteBigEndian(byte[] data, ref int p, long value, int width)
    {
        for (int i = width - 1; i >= 0; i--)
            data[p++] = (byte)((value >> (8 * i)) & 0xFF);
    }
}
