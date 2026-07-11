using System.Collections.Generic;
using System.IO;
using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Structure;

/// <summary>
/// Incremental-update object-stream supersession (ISO 32000-1 7.5.7 + 7.5.8). When a file is updated
/// incrementally, the same object number can appear in more than one revision's object stream — an older
/// stream keeps a now-superseded copy. The cross-reference chain (newest-wins) is authoritative about which
/// stream owns each object; loading the object from the wrong (older) stream would return stale data.
/// The bug this guards: <c>ExtractObjectsFromStream</c> used to cache <em>every</em> object in whatever
/// stream it opened, so resolving an object that happens to share an old stream with a superseded object
/// would overwrite the current object with the old copy — and the result depended on access order.
/// The fixture is hand-built with unfiltered xref and object streams so it is byte-exact and corpus-free.
/// </summary>
public class IncrementalObjStmTests
{
    [Fact]
    public void Object_superseded_in_a_newer_stream_wins_regardless_of_access_order()
    {
        byte[] pdf = BuildIncrementalPdf();
        using var doc = PdfDocument.Load(new MemoryStream(pdf), leaveOpen: false);

        // The merged xref must place object 5 in the newer object stream (8), not the old one (7).
        PdfXrefEntry? entry = doc.XrefTable.GetEntry(5);
        Assert.NotNull(entry);
        Assert.Equal(PdfXrefEntryType.Compressed, entry!.EntryType);
        Assert.Equal(8, entry.ByteOffset); // containing object-stream number

        // Resolve object 4 FIRST — it lives in the old stream (7), which also physically contains the
        // superseded object 5. Extracting stream 7 must not clobber object 5's current copy.
        PdfObject? o4 = doc.ResolveReference(new PdfIndirectReference(4, 0));
        Assert.Equal(4, IntEntry(o4, "Marker"));

        // Object 5 must still resolve to its current (revision 2) copy.
        PdfObject? o5 = doc.ResolveReference(new PdfIndirectReference(5, 0));
        Assert.Equal(2, IntEntry(o5, "Rev"));
    }

    private static long IntEntry(PdfObject? obj, string key) =>
        obj is PdfDictionary d && d.Get(key) is PdfInteger i ? i.LongValue : -1;

    private static byte[] BuildIncrementalPdf()
    {
        var bytes = new List<byte>();
        var offset = new Dictionary<int, int>();
        void Append(string s) => bytes.AddRange(Encoding.Latin1.GetBytes(s));
        void StartObj(int n) { offset[n] = bytes.Count; Append($"{n} 0 obj\n"); }

        Append("%PDF-1.5\n");

        StartObj(1);
        Append("<</Type/Catalog/Pages 2 0 R>>\nendobj\n");
        StartObj(2);
        Append("<</Type/Pages/Kids[3 0 R]/Count 1>>\nendobj\n");
        StartObj(3);
        Append("<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]>>\nendobj\n");

        // ── revision 1 ──────────────────────────────────────────────────────────────────────────────────
        // Object stream 7 holds object 4 and the OLD object 5 (/Rev 1).
        const string obj4Data = "<</Marker 4>>";
        const string obj5Old = "<</Rev 1>>";
        string headerA = $"4 0 5 {obj4Data.Length}\n"; // "objNum offset" pairs; obj4 at 0, obj5 after obj4
        string bodyA = headerA + obj4Data + obj5Old;
        StartObj(7);
        Append($"<</Type/ObjStm/N 2/First {headerA.Length}/Length {bodyA.Length}>>\nstream\n");
        Append(bodyA);
        Append("\nendstream\nendobj\n");

        // Cross-reference stream 9 (revision 1), /W[1 2 1], /Index[0 10].
        byte[] xref1 = XrefBytes(
            Free(),                       // 0
            Uncompressed(offset[1]),      // 1
            Uncompressed(offset[2]),      // 2
            Uncompressed(offset[3]),      // 3
            Compressed(7, 0),             // 4 → stream 7, index 0
            Compressed(7, 1),             // 5 → stream 7, index 1 (OLD)
            Free(),                       // 6
            Uncompressed(offset[7]),      // 7
            Free(),                       // 8 (not present in rev 1)
            Placeholder());               // 9 → patched to its own offset below
        offset[9] = bytes.Count;
        // object 9's own entry is the last (index 9); patch its offset now that we know it.
        PatchUncompressed(xref1, 9, offset[9]);
        Append("9 0 obj\n");
        Append($"<</Type/XRef/W[1 2 1]/Index[0 10]/Size 10/Root 1 0 R/Length {xref1.Length}>>\nstream\n");
        bytes.AddRange(xref1);
        Append("\nendstream\nendobj\n");
        int startxref1 = offset[9];
        Append($"startxref\n{startxref1}\n%%EOF\n");

        // ── revision 2 (incremental) ─────────────────────────────────────────────────────────────────────
        // Object stream 8 holds the NEW object 5 (/Rev 2), superseding the copy in stream 7.
        const string obj5New = "<</Rev 2>>";
        string headerB = "5 0\n";
        string bodyB = headerB + obj5New;
        StartObj(8);
        Append($"<</Type/ObjStm/N 1/First {headerB.Length}/Length {bodyB.Length}>>\nstream\n");
        Append(bodyB);
        Append("\nendstream\nendobj\n");

        // Cross-reference stream 10 (revision 2), /Index[5 1 8 1 10 1], /Prev → rev 1.
        byte[] xref2 = XrefBytes(
            Compressed(8, 0),             // 5 → stream 8, index 0 (NEW, supersedes)
            Uncompressed(offset[8]),      // 8
            Placeholder());               // 10 → patched below
        offset[10] = bytes.Count;
        PatchUncompressed(xref2, 2, offset[10]); // index 2 within this xref = object 10's entry
        Append("10 0 obj\n");
        Append($"<</Type/XRef/W[1 2 1]/Index[5 1 8 1 10 1]/Size 11/Root 1 0 R/Prev {startxref1}/Length {xref2.Length}>>\nstream\n");
        bytes.AddRange(xref2);
        Append("\nendstream\nendobj\n");
        Append($"startxref\n{offset[10]}\n%%EOF");

        return [.. bytes];
    }

    // ── xref-stream entry helpers (/W = [1 2 1]) ──────────────────────────────────────────────────────
    private static byte[] Free() => [0, 0, 0, 0];
    private static byte[] Uncompressed(int off) => [1, (byte)(off >> 8), (byte)off, 0];
    private static byte[] Compressed(int stream, int index) => [2, (byte)(stream >> 8), (byte)stream, (byte)index];
    private static byte[] Placeholder() => [1, 0, 0, 0];

    private static byte[] XrefBytes(params byte[][] entries)
    {
        var b = new List<byte>();
        foreach (byte[] e in entries) b.AddRange(e);
        return [.. b];
    }

    /// <summary>Patches the 4-byte entry at <paramref name="index"/> to an uncompressed entry for
    /// <paramref name="off"/> (used for an xref stream's own self-referential entry).</summary>
    private static void PatchUncompressed(byte[] xref, int index, int off)
    {
        int p = index * 4;
        xref[p] = 1;
        xref[p + 1] = (byte)(off >> 8);
        xref[p + 2] = (byte)off;
        xref[p + 3] = 0;
    }
}
