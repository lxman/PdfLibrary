using System.Collections.Generic;
using System.IO;
using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Structure;

/// <summary>
/// Hybrid-reference files (ISO 32000-1:2008, 7.5.8.4). A hybrid file carries both a classic
/// cross-reference table — which marks compressed objects <c>free</c> so a pre-1.5 reader ignores them —
/// and an <c>/XRefStm</c> cross-reference stream that gives those objects their real (type 2) location in
/// an object stream. A conforming 1.5+ reader must follow the trailer's <c>/XRefStm</c> and let its entries
/// supersede the classic table's free markers; a reader that follows only <c>/Prev</c> silently loses every
/// compressed object (this is what made the PDF/UA <c>PDFUA-Ref-2-09_Scanned</c> reference file report a
/// missing /StructTreeRoot). The fixture is hand-built with an unfiltered xref stream and object stream so
/// it is byte-exact and needs no corpus.
/// </summary>
public class HybridXrefTests
{
    [Fact]
    public void Object_reachable_only_via_XRefStm_resolves()
    {
        byte[] pdf = BuildHybridPdf();
        using var doc = PdfDocument.Load(new MemoryStream(pdf), leaveOpen: false);

        // Object 5 is marked FREE by the classic table and lives (type 2) in object stream 6, indexed only
        // by the /XRefStm cross-reference stream. It must resolve to its dictionary, not null.
        PdfObject? structTreeRoot = doc.ResolveReference(new PdfIndirectReference(5, 0));
        var dict = Assert.IsType<PdfDictionary>(structTreeRoot);
        Assert.Equal("StructTreeRoot", (dict.Get("Type") as PdfName)?.Value);

        // And it is reachable through the catalog the same way a rule would reach it.
        PdfCatalog? catalog = doc.GetCatalog();
        Assert.NotNull(catalog);
        PdfObject? viaCatalog = doc.ResolveReference(catalog!.Dictionary.Get("StructTreeRoot") as PdfIndirectReference
                                                     ?? new PdfIndirectReference(-1, 0));
        Assert.IsType<PdfDictionary>(viaCatalog);

        // The xref entry for object 5 is a compressed (type 2) entry pointing into object stream 6.
        PdfXrefEntry? entry = doc.XrefTable.GetEntry(5);
        Assert.NotNull(entry);
        Assert.Equal(PdfXrefEntryType.Compressed, entry!.EntryType);
        Assert.True(entry.IsInUse);
        Assert.Equal(6, entry.ByteOffset); // field2 of a type-2 entry = containing object-stream number
    }

    /// <summary>
    /// Assembles a minimal but well-formed hybrid-reference PDF:
    /// objects 1–3 (catalog/pages/page) and 6 (the object stream) are classic uncompressed entries;
    /// object 4 is an unfiltered <c>/Type/XRef</c> stream located by the trailer's <c>/XRefStm</c>;
    /// object 5 (the /StructTreeRoot) lives inside object stream 6 and is indexed only by that xref stream,
    /// while the classic table marks object 5 free.
    /// </summary>
    private static byte[] BuildHybridPdf()
    {
        var bytes = new List<byte>();
        var offset = new Dictionary<int, long>();

        void Append(string s) => bytes.AddRange(Encoding.Latin1.GetBytes(s));
        void StartObj(int n) { offset[n] = bytes.Count; Append($"{n} 0 obj\n"); }

        Append("%PDF-1.5\n");

        StartObj(1);
        Append("<</Type/Catalog/Pages 2 0 R/StructTreeRoot 5 0 R>>\nendobj\n");
        StartObj(2);
        Append("<</Type/Pages/Kids[3 0 R]/Count 1>>\nendobj\n");
        StartObj(3);
        Append("<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]>>\nendobj\n");

        // Object stream 6 holds compressed object 5. Header is one "objNum offsetWithinData" pair; the
        // object data follows at /First. Unfiltered, so the bytes are literal.
        const string obj5Data = "<</Type/StructTreeRoot/K[]>>";
        string objStmHeader = "5 0\n";           // one pair: object 5 at offset 0 within the data section
        int first = objStmHeader.Length;         // /First = length of the header section
        string objStmBody = objStmHeader + obj5Data;
        StartObj(6);
        Append($"<</Type/ObjStm/N 1/First {first}/Length {objStmBody.Length}>>\nstream\n");
        Append(objStmBody);
        Append("\nendstream\nendobj\n");

        // Cross-reference stream (object 4), unfiltered, /W[1 2 1], one entry for object 5:
        // type 2 (compressed), field2 = containing object-stream number (6), field3 = index in stream (0).
        byte[] xrefStmData = [0x02, 0x00, 0x06, 0x00];
        offset[4] = bytes.Count;
        Append("4 0 obj\n");
        Append($"<</Type/XRef/W[1 2 1]/Index[5 1]/Size 7/Length {xrefStmData.Length}>>\nstream\n");
        bytes.AddRange(xrefStmData);
        Append("\nendstream\nendobj\n");

        // Classic cross-reference table for objects 0–6. Object 5 is marked FREE (the hybrid convention);
        // its real location comes only from the /XRefStm stream above. Each entry is exactly 20 bytes.
        long xrefStart = bytes.Count;
        Append("xref\n0 7\n");
        Append("0000000000 65535 f\r\n");                 // object 0: free-list head
        Append($"{offset[1]:D10} 00000 n\r\n");
        Append($"{offset[2]:D10} 00000 n\r\n");
        Append($"{offset[3]:D10} 00000 n\r\n");
        Append($"{offset[4]:D10} 00000 n\r\n");
        Append("0000000000 65535 f\r\n");                 // object 5: free in the classic table (hybrid)
        Append($"{offset[6]:D10} 00000 n\r\n");
        Append($"trailer\n<</Size 7/Root 1 0 R/XRefStm {offset[4]}>>\n");
        Append($"startxref\n{xrefStart}\n%%EOF");

        return [.. bytes];
    }
}
